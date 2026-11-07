using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Eam.Core.Servers;

namespace Eam.Engine.Servers;

/// <summary>
/// Step 8: anti-cheat, attack log, game statistics (deaths, cores), positions for "what happened here",
/// server statistics every 10 minutes, pole control, emergency teleport and telnet flood protection.
/// </summary>
public sealed class SecurityService : BackgroundService
{
    private readonly ServerManager _manager;
    private readonly LiveService _live;
    private readonly Alerts _alerts;
    private readonly ILogger<SecurityService> _log;

    private readonly ConcurrentDictionary<long, DateTime> _lastPositions = new();
    private readonly ConcurrentDictionary<long, DateTime> _lastStats = new();
    private readonly ConcurrentDictionary<long, DateTime> _lastScan = new();
    private readonly ConcurrentDictionary<long, DateTime> _lastAttackCheck = new();
    private readonly ConcurrentDictionary<string, double> _credits = new();
    private readonly ConcurrentDictionary<string, DateTime> _attackSeen = new();
    private readonly ConcurrentDictionary<string, DateTime> _poleSince = new();
    private readonly ConcurrentDictionary<string, bool> _poleAlerted = new();
    private readonly ConcurrentDictionary<long, (string File, long Pos)> _logPos = new();
    private readonly ConcurrentDictionary<long, Queue<DateTime>> _flood = new();
    private readonly ConcurrentDictionary<long, DateTime> _floodRestart = new();

    public SecurityService(ServerManager manager, LiveService live, Alerts alerts, ILogger<SecurityService> log)
    {
        _manager = manager;
        _live = live;
        _alerts = alerts;
        _log = log;
        _live.ModEvent += OnModEventAsync;
    }

