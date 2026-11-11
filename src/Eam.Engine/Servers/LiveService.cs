using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Eam.Core.Servers;
using Eam.Engine.Api;

namespace Eam.Engine.Servers;

public sealed class LivePlayer
{
    public int Id { get; init; }
    public string? SteamId { get; set; }
    public string Name { get; set; } = "";
    public string? Playfield { get; set; }
    public int FactionId { get; set; }
    public string? Faction { get; set; }
    public int Ping { get; set; }
    public float Health { get; set; }
    public float HealthMax { get; set; }
    public double Credits { get; set; }
    public int Level { get; set; }
    public int Exp { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int Origin { get; set; }
    public int Permission { get; set; }
    public DateTime OnlineSinceUtc { get; set; } = DateTime.UtcNow;
    internal DateTime PlaytimeCountedUtc = DateTime.UtcNow;
}

public sealed class LivePlayfield
{
    public string Name { get; init; } = "";
    public int ProcessId { get; set; }
    public int Players { get; set; }
    public float Fps { get; set; }
    public int MemoryMb { get; set; }
    public int Structures { get; set; }
    public int Mobs { get; set; }
    public int UptimeSeconds { get; set; }
}

/// <summary>Everything EAM currently knows about one running server.</summary>
public sealed class ServerLive
{
    public readonly object Gate = new();
    /// <summary>Players who really just logged in (not players already there when EAM or the mod restarted).</summary>
    public readonly HashSet<int> JustConnected = [];
    public bool Connected;
    public string? ModVersion;
    public DateTime LastHandlerErrorUtc;
    public string? LinkError;
    public DateTime? ConnectedUtc;
    public readonly Dictionary<int, LivePlayer> Players = new();
    public readonly Dictionary<string, LivePlayfield> Playfields = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<int, string> Factions = new();
    public readonly Dictionary<int, string> FactionNames = new();
    public float? DediFps;
    public int? DediMemoryMb;
    public int? DediUptime;
    public double CpuPercent;
    public long MemoryBytes;
    public int ProcessCount;
    internal TimeSpan LastCpu;
    internal DateTime LastCpuSample;
    internal readonly List<(DateTime At, string Text)> RecentlySent = new();
}

/// <summary>
/// Step 3: talks to the EAM mod of every running server, keeps the live picture
/// (players, playfields, performance), records the chat and pushes everything to the open windows.
/// </summary>
public sealed class LiveService(ServerStore store, ServerManager manager, LiveHub hub, ILogger<LiveService> log) : BackgroundService
{
    private readonly ConcurrentDictionary<long, ServerLive> _live = new();
    private readonly ConcurrentDictionary<long, LinkClient> _links = new();
    private readonly ConcurrentDictionary<long, Task> _linkTasks = new();
    private readonly ConcurrentDictionary<long, bool> _dirty = new();
    private readonly ConcurrentDictionary<long, string> _lastPushed = new();
    private readonly ConcurrentDictionary<string, ServerData> _data = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _waiters = new();
    private CancellationToken _stopping;

    public bool IsLinked(long id) => _links.ContainsKey(id);

    /// <summary>
    /// Sends a request to the server's EAM mod and waits for the game's answer (same "rid").
    /// The answer is an "ok", an "error" or the requested data.
    /// </summary>
    public async Task<JsonElement> RequestAsync(long id, Dictionary<string, object?> request, TimeSpan? timeout = null)
    {
        var link = await WaitLinkAsync(id);
        var rid = Guid.NewGuid().ToString("N")[..12];
        request["rid"] = rid;
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[rid] = tcs;
        try
        {
            await link.SendAsync(request);
            var done = await Task.WhenAny(tcs.Task, link.Closed, Task.Delay(timeout ?? TimeSpan.FromSeconds(10)));
            if (done == link.Closed && !tcs.Task.IsCompleted) throw new ServerException("mod_link_lost");
            if (done != tcs.Task) throw new ServerException("game_timeout");
            var answer = tcs.Task.Result;
            if (Str(answer, "ev") == "error") throw new ServerException("game_error", Str(answer, "code"));
            return answer;
        }
        finally { _waiters.TryRemove(rid, out _); }
    }

