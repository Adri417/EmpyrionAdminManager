using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Eam.Link;

namespace Eam.Core.Servers;

/// <summary>Engine side of the EAM link: connects to the mod of one server on 127.0.0.1.</summary>
public sealed class LinkClient : IAsyncDisposable
{
    private readonly TcpClient _tcp = new() { NoDelay = true };
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Largest message accepted from the mod. Answers can be big (the structure list of a busy server is
    /// several MB): the 1 MB limit of the mod's own input would cut the link at every structure refresh.
    /// </summary>
    public const int MaxIncomingLength = 256 * 1024 * 1024;

    /// <summary>Completes when the link is closed (requests waiting for an answer then fail at once).</summary>
    public Task Closed => _closed.Task;

    public bool Connected => _tcp.Connected && _stream is not null;

    /// <summary>Connects and authenticates. Throws <see cref="UnauthorizedAccessException"/> on a wrong key.</summary>
    public static async Task<LinkClient> ConnectAsync(int port, string key, CancellationToken ct)
    {
        var c = new LinkClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await c._tcp.ConnectAsync("127.0.0.1", port, timeout.Token);
            var stream = c._tcp.GetStream();
            var reader = new StreamReader(stream, new UTF8Encoding(false));
            var greeting = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("link_closed");
            var parts = greeting.Split(' ');
            if (parts.Length != 3 || parts[0] != LinkProtocol.Greeting) throw new IOException("link_bad_greeting");
            await stream.WriteAsync(Encoding.UTF8.GetBytes("AUTH " + LinkProtocol.Sign(key, parts[2]) + "\n"), timeout.Token);
            var ok = await reader.ReadLineAsync(timeout.Token);
            if (ok != "OK") throw new UnauthorizedAccessException("link_rejected");
            c._stream = stream;
            c._reader = reader;
            return c;
        }
        catch
        {
            await c.DisposeAsync();
            throw;
        }
    }

    public async Task SendAsync(object message, CancellationToken ct = default)
    {
        var stream = _stream ?? throw new IOException("link_closed");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await _writeLock.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(bytes, ct);
            await stream.WriteAsync("\n"u8.ToArray(), ct);
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>Reads the next message, or null when the connection is closed.</summary>
    public async Task<JsonElement?> ReceiveAsync(CancellationToken ct)
    {
        var reader = _reader ?? throw new IOException("link_closed");
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) return null;
            if (line.Length == 0) continue;
            if (line.Length > MaxIncomingLength) throw new IOException("line_too_long");
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.Clone();
        }
    }

    public ValueTask DisposeAsync()
    {
        try { _tcp.Dispose(); } catch { }
        _stream = null;
        _reader = null;
        _closed.TrySetResult();
        return ValueTask.CompletedTask;
    }
}
