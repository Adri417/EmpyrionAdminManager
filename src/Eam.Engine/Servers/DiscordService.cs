using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Eam.Core.Servers;
using Eam.Engine.Api;

namespace Eam.Engine.Servers;

/// <summary>
/// Step 7: Discord bridge with a bot of the admin's own Discord application.
/// Public channel both ways (global chat), one channel per faction both ways, admin channel one way (alerts).
/// Discord is read by polling the channels every few seconds (no permanent connection to keep alive).
/// The bot needs the "Message Content Intent" and the permissions to read and send messages in these channels.
/// </summary>
public sealed class DiscordService : BackgroundService
{
    private const string ApiBase = "https://discord.com/api/v10";
    private readonly ServerManager _manager;
    private readonly LiveService _live;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<DiscordService> _log;
    private readonly ConcurrentDictionary<string, string> _lastMessage = new();
    private readonly ConcurrentDictionary<long, string> _status = new();

    public DiscordService(ServerManager manager, LiveService live, Alerts alerts, IHttpClientFactory http, ILogger<DiscordService> log)
    {
        _manager = manager;
        _live = live;
        _http = http;
        _log = log;
        _live.ChatReceived += OnChatAsync;
        alerts.ToDiscordAdmin = AdminAsync;
    }

    public string? Status(long id) => _status.TryGetValue(id, out var s) ? s : null;

    private CommunitySettings Settings(ServerDefinition def) => FeatureStore.Load<CommunitySettings>(_live.Data(def), "community");

    private HttpClient Client(string token)
    {
        var c = _http.CreateClient("web");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("EmpyrionAdminManager (local, 1.0)");
        return c;
    }

    /// <summary>Discord markup and mentions are neutralised: a player cannot ping @everyone.</summary>
    internal static string Safe(string text) =>
        text.Replace("@", "@​").Replace("`", "'").Replace("*", "\\*").Replace("_", "\\_").Replace("~", "\\~");

    public async Task SendAsync(string token, string channel, string content)
    {
        if (channel.Length == 0) return;
        using var c = Client(token);
        var body = new { content = content.Length > 1900 ? content[..1900] : content, allowed_mentions = new { parse = Array.Empty<string>() } };
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var res = await c.PostAsJsonAsync($"{ApiBase}/channels/{channel}/messages", body);
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = 1.0;
                try { wait = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("retry_after").GetDouble(); } catch { }
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(wait, 0.5, 10)));
                continue;
            }
            if (!res.IsSuccessStatusCode) {
                var detail = await res.Content.ReadAsStringAsync();
                throw new ServerException("discord_error", $"{(int)res.StatusCode} {(detail.Length > 150 ? detail[..150] : detail)}");
            }
            return;
        }
    }

    private async Task AdminAsync(ServerDefinition def, string text)
    {
        var s = Settings(def);
        if (!s.DiscordEnabled || s.DiscordToken.Length == 0 || s.DiscordAdminChannel.Length == 0) return;
        try { await SendAsync(s.DiscordToken, s.DiscordAdminChannel, "⚠️ " + Safe(text)); }
        catch (Exception e) { _log.LogDebug("Discord admin message failed: {Err}", e.Message); }
    }

    /// <summary>Test button of the settings page.</summary>
    public async Task TestAsync(long id)
    {
        var def = _manager.Definition(id);
        var s = Settings(def);
        if (s.DiscordToken.Length == 0) throw new ServerException("discord_token_missing");
        foreach (var ch in new[] { s.DiscordPublicChannel, s.DiscordAdminChannel }.Concat(s.DiscordFactionChannels.Select(f => f.ChannelId)).Where(c => c.Length > 0).Distinct())
            await SendAsync(s.DiscordToken, ch, $"✅ Empyrion Admin Manager — {Safe(def.Name)}");
    }

    private async Task OnChatAsync(ServerDefinition def, ChatEntry entry, LivePlayer? sender)
    {
        var s = Settings(def);
        if (!s.DiscordEnabled || s.DiscordToken.Length == 0) return;
        string? channel = null;
        if (entry.Channel == "Global") channel = s.DiscordPublicChannel;
        else if (entry.Channel == "Faction" && sender is not null)
            channel = s.DiscordFactionChannels.FirstOrDefault(f => f.FactionId == sender.FactionId)?.ChannelId;
        if (string.IsNullOrEmpty(channel)) return;
        try { await SendAsync(s.DiscordToken, channel, $"**{Safe(entry.SenderName)}** : {Safe(entry.Text)}"); }
        catch (Exception e) { _status[def.Id] = e.Message; }
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));
        while (await timer.WaitForNextTickAsync(stop))
        {
            foreach (var def in _manager.Definitions())
            {
                CommunitySettings s;
                try { s = Settings(def); } catch { continue; }
                if (!s.DiscordEnabled || s.DiscordToken.Length == 0 || !_live.IsLinked(def.Id)) continue;
                try
                {
                    if (s.DiscordPublicChannel.Length > 0) await PollAsync(def, s, s.DiscordPublicChannel, null);
                    foreach (var f in s.DiscordFactionChannels) await PollAsync(def, s, f.ChannelId, f.FactionId);
                    _status[def.Id] = "ok";
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _status[def.Id] = e.Message;
                    _log.LogDebug("Discord poll of '{Name}' failed: {Err}", def.Name, e.Message);
                }
            }
        }
    }

    private async Task PollAsync(ServerDefinition def, CommunitySettings s, string channel, int? factionId)
    {
        using var c = Client(s.DiscordToken);
        var key = def.Id + ":" + channel;
        if (!_lastMessage.TryGetValue(key, out var after))
        {
            // First look: start after the latest message, never replay the channel's history in game.
            var latest = await c.GetFromJsonAsync<JsonElement>($"{ApiBase}/channels/{channel}/messages?limit=1");
            _lastMessage[key] = latest.ValueKind == JsonValueKind.Array && latest.GetArrayLength() > 0 ? latest[0].GetProperty("id").GetString()! : "0";
            return;
        }
        using var res = await c.GetAsync($"{ApiBase}/channels/{channel}/messages?after={after}&limit=20");
        if (res.StatusCode == HttpStatusCode.TooManyRequests) return;
        if (!res.IsSuccessStatusCode) throw new ServerException("discord_error", $"{(int)res.StatusCode}");
        var list = await res.Content.ReadFromJsonAsync<JsonElement>();
        if (list.ValueKind != JsonValueKind.Array) return;
        foreach (var m in list.EnumerateArray().Reverse()) // oldest first
        {
            var id = m.GetProperty("id").GetString()!;
            if (ulong.TryParse(id, out var n) && ulong.TryParse(_lastMessage[key], out var last) && n <= last) continue;
            _lastMessage[key] = id;
            var author = m.GetProperty("author");
            if (author.TryGetProperty("bot", out var bot) && bot.ValueKind == JsonValueKind.True) continue;
            var name = author.TryGetProperty("global_name", out var gn) && gn.ValueKind == JsonValueKind.String ? gn.GetString()! : author.GetProperty("username").GetString()!;
            var text = (m.TryGetProperty("content", out var ct) ? ct.GetString() : "")?.Replace("\n", " ").Trim() ?? "";
            if (text.Length == 0) continue;
            if (text.Length > 200) text = text[..200];
            if (factionId is { } fid)
                await _live.SendToModAsync(def.Id, new { op = "msg_faction", id = fid, text = $"[Discord] {name}: {text}", prio = 1, secs = 15 });
            else
                await _live.SendAsync(def.Id, $"[Discord] {name}: {text}", "chat", null, "Discord");
        }
    }
}