    /// <summary>
    /// The link of a server; when it is reconnecting (a few seconds), waits for it instead of failing at once.
    /// </summary>
    private async Task<LinkClient> WaitLinkAsync(long id)
    {
        for (var i = 0; i < 40; i++)
        {
            if (_links.TryGetValue(id, out var link)) return link;
            if (!_linkTasks.ContainsKey(id)) break; // no link loop: the server is not running
            await Task.Delay(250);
        }
        throw new ServerException("mod_not_connected");
    }

    /// <summary>Fire-and-forget request to the mod (no answer expected).</summary>
    public async Task SendToModAsync(long id, object request)
    {
        if (!_links.TryGetValue(id, out var link)) throw new ServerException("mod_not_connected");
        await link.SendAsync(request);
    }

    /// <summary>Called when a player joins: messages left while they were offline.</summary>
    public event Func<ServerDefinition, LivePlayer, Task>? PlayerJoined;
    public event Func<ServerDefinition, LivePlayer, Task>? PlayerLeft;
    /// <summary>A player who really just logged in (PlayerJoined also fires for players found online after a restart).</summary>
    public event Func<ServerDefinition, LivePlayer, Task>? PlayerArrived;

    /// <summary>Every message from the mod (steps 6 to 9 listen to it). Handlers run in the background.</summary>
    public event Func<ServerDefinition, string, JsonElement, Task>? ModEvent;

    /// <summary>A chat message written in game (chatbot, filter, alerts, Discord).</summary>
    public event Func<ServerDefinition, ChatEntry, LivePlayer?, Task>? ChatReceived;

    /// <summary>A player currently online, by entity id.</summary>
    public LivePlayer? Online(long id, int entityId)
    {
        var l = Live(id);
        lock (l.Gate) return l.Players.TryGetValue(entityId, out var p) ? p : null;
    }

    public LivePlayer? OnlineBySteam(long id, string steamId)
    {
        var l = Live(id);
        lock (l.Gate) return l.Players.Values.FirstOrDefault(p => p.SteamId == steamId);
    }

    public IReadOnlyList<LivePlayer> OnlinePlayers(long id)
    {
        var l = Live(id);
        lock (l.Gate) return l.Players.Values.ToList();
    }

    public string? FactionName(long id, int factionId)
    {
        var l = Live(id);
        lock (l.Gate) return l.Factions.TryGetValue(factionId, out var f) ? f : null;
    }

    public sealed record FactionInfo(int Id, string Abbrev, string Name);

    public IReadOnlyList<FactionInfo> FactionDetails(long id)
    {
        var l = Live(id);
        lock (l.Gate) return l.Factions.Select(f => new FactionInfo(f.Key, f.Value, l.FactionNames.GetValueOrDefault(f.Key) ?? "")).ToList();
    }

    public IReadOnlyDictionary<int, string> Factions(long id)
    {
        var l = Live(id);
        lock (l.Gate) return new Dictionary<int, string>(l.Factions);
    }

    private void Raise(ServerDefinition def, string ev, JsonElement m)
    {
        if (ModEvent is not { } handlers) return;
        var copy = m.Clone();
        foreach (var h in handlers.GetInvocationList().Cast<Func<ServerDefinition, string, JsonElement, Task>>())
            _ = Task.Run(async () =>
            {
                try { await h(def, ev, copy); }
                catch (Exception e) { log.LogWarning(e, "Handler of '{Ev}' failed", ev); }
            });
    }

    public ServerLive Live(long id) => _live.GetOrAdd(id, _ => new ServerLive());

    public ServerData Data(ServerDefinition def) => _data.GetOrAdd(def.DatabaseFile, f => new ServerData(f));