    public SecuritySettings Settings(ServerDefinition def) => FeatureStore.Load<SecuritySettings>(_live.Data(def), "security");

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stop))
        {
            foreach (var def in _manager.Definitions())
            {
                if (_manager.Runtime(def.Id).State != ServerState.Running) continue;
                try
                {
                    var s = Settings(def);
                    if (Due(_lastStats, def.Id, TimeSpan.FromMinutes(10))) RecordStats(def);
                    if (s.FloodEnabled) await WatchFloodAsync(def, s);
                    if (!_live.IsLinked(def.Id)) continue;
                    if (Due(_lastPositions, def.Id, TimeSpan.FromSeconds(s.PositionSeconds))) RecordPositions(def);
                    if (s.AttackLogEnabled && Due(_lastAttackCheck, def.Id, TimeSpan.FromSeconds(30))) await CheckAttacksAsync(def, s);
                    if (s.PolesEnabled) await CheckPolesAsync(def, s);
                    if (s.AntiCheatEnabled && Due(_lastScan, def.Id, TimeSpan.FromMinutes(s.ScanMinutes))) await ScanAsync(def, s, "EAM");
                }
                catch (Exception e) when (e is not OperationCanceledException) { _log.LogDebug("Security check of '{Name}' failed: {Err}", def.Name, e.Message); }
            }
        }
    }

    private static bool Due(ConcurrentDictionary<long, DateTime> last, long id, TimeSpan every)
    {
        var now = DateTime.UtcNow;
        if (last.TryGetValue(id, out var t) && now - t < every) return false;
        last[id] = now;
        return true;
    }

    // ------------------------------------------------------------------ statistics and positions

    private void RecordStats(ServerDefinition def)
    {
        var l = _live.Live(def.Id);
        var data = _live.Data(def);
        var now = DateTime.UtcNow;
        lock (l.Gate)
        {
            data.AddStat(new StatRow(now, "server", Math.Round(l.CpuPercent, 1), Math.Round(l.MemoryBytes / 1048576.0), l.Players.Count, l.Playfields.Count,
                l.DediFps ?? 0, l.Playfields.Values.Sum(p => p.Structures)));
            foreach (var pf in l.Playfields.Values)
                data.AddStat(new StatRow(now, "pf:" + pf.Name, 0, pf.MemoryMb, pf.Players, 1, pf.Fps, pf.Structures));
        }
    }

    private void RecordPositions(ServerDefinition def)
    {
        var now = DateTime.UtcNow;
        var rows = _live.OnlinePlayers(def.Id).Where(p => p.SteamId is { Length: > 0 } && p.Playfield is { Length: > 0 })
            .Select(p => new PositionRow(now, "p", p.SteamId!, p.Name, p.Playfield!, p.X, p.Y, p.Z)).ToList();
        if (rows.Count > 0) _live.Data(def).AddPositions(rows);
    }

    public object Stats(long id, int days)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var from = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 14));
        var server = data.Stats("server", from);
        var playfields = data.StatScopes(from).Where(x => x.StartsWith("pf:", StringComparison.Ordinal))
            .Select(scope => new { name = scope[3..], rows = data.Stats(scope, from) })
            .Select(p => new
            {
                p.name,
                samples = p.rows.Count,
                avgFps = p.rows.Count > 0 ? Math.Round(p.rows.Average(r => r.Fps), 1) : 0,
                minFps = p.rows.Count > 0 ? Math.Round(p.rows.Min(r => r.Fps), 1) : 0,
                maxMem = p.rows.Count > 0 ? p.rows.Max(r => r.Mem) : 0,
                maxPlayers = p.rows.Count > 0 ? p.rows.Max(r => r.Players) : 0,
                structures = p.rows.Count > 0 ? p.rows[^1].Structures : 0,
            }).OrderByDescending(p => p.samples).ToList();
        var tips = new List<string>();
        if (server.Count > 6)
        {
            if (server.Average(r => r.Cpu) > 80) tips.Add("tip_cpu");
            if (playfields.Any(p => p.avgFps is > 0 and < 15)) tips.Add("tip_fps");
            if (server.Max(r => r.Playfields) > 15) tips.Add("tip_playfields");
        }
        return new { server, playfields, tips };
    }

    // ------------------------------------------------------------------ anti-cheat

    public async Task<object> ScanAsync(ServerDefinition def, SecuritySettings s, string by)
    {
        var data = _live.Data(def);
        var admins = s.ExemptAdmins ? AdminConfig.ReadElevated(AdminConfig.FileOf(def)).Where(e => e.Permission >= 3).Select(e => e.SteamId).ToHashSet() : [];
        var white = s.Whitelist.ToHashSet();
        var rules = s.CheatRules.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.First());
        var found = new List<object>();
        foreach (var p in _live.OnlinePlayers(def.Id))
        {
            if (p.SteamId is not { Length: > 0 } steam || admins.Contains(steam)) continue;
            JsonElement info;
            try { info = await _live.RequestAsync(def.Id, new() { ["op"] = "player", ["id"] = p.Id, ["full"] = true }, TimeSpan.FromSeconds(15)); }
            catch (ServerException) { continue; }
            var reasons = new List<(int Level, string Text)>();
            var totals = new Dictionary<int, long>();
            foreach (var bag in new[] { "toolbelt", "bag" })
                if (info.TryGetProperty(bag, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var st in arr.EnumerateArray())
                    {
                        var item = LiveService.Int(st, "id");
                        var count = LiveService.Int(st, "count");
                        if (white.Contains(item)) continue;
                        totals[item] = totals.GetValueOrDefault(item) + count;
                        if (s.MaxStack > 0 && count > s.MaxStack) reasons.Add((3, $"stack {item} × {count} > {s.MaxStack}"));
                        if (LiveService.Int(st, "ammo") < 0) reasons.Add((2, $"ammo {item}"));
                    }
            foreach (var (item, count) in totals)
                if (rules.TryGetValue(item, out var r) && count > r.Max) reasons.Add((r.Level, $"item {item} × {count}{(r.Max > 0 ? $" > {r.Max}" : "")}"));
            var health = LiveService.Num(info, "health");
            var healthMax = LiveService.Num(info, "healthMax");
            if (healthMax > 0 && health > healthMax * 1.5) reasons.Add((2, $"health {health:0}/{healthMax:0}"));
            var food = LiveService.Num(info, "food");
            var foodMax = LiveService.Num(info, "foodMax");
            if (foodMax > 0 && food > foodMax * 1.5) reasons.Add((1, $"food {food:0}/{foodMax:0}"));
            var credits = LiveService.Num(info, "credits");
            var key = def.Id + ":" + steam;
            if (s.CreditsJump > 0 && _credits.TryGetValue(key, out var before) && credits - before > s.CreditsJump)
                reasons.Add((2, $"credits +{credits - before:0} since the last scan"));
            _credits[key] = credits;
            if (reasons.Count == 0) continue;

            var level = reasons.Max(r => r.Level);
            var label = level switch { 3 => "extreme", 2 => "high", _ => "maybe" };
            var text = $"{label}: {string.Join(", ", reasons.Select(r => r.Text).Distinct().Take(10))}";
            await _alerts.RaiseAsync(def, "cheat", level >= 3 ? 2 : level == 2 ? 1 : 0, text, steam, p.Name, p.Playfield);
            found.Add(new { p.Name, steamId = steam, level = label, reasons = reasons.Select(r => r.Text).Distinct().ToList() });
            if (level >= 3 && s.AutoBanExtreme)
            {
                await _manager.ConsoleAsync(def.Id, $"ban {steam} {s.AutoBanDuration}", "EAM anti-cheat");
                data.PlayerEvent(steam, p.Name, "admin_ban", $"{s.AutoBanDuration} (anti-cheat: {text})", "EAM");
            }
        }
        return found;
    }

    // ------------------------------------------------------------------ attacks

    private async Task CheckAttacksAsync(ServerDefinition def, SecuritySettings s)
    {
        var data = _live.Data(def);
        var players = _live.OnlinePlayers(def.Id);
        if (players.Count == 0) return;
        var bases = data.Structures().Where(x => x.Type == EntityTypes.BA && FactionGroups.IsPlayerOwned(x.FactionGroup, x.FactionId)).ToList();
        foreach (var p in players)
        {
            if (p.Playfield is null || p.SteamId is null) continue;
            foreach (var b in bases)
            {
                if (!string.Equals(b.Playfield, p.Playfield, StringComparison.OrdinalIgnoreCase)) continue;
                var own = b.FactionGroup == FactionGroups.Player ? b.FactionId == p.Id : b.FactionId == p.FactionId && p.FactionId > 0;
                if (own) continue;
                var d = Math.Sqrt(Math.Pow(b.X - p.X, 2) + Math.Pow(b.Y - p.Y, 2) + Math.Pow(b.Z - p.Z, 2));
                if (d > s.AttackRadius) continue;
                var key = $"{def.Id}:{p.SteamId}:{b.Id}";
                if (_attackSeen.TryGetValue(key, out var at) && DateTime.UtcNow - at < TimeSpan.FromMinutes(10)) continue;
                _attackSeen[key] = DateTime.UtcNow;
                var defenders = players.Where(x => b.FactionGroup == FactionGroups.Player ? x.Id == b.FactionId : x.FactionId == b.FactionId).ToList();
                await _alerts.RaiseAsync(def, "attack", 0,
                    $"near base {b.Name} ({b.Id}) at {d:0} m from its core; defenders online: {(defenders.Count == 0 ? "none" : string.Join(", ", defenders.Select(x => x.Name)))}",
                    p.SteamId, p.Name, p.Playfield);
                if (s.AttackWarnOwners)
                    foreach (var dfd in defenders)
                        try { await _live.SendToModAsync(def.Id, new { op = "msg_player", id = dfd.Id, text = $"EAM: {p.Name} approche de « {b.Name} » ({d:0} m) / is near your base", prio = 0, secs = 10 }); } catch { }
            }
        }
    }

    // ------------------------------------------------------------------ poles

    private async Task CheckPolesAsync(ServerDefinition def, SecuritySettings s)
    {
        foreach (var p in _live.OnlinePlayers(def.Id))
        {
            if (p.SteamId is null || p.Playfield is null) continue;
            var key = $"{def.Id}:{p.SteamId}";
            var inZone = FeatureStore.MatchesPlayfield(s.PolePlayfields, p.Playfield) && s.PolePlayfields.Length > 0 && Math.Abs(p.Z) > s.PoleLimit;
            if (!inZone) { _poleSince.TryRemove(key, out _); _poleAlerted.TryRemove(key, out _); continue; }
            var since = _poleSince.GetOrAdd(key, _ => DateTime.UtcNow);
            var secs = (DateTime.UtcNow - since).TotalSeconds;
            if (secs >= s.PoleAlertSeconds && _poleAlerted.TryAdd(key, true))
            {
                await _alerts.RaiseAsync(def, "pole", 1, $"in a pole zone for {secs:0} s (z = {p.Z:0})", p.SteamId, p.Name, p.Playfield);
                try { await _live.SendToModAsync(def.Id, new { op = "msg_player", id = p.Id, text = "EAM: zone de pôle interdite, vous allez être déplacé / pole zone, you will be moved", prio = 0, secs = 10 }); } catch { }
            }
            if (secs >= s.PoleMoveSeconds)
            {
                _poleSince.TryRemove(key, out _);
                _poleAlerted.TryRemove(key, out _);
                var z = Math.Sign(p.Z) * (s.PoleLimit - 200);
                try
                {
                    await _live.RequestAsync(def.Id, new() { ["op"] = "teleport", ["id"] = p.Id, ["x"] = p.X, ["y"] = p.Y + 60, ["z"] = z }, TimeSpan.FromSeconds(30));
                    await _alerts.RaiseAsync(def, "pole", 1, $"moved out of the pole zone (z {p.Z:0} → {z:0})", p.SteamId, p.Name, p.Playfield);
                }
                catch (ServerException e) { _log.LogWarning("Pole move of {Name} failed: {Err}", p.Name, e.Code); }
            }
        }
    }

    // ------------------------------------------------------------------ game events: deaths, cores, emergency teleport

    private async Task OnModEventAsync(ServerDefinition def, string ev, JsonElement m)
    {
        switch (ev)
        {
            case "stat":
            {
                var kind = LiveService.Str(m, "kind") ?? "";
                var i1 = LiveService.Int(m, "i1");
                var i2 = LiveService.Int(m, "i2");
                var data = _live.Data(def);
                string text;
                LivePlayer? who = null;
                if (kind == "PlayerDied")
                {
                    who = _live.Online(def.Id, i1);
                    var killer = _live.Online(def.Id, i2);
                    text = $"died{(killer is not null && killer != who ? $", killed by {killer.Name}" : "")} ({LiveService.Int(m, "i3")}/{LiveService.Int(m, "i4")})";
                    if (who?.SteamId is { } steam) data.PlayerEvent(steam, who.Name, "death", text);
                }
                else
                {
                    var st = data.Structure(i1);
                    text = $"{kind}: {(st is null ? "#" + i1 : $"{st.Name} ({st.Id}, {EntityTypes.Name(st.Type)})")} [{i2}, {LiveService.Int(m, "i3")}, {LiveService.Int(m, "i4")}]";
                    if (st is not null) data.StructureEventBy(st.Id, kind, null, $"{i2}/{LiveService.Int(m, "i3")}/{LiveService.Int(m, "i4")}", "game");
                }
                await _alerts.RaiseAsync(def, "game", kind is "CoreRemoved" or "StructDestroyed" ? 1 : 0, text, who?.SteamId, who?.Name, who?.Playfield);
                break;
            }
            case "player_waiting_lost":
            {
                var s = Settings(def);
                if (!s.EmergencyEnabled) return;
                var data = _live.Data(def);
                var seen = data.Players().FirstOrDefault(p => p.EntityId == LiveService.Int(m, "id"));
                if (seen is null) return;
                var c = data.Counter(seen.SteamId, "emergency_fail");
                var count = c is not null && DateTime.UtcNow - c.LastUtc < TimeSpan.FromMinutes(s.EmergencyWindowMinutes) ? c.Count + 1 : 1;
                data.SetCounter(seen.SteamId, "emergency_fail", count);
                if (count >= s.EmergencyFailures)
                {
                    data.SetCounter(seen.SteamId, "emergency_pending", 1);
                    await _alerts.RaiseAsync(def, "emergency", 1, $"{count} failed logins: emergency teleport at the next login", seen.SteamId, seen.Name);
                }
                break;
            }
            case "player_connected":
            {
                var s = Settings(def);
                if (!s.EmergencyEnabled) return;
                var id = LiveService.Int(m, "id");
                var data = _live.Data(def);
                var seen = data.Players().FirstOrDefault(p => p.EntityId == id);
                if (seen is null || data.Counter(seen.SteamId, "emergency_pending") is null) return;
                data.DeleteCounter(seen.SteamId, "emergency_pending");
                data.DeleteCounter(seen.SteamId, "emergency_fail");
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    try
                    {
                        var target = s.EmergencyPlayfield;
                        if (target.Length == 0)
                        {
                            var info = await _live.RequestAsync(def.Id, new() { ["op"] = "player", ["id"] = id, ["full"] = true }, TimeSpan.FromSeconds(15));
                            target = LiveService.Str(info, "startPf") ?? "";
                        }
                        if (target.Length == 0) throw new ServerException("invalid_playfield");
                        await _live.RequestAsync(def.Id, new() { ["op"] = "teleport", ["id"] = id, ["pf"] = target, ["x"] = s.EmergencyX, ["y"] = s.EmergencyY, ["z"] = s.EmergencyZ }, TimeSpan.FromSeconds(60));
                        await _alerts.RaiseAsync(def, "emergency", 1, $"emergency teleport to {target}", seen.SteamId, seen.Name, target);
                        data.PlayerEvent(seen.SteamId, seen.Name, "emergency_teleport", target, "EAM");
                        return;
                    }
                    catch (ServerException) { await Task.Delay(TimeSpan.FromSeconds(10)); }
                }
                await _alerts.RaiseAsync(def, "emergency", 2, "emergency teleport failed", seen.SteamId, seen.Name);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ telnet flood ("Cannot send package to client" loop)

    private async Task WatchFloodAsync(ServerDefinition def, SecuritySettings s)
    {
        var logs = Path.Combine(def.GameDir, "Logs");
        if (!Directory.Exists(logs)) return;
        var newest = Directory.EnumerateFiles(logs, "Dedicated_*.log", SearchOption.AllDirectories).Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        if (newest is null) return;
        var (file, pos) = _logPos.GetValueOrDefault(def.Id);
        if (file != newest.FullName) { _logPos[def.Id] = (newest.FullName, newest.Length); return; } // start at the end of a new log
        if (newest.Length <= pos) { if (newest.Length < pos) _logPos[def.Id] = (file, newest.Length); return; }
        string chunk;
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            fs.Seek(Math.Max(pos, newest.Length - 4 * 1024 * 1024), SeekOrigin.Begin);
            using var r = new StreamReader(fs, Encoding.UTF8);
            chunk = await r.ReadToEndAsync();
            _logPos[def.Id] = (file, fs.Length);
        }
        var hits = CountOccurrences(chunk, s.FloodText);
        var q = _flood.GetOrAdd(def.Id, _ => new Queue<DateTime>());
        var now = DateTime.UtcNow;
        lock (q)
        {
            for (var i = 0; i < Math.Min(hits, 10000); i++) q.Enqueue(now);
            while (q.Count > 0 && now - q.Peek() > TimeSpan.FromMinutes(1)) q.Dequeue();
            if (q.Count < s.FloodPerMinute) return;
            q.Clear();
        }
        if (_floodRestart.TryGetValue(def.Id, out var last) && now - last < TimeSpan.FromMinutes(15)) return;
        _floodRestart[def.Id] = now;
        await _alerts.RaiseAsync(def, "flood", 2, $"\"{s.FloodText}\" repeated {s.FloodPerMinute}+ times in a minute: restarting the server");
        _ = Task.Run(async () =>
        {
            try { await _manager.RestartAsync(def.Id, 1, "Restart (network problem) / Redémarrage (problème réseau)", "EAM flood protection"); }
            catch (Exception e) { _log.LogWarning("Flood restart failed: {Err}", e.Message); }
        });
    }

    internal static int CountOccurrences(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    // ------------------------------------------------------------------ journal and history for the interface

    public object Journal(long id, string? category, int minSeverity, long? before, string? steam)
    {
        var data = _live.Data(_manager.Definition(id));
        return data.Journal(string.IsNullOrEmpty(category) ? null : category, minSeverity, 200, before, string.IsNullOrEmpty(steam) ? null : steam);
    }

    public object Near(long id, string pf, double x, double y, double z, double radius, DateTime from, DateTime to)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        radius = Math.Clamp(radius, 5, 5000);
        var rows = data.PositionsNear(pf, x, y, z, radius, from, to, 5000);
        // One line per player or structure: first and last time seen, closest distance.
        var summary = rows.GroupBy(r => (r.Kind, r.Ref)).Select(g => new
        {
            kind = g.Key.Kind, reference = g.Key.Ref, name = g.First().Name, first = g.Min(r => r.Utc), last = g.Max(r => r.Utc), samples = g.Count(),
            closest = Math.Round(g.Min(r => Math.Sqrt(Math.Pow(r.X - x, 2) + Math.Pow(r.Y - y, 2) + Math.Pow(r.Z - z, 2)))),
        }).OrderBy(r => r.closest).ToList();
        var ids = rows.Where(r => r.Kind == "s").Select(r => int.TryParse(r.Ref, out var i) ? i : 0).ToHashSet();
        var events = data.StructureEvents(null, 2000, from, to).Where(e => ids.Contains(e.StructureId)).Take(300).ToList();
        var journal = data.Journal(null, 0, 1000).Where(j => j.Utc >= from && j.Utc <= to && string.Equals(j.Playfield, pf, StringComparison.OrdinalIgnoreCase)).Take(300).ToList();
        return new { summary, events, journal };
    }

    public object Track(long id, string reference, DateTime from, DateTime to) =>
        _live.Data(_manager.Definition(id)).PositionsOf(reference, from, to, 3000);
}
