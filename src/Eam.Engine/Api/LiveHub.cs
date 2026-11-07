using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Eam.Core.Monitoring;

namespace Eam.Engine.Api;

/// <summary>
/// Pushes live updates (system load now; chat, players and alerts in later steps)
/// to every open window over a WebSocket. Nothing is measured while nobody is watching.
/// </summary>
public sealed class LiveHub(SystemMonitor monitor, ILogger<LiveHub> log) : BackgroundService
{
    private sealed record Client(WebSocket Socket, SemaphoreSlim Gate);
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public int ClientCount => _clients.Count;

    public async Task Accept(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        // Same-origin only: a foreign page must not open a socket with the admin's cookie.
        var origin = ctx.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var o)
                                  || !string.Equals(o.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase)))
        {
            ctx.Response.StatusCode = 403;
            return;
        }

        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var id = Guid.NewGuid();
        var client = new Client(ws, new SemaphoreSlim(1, 1));
        _clients[id] = client;
        try
        {
            await SendAsync(client, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "system", data = monitor.Take() }, Json)), ctx.RequestAborted);
            var buf = new byte[1024];
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buf, ctx.RequestAborted);
                if (r.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
        finally
        {
            _clients.TryRemove(id, out _);
            if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); } catch { }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        monitor.Take(); // first CPU sample
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(stop))
        {
            if (_clients.IsEmpty) continue;
            try { await BroadcastAsync(new { type = "system", data = monitor.Take() }, stop); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Live update failed"); }
        }
    }

    public async Task BroadcastAsync(object message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, Json));
        foreach (var (id, client) in _clients)
        {
            try { await SendAsync(client, bytes, ct); }
            catch { _clients.TryRemove(id, out _); }
        }
    }

    // A WebSocket accepts only one send at a time: messages from different sources are queued per client.
    private static async Task SendAsync(Client c, byte[] bytes, CancellationToken ct)
    {
        await c.Gate.WaitAsync(ct);
        try { await c.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
        finally { c.Gate.Release(); }
    }
}