    /// <summary>Forgets the cached database of a server whose folder moved or was deleted.</summary>
    public void ForgetData(string databaseFile) => _data.TryRemove(databaseFile, out _);

    // ------------------------------------------------------------------ snapshot for the UI

    public object Snapshot(long id)
    {
        var l = Live(id);
        lock (l.Gate)
        {
            return new
            {
                id,
                connected = l.Connected,
                modVersion = l.ModVersion,
                linkError = l.LinkError,
                cpuPercent = Math.Round(l.CpuPercent, 1),
                memoryBytes = l.MemoryBytes,
                processCount = l.ProcessCount,
                fps = l.DediFps,
                dediMemoryMb = l.DediMemoryMb,
                uptime = l.DediUptime,
                players = l.Players.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => new
                {
                    p.Id, p.SteamId, p.Name, p.Playfield, p.FactionId,
                    faction = p.FactionId != 0 && l.Factions.TryGetValue(p.FactionId, out var f) ? f : null,
                    p.Ping, p.Health, p.HealthMax, p.Credits, p.Level, p.Exp,
                    x = Math.Round(p.X), y = Math.Round(p.Y), z = Math.Round(p.Z), p.OnlineSinceUtc,
                }).ToList(),
                playfields = l.Playfields.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => new
                {
                    p.Name, p.ProcessId, p.Players, fps = Math.Round(p.Fps, 1), p.MemoryMb, p.Structures, p.Mobs, p.UptimeSeconds,
                }).ToList(),
            };
        }
    }

    public IReadOnlyList<ChatEntry> Chat(long id, int limit, long? beforeId, string? search = null)
    {
        var def = store.Find(id) ?? throw new ServerException("not_found");
        return Data(def).RecentChat(limit, beforeId, search);
    }

    // ------------------------------------------------------------------ sending messages

    /// <summary>The connected EAM mod can write in the chat through the game's chat API (mod 0.9.22 and later).</summary>
    private static bool ChatApi(ServerLive l0)
    {
        string? v;
        lock (l0.Gate) v = l0.ModVersion;
        return Version.TryParse(v, out var ver) && ver >= new Version(0, 9, 22);
    }

    /// <summary>A server line in the global chat (countdowns, announcements). False when the mod is not connected.</summary>
    public async Task<bool> ServerChatAsync(long id, string text)
    {
        if (!_links.TryGetValue(id, out var link)) return false;
        var safe = text.Replace("'", "’");
        var l = Live(id);
        lock (l.Gate)
        {
            l.RecentlySent.RemoveAll(x => DateTime.UtcNow - x.At > TimeSpan.FromSeconds(30));
            l.RecentlySent.Add((DateTime.UtcNow, safe));
        }
        try
        {
            if (ChatApi(l)) await link.SendAsync(new { op = "chat", text = safe, sender = "Server" });
            else await link.SendAsync(new { op = "console", cmd = $"say '{safe}'" });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Red alert with the alarm sound to every player (false when the EAM mod is not connected).</summary>
    public async Task<bool> RedAlertAsync(long id, string text)
    {
        if (!_links.TryGetValue(id, out var link)) return false;
        try { await link.SendAsync(new { op = "msg_all", text, prio = 0, secs = 8 }); return true; }
        catch { return false; }
    }

    /// <summary>
    /// mode "chat": message in the global chat; "popup": message box on the players' screen
    /// (all players, or one player when <paramref name="playerId"/> is set).
    /// </summary>
    public async Task<ChatEntry?> SendAsync(long id, string text, string mode, int? playerId, string by, bool asServer = false)
    {
        var def = store.Find(id) ?? throw new ServerException("not_found");
        text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (text.Length == 0) throw new ServerException("message_empty");
        if (text.Length > 300) throw new ServerException("message_too_long");
        var rt = manager.Runtime(id);
        if (rt.State != ServerState.Running) throw new ServerException("server_not_running");
        _links.TryGetValue(id, out var link);

        if (mode == "private")
        {
            // Private message in the game chat ("say p:<player> '...'"), kept in EAM's chat history.
            if (playerId is not { } to || Online(id, to) is not { } target) throw new ServerException("player_offline");
            var priv = (!asServer && FeatureStore.Load<CommunitySettings>(Data(def), "community").ShowAdminName ? $"{by}: {text}" : text).Replace("'", "’");
            var lp = Live(id);
            lock (lp.Gate)
            {
                lp.RecentlySent.RemoveAll(x => DateTime.UtcNow - x.At > TimeSpan.FromSeconds(30));
                lp.RecentlySent.Add((DateTime.UtcNow, priv));
            }
            var cmdText = $"say p:{to} '{priv}'";
            if (link is not null && ChatApi(l0: Live(id))) await link.SendAsync(new { op = "chat", text = priv, to, sender = "EAM" });
            else if (link is not null) await link.SendAsync(new { op = "console", cmd = cmdText });
            else await manager.ConsoleAsync(id, cmdText, by);
            var pe = Data(def).AddChat(DateTime.UtcNow, "SinglePlayer", -1, by, null, to, $"→ {target.Name}: {text.Replace("'", "’")}", "eam");
            await hub.BroadcastAsync(new { type = "chat", id, msg = pe }, CancellationToken.None);
            return pe;
        }

        if (mode is "popup" or "alert")
        {
            // "alert" = Empyrion's red box with the alarm sound (priority 0), "popup" = the normal message box.
            if (link is null) throw new ServerException("mod_not_connected");
            var prio = mode == "alert" ? 0 : 1;
            if (playerId is { } pid) await link.SendAsync(new { op = "msg_player", id = pid, text, prio, secs = 10 });
            else await link.SendAsync(new { op = "msg_all", text, prio, secs = 10 });
            ServerManager.ServerLog(def, $"Message box from {by}{(playerId is null ? "" : $" to player {playerId}")}: {text}");
            return null;
        }

        // "Adrien: hello" in game, so players know which admin is talking (option of the Community tab).
        var inGame = !asServer && FeatureStore.Load<CommunitySettings>(Data(def), "community").ShowAdminName ? $"{by}: {text}" : text;
        var safe = inGame.Replace("'", "’");
        var stored = text.Replace("'", "’");
        var l = Live(id);
        lock (l.Gate)
        {
            l.RecentlySent.RemoveAll(x => DateTime.UtcNow - x.At > TimeSpan.FromSeconds(30));
            l.RecentlySent.Add((DateTime.UtcNow, safe));
        }
        // Mod 0.9.22+: one chat line through the game's chat API ("say" is shown twice to the players).
        if (link is not null && ChatApi(l)) await link.SendAsync(new { op = "chat", text = safe, sender = "Server" });
        else if (link is not null) await link.SendAsync(new { op = "console", cmd = $"say '{safe}'" });
        else await manager.ConsoleAsync(id, $"say '{safe}'", by);
        var entry = Data(def).AddChat(DateTime.UtcNow, "Global", -1, by, null, -1, stored, "eam");
        await hub.BroadcastAsync(new { type = "chat", id, msg = entry }, CancellationToken.None);
        return entry;
    }

    // ------------------------------------------------------------------ main loop

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        _stopping = stop;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var tick = 0;
        while (await timer.WaitForNextTickAsync(stop))
        {
            tick++;
            foreach (var def in store.List())
            {
                try
                {
                    if (tick % 5 == 0) await WatchAsync(def, tick);
                    if (_dirty.TryRemove(def.Id, out _) && hub.ClientCount > 0)
                    {
                        // Only push when something really changed: no redraw and no traffic otherwise.
                        var json = System.Text.Json.JsonSerializer.Serialize(Snapshot(def.Id));
                        if (_lastPushed.TryGetValue(def.Id, out var last) && last == json) continue;
                        _lastPushed[def.Id] = json;
                        await hub.BroadcastAsync(new { type = "live", id = def.Id, data = Snapshot(def.Id) }, stop);
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Live watch of '{Name}' failed", def.Name); }
            }
        }
    }

    private async Task WatchAsync(ServerDefinition def, int tick)
    {
        var rt = manager.Runtime(def.Id);
        var l = Live(def.Id);
        var up = rt.State is ServerState.Running or ServerState.Starting or ServerState.Stopping;

        if (!up)
        {
            await DropLinkAsync(def.Id);
            lock (l.Gate)
            {
                if (l.ProcessCount == 0 && l.Players.Count == 0 && l.Playfields.Count == 0 && !l.Connected) return;
                l.Players.Clear(); l.Playfields.Clear();
                l.Connected = false; l.CpuPercent = 0; l.MemoryBytes = 0; l.ProcessCount = 0;
                l.DediFps = null; l.DediMemoryMb = null; l.DediUptime = null;
            }
            _dirty[def.Id] = true;
            return;
        }

        SampleProcesses(def, l);
        _dirty[def.Id] = true;

        if (!_linkTasks.ContainsKey(def.Id) && rt.State == ServerState.Running && def.ModKey.Length > 0)
            _linkTasks[def.Id] = Task.Run(() => LinkLoopAsync(def));

        if (_links.TryGetValue(def.Id, out var link))
        {
            try
            {
                // Player list + details every 5 s, factions every 15 s: a few small local messages, negligible load.
                await link.SendAsync(new { op = "players" });
                if (tick % 10 == 0) await link.SendAsync(new { op = "dedi" });
                if (tick % 15 == 0) await link.SendAsync(new { op = "factions" });
                if (tick % 30 == 0)
                {
                    await link.SendAsync(new { op = "playfields" });
                    string[] pfs;
                    lock (l.Gate) pfs = l.Playfields.Keys.ToArray();
                    foreach (var pf in pfs) await link.SendAsync(new { op = "pfstats", pf });
                }
            }
            catch (Exception e) { log.LogDebug("Link send failed: {Err}", e.Message); }
        }
    }

    private static void SampleProcesses(ServerDefinition def, ServerLive l)
    {
        var procs = ServerManager.FindGameProcesses(def);
        try
        {
            long mem = 0;
            var cpu = TimeSpan.Zero;
            foreach (var p in procs)
            {
                try { p.Refresh(); mem += p.WorkingSet64; cpu += p.TotalProcessorTime; } catch { }
            }
            var now = DateTime.UtcNow;
            lock (l.Gate)
            {
                if (l.LastCpuSample != default && cpu >= l.LastCpu)
                {
                    var wall = (now - l.LastCpuSample).TotalMilliseconds * Environment.ProcessorCount;
                    l.CpuPercent = wall > 0 ? Math.Clamp(100 * (cpu - l.LastCpu).TotalMilliseconds / wall, 0, 100) : 0;
                }
                l.LastCpu = cpu;
                l.LastCpuSample = now;
                l.MemoryBytes = mem;
                l.ProcessCount = procs.Count;
            }
        }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    private async Task DropLinkAsync(long id)
    {
        if (_links.TryRemove(id, out var link)) await link.DisposeAsync();
    }

    // ------------------------------------------------------------------ link with the mod

    private async Task LinkLoopAsync(ServerDefinition def)
    {
        var l = Live(def.Id);
        try
        {
            while (!_stopping.IsCancellationRequested && manager.Runtime(def.Id).State == ServerState.Running)
            {
                LinkClient link;
                try
                {
                    link = await LinkClient.ConnectAsync(def.ModPort, def.ModKey, _stopping);
                }
                catch (UnauthorizedAccessException)
                {
                    SetLinkError(l, def.Id, "mod_key_rejected");
                    await Task.Delay(TimeSpan.FromSeconds(30), _stopping);
                    continue;
                }
                catch (Exception)
                {
                    // The mod starts with the game: it may not listen yet.
                    SetLinkError(l, def.Id, "mod_not_reachable");
                    await Task.Delay(TimeSpan.FromSeconds(5), _stopping);
                    continue;
                }

                _links[def.Id] = link;
                lock (l.Gate) { l.Connected = true; l.LinkError = null; l.ConnectedUtc = DateTime.UtcNow; }
                ServerManager.ServerLog(def, "EAM mod connected");
                _dirty[def.Id] = true;
                try
                {
                    foreach (var op in new[] { "factions", "players", "playfields", "dedi" }) await link.SendAsync(new { op });
                    while (true)
                    {
                        var msg = await link.ReceiveAsync(_stopping);
                        if (msg is null) break;
                        // One message that cannot be handled must never cut the link (every action through the
                        // mod would fail while it reconnects): it is logged and skipped.
                        try { await HandleAsync(def, l, link, msg.Value); }
                        catch (Exception e) when (e is not OperationCanceledException && link.Connected)
                        {
                            var ev = Str(msg.Value, "ev") ?? "?";
                            if (DateTime.UtcNow - l.LastHandlerErrorUtc > TimeSpan.FromMinutes(10))
                            {
                                l.LastHandlerErrorUtc = DateTime.UtcNow;
                                ServerManager.ServerLog(def, $"EAM mod message '{ev}' ignored: {e.GetType().Name}: {e.Message}");
                            }
                            log.LogWarning(e, "Mod message '{Ev}' of '{Name}' failed", ev, def.Name);
                        }
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogDebug("Link of '{Name}' closed: {Err}", def.Name, e.Message);
                    ServerManager.ServerLog(def, $"EAM mod link closed: {e.GetType().Name}: {e.Message}");
                }
                finally
                {
                    _links.TryRemove(def.Id, out _);
                    await link.DisposeAsync();
                    lock (l.Gate) { l.Connected = false; }
                    _dirty[def.Id] = true;
                    ServerManager.ServerLog(def, "EAM mod disconnected");
                }
                await Task.Delay(TimeSpan.FromSeconds(3), _stopping);
            }
        }
        catch (OperationCanceledException) { }
        finally { _linkTasks.TryRemove(def.Id, out _); }
    }

    private void SetLinkError(ServerLive l, long id, string code)
    {
        lock (l.Gate)
        {
            if (l.LinkError == code) return;
            l.LinkError = code;
        }
        _dirty[id] = true;
    }

    private async Task HandleAsync(ServerDefinition def, ServerLive l, LinkClient link, JsonElement m)
    {
        var ev = Str(m, "ev");
        if (Str(m, "rid") is { } rid && _waiters.TryRemove(rid, out var waiter)) waiter.TrySetResult(m.Clone());
        if (ev is not null and not "chat") Raise(def, ev, m);
        switch (ev)
        {
            case "hello":
                lock (l.Gate) l.ModVersion = Str(m, "mod");
                break;

            case "player_connected":
                lock (l.Gate) { l.JustConnected.Add(Int(m, "id")); }
                await link.SendAsync(new { op = "player", id = Int(m, "id") });
                break;

            case "player_disconnected":
            {
                var id = Int(m, "id");
                LivePlayer? gone;
                lock (l.Gate) { l.Players.Remove(id, out gone); }
                if (gone?.SteamId is { } steam)
                {
                    Data(def).PlayerSeen(steam, gone.Id, gone.Name, (long)(DateTime.UtcNow - gone.PlaytimeCountedUtc).TotalSeconds);
                    Data(def).PlayerEvent(steam, gone.Name, "leave", gone.Playfield);
                }
                if (gone is not null)
                {
                    await hub.BroadcastAsync(new { type = "player_left", id = def.Id, name = gone.Name }, CancellationToken.None);
                    if (PlayerLeft is { } left) _ = Task.Run(() => left(def, gone));
                }
                break;
            }

            case "player_list":
            {
                var ids = m.TryGetProperty("ids", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(x => x.GetInt32()).ToHashSet() : [];
                lock (l.Gate)
                {
                    foreach (var gone in l.Players.Keys.Where(k => !ids.Contains(k)).ToList()) l.Players.Remove(gone);
                }
                foreach (var id in ids) await link.SendAsync(new { op = "player", id });
                break;
            }

            case "player_info":
            {
                var id = Int(m, "id");
                bool isNew, arrived;
                LivePlayer p;
                long addSeconds;
                lock (l.Gate)
                {
                    isNew = !l.Players.TryGetValue(id, out var existing);
                    arrived = l.JustConnected.Remove(id);
                    p = existing ?? new LivePlayer { Id = id };
                    p.SteamId = Str(m, "steamId");
                    p.Name = Str(m, "name") ?? p.Name;
                    p.Playfield = Str(m, "pf");
                    p.FactionId = Int(m, "factionId");
                    p.Ping = Int(m, "ping");
                    p.Health = (float)Num(m, "health");
                    p.HealthMax = (float)Num(m, "healthMax");
                    p.Credits = Num(m, "credits");
                    p.Level = Int(m, "upgrade");
                    p.Exp = Int(m, "exp");
                    p.X = (float)Num(m, "x"); p.Y = (float)Num(m, "y"); p.Z = (float)Num(m, "z");
                    p.Origin = Int(m, "origin");
                    p.Permission = Int(m, "permission");
                    var now = DateTime.UtcNow;
                    addSeconds = isNew ? 0 : (long)(now - p.PlaytimeCountedUtc).TotalSeconds;
                    p.PlaytimeCountedUtc = now;
                    l.Players[id] = p;
                }
                if (p.SteamId is { Length: > 0 } s) Data(def).PlayerSeen(s, id, p.Name, addSeconds);
                if (isNew)
                {
                    if (p.SteamId is { Length: > 0 } s2) Data(def).PlayerEvent(s2, p.Name, "join", p.Playfield);
                    await hub.BroadcastAsync(new { type = "player_joined", id = def.Id, name = p.Name }, CancellationToken.None);
                    if (PlayerJoined is { } handler) _ = Task.Run(() => handler(def, p));
                    if (arrived && PlayerArrived is { } arrive) _ = Task.Run(() => arrive(def, p));
                }
                break;
            }

            case "faction_changed":
                await link.SendAsync(new { op = "factions" });
                await link.SendAsync(new { op = "player", id = Int(m, "id") });
                break;

            case "player_playfield":
            {
                LivePlayer? moved;
                lock (l.Gate) { if (l.Players.TryGetValue(Int(m, "id"), out moved)) moved.Playfield = Str(m, "pf"); }
                if (moved?.SteamId is { Length: > 0 } ms) Data(def).PlayerEvent(ms, moved.Name, "playfield", Str(m, "pf"));
                break;
            }

            case "playfield_list":
            {
                var names = m.TryGetProperty("list", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : [];
                lock (l.Gate)
                {
                    foreach (var gone in l.Playfields.Keys.Where(k => !names.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList()) l.Playfields.Remove(gone);
                    foreach (var n in names) if (!l.Playfields.ContainsKey(n)) l.Playfields[n] = new LivePlayfield { Name = n };
                }
                foreach (var pf in names) await link.SendAsync(new { op = "pfstats", pf });
                break;
            }

            case "playfield_loaded":
            {
                var name = Str(m, "pf") ?? "";
                lock (l.Gate) { l.Playfields[name] = new LivePlayfield { Name = name, ProcessId = Int(m, "pid") }; }
                await link.SendAsync(new { op = "pfstats", pf = name });
                break;
            }

            case "playfield_unloaded":
                lock (l.Gate) l.Playfields.Remove(Str(m, "pf") ?? "");
                break;

            case "playfield_stats":
            {
                var name = Str(m, "pf") ?? "";
                lock (l.Gate)
                {
                    if (!l.Playfields.TryGetValue(name, out var pf)) l.Playfields[name] = pf = new LivePlayfield { Name = name };
                    pf.ProcessId = Int(m, "pid");
                    pf.Players = Int(m, "players");
                    pf.Fps = (float)Num(m, "fps");
                    pf.MemoryMb = Int(m, "mem");
                    pf.Structures = Int(m, "structs");
                    pf.Mobs = Int(m, "mobs");
                    pf.UptimeSeconds = Int(m, "uptime");
                }
                break;
            }

            case "dedi":
                lock (l.Gate) { l.DediFps = (float)Num(m, "fps"); l.DediMemoryMb = Int(m, "mem"); l.DediUptime = Int(m, "uptime"); }
                break;

            case "factions":
                if (m.TryGetProperty("list", out var fl) && fl.ValueKind == JsonValueKind.Array)
                    lock (l.Gate)
                    {
                        l.Factions.Clear(); // deleted factions disappear too
                        l.FactionNames.Clear();
                        foreach (var f in fl.EnumerateArray())
                        {
                            l.Factions[Int(f, "id")] = Str(f, "abbrev") is { Length: > 0 } a ? a : Str(f, "name") ?? "";
                            l.FactionNames[Int(f, "id")] = Str(f, "name") ?? "";
                        }
                    }
                break;

            case "chat":
                await OnChatAsync(def, l, m);
                return; // the chat message itself is pushed, no need for a full snapshot

            case "ok":
            case "inventory":
            case "credits":
            case "banned":
                return;

            case "error":
                log.LogDebug("Mod of '{Name}' reported {Code} for {Op}", def.Name, Str(m, "code"), Str(m, "op"));
                return;

            default:
                return;
        }
        _dirty[def.Id] = true;
    }

    private async Task OnChatAsync(ServerDefinition def, ServerLive l, JsonElement m)
    {
        var text = Str(m, "text") ?? "";
        if (text.Length == 0) return;
        var senderId = Int(m, "sender");
        var senderType = Str(m, "senderType") ?? "Player";
        string name;
        string? steam = null;
        lock (l.Gate)
        {
            if (senderType != "Player")
            {
                // Our own "say" comes back from the game: already recorded with the admin's name.
                l.RecentlySent.RemoveAll(x => DateTime.UtcNow - x.At > TimeSpan.FromSeconds(30));
                if (l.RecentlySent.Any(x => text.Contains(x.Text, StringComparison.Ordinal))) return;
                name = Str(m, "name") is { Length: > 0 } n ? n : "Server";
            }
            else if (l.Players.TryGetValue(senderId, out var p)) { name = p.Name; steam = p.SteamId; }
            else name = Str(m, "name") is { Length: > 0 } n2 ? n2 : $"#{senderId}";
        }
        var entry = Data(def).AddChat(DateTime.UtcNow, Str(m, "channel") ?? "Global", senderId, name, steam, Int(m, "recipient", -1), text, "game");
        await hub.BroadcastAsync(new { type = "chat", id = def.Id, msg = entry }, CancellationToken.None);
        if (ChatReceived is { } handlers && senderType == "Player")
        {
            var sender = Online(def.Id, senderId);
            foreach (var h in handlers.GetInvocationList().Cast<Func<ServerDefinition, ChatEntry, LivePlayer?, Task>>())
                _ = Task.Run(async () =>
                {
                    try { await h(def, entry, sender); }
                    catch (Exception e) { log.LogWarning(e, "Chat handler failed"); }
                });
        }
    }

    internal static string? Str(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        } : null;

    internal static double Num(JsonElement e, string k) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    internal static int Int(JsonElement e, string k, int fallback = 0) =>
        e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetDouble() : fallback;
}
