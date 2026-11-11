using System.Collections.Concurrent;
using System.Text.Json;
using Eam.Core.Servers;

namespace Eam.Engine.Servers;

/// <summary>
/// Version 0.9.5: inventory history and god mode.
/// Every few minutes the inventory of each online player is copied when it changed. When a player dies, the last copy
/// is kept as a "death" copy: an admin can give the whole inventory back after a bugged backpack.
/// God mode keeps the health, food, oxygen and stamina of the flagged players full.
/// </summary>
public sealed class PlayerCareService : BackgroundService
{
    private readonly ServerManager _manager;
    private readonly LiveService _live;
    private readonly ILogger<PlayerCareService> _log;
    private readonly ConcurrentDictionary<long, DateTime> _lastSnap = new();
    private readonly ConcurrentDictionary<long, DateTime> _lastPrune = new();

    public PlayerCareService(ServerManager manager, LiveService live, ILogger<PlayerCareService> log)
    {
        _manager = manager;
        _live = live;
        _log = log;
        _live.ModEvent += OnModEventAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stop))
        {
            foreach (var def in _manager.Definitions())
            {
                if (_manager.Runtime(def.Id).State != ServerState.Running || !_live.IsLinked(def.Id)) continue;
                try
                {
                    var data = _live.Data(def);
                    var s = FeatureStore.Load<SecuritySettings>(data, "security");
                    await GodModeAsync(def, data);
                    if (s.InventoryHistoryEnabled) await PeriodicSnapshotsAsync(def, TimeSpan.FromMinutes(Math.Max(1, s.InventoryMinutes)));
                    if (Due(_lastPrune, def.Id, TimeSpan.FromHours(6))) data.PruneInventorySnapshots(s.InventoryDays);
                }
                catch (Exception e) when (e is not OperationCanceledException) { _log.LogDebug("Player care of '{Name}' failed: {Err}", def.Name, e.Message); }
            }
        }
    }

    /// <summary>Last periodic inventory copy per server and player.</summary>
    private readonly ConcurrentDictionary<(long, string), DateTime> _lastPlayerSnap = new();

    /// <summary>
    /// One periodic copy per player and interval, spread over the interval (a few players per 10 s round)
    /// instead of every player at the same second: the game gets a small, steady number of requests.
    /// </summary>
    private async Task PeriodicSnapshotsAsync(ServerDefinition def, TimeSpan every)
    {
        var now = DateTime.UtcNow;
        var online = _live.OnlinePlayers(def.Id).Where(p => p.SteamId is { Length: > 0 }).ToList();
        var perRound = Math.Max(1, (int)Math.Ceiling(online.Count * 10.0 / every.TotalSeconds));
        var due = online
            .Select(p => (p, last: _lastPlayerSnap.TryGetValue((def.Id, p.SteamId!), out var t) ? t : (DateTime?)null))
            .Where(x => x.last is null || now - x.last.Value >= every)
            .OrderBy(x => x.last ?? DateTime.MinValue)
            .Take(perRound).ToList();
        foreach (var (p, last) in due)
        {
            if (last is null)
            {
                // First time seen: spread the first copies over the interval.
                _lastPlayerSnap[(def.Id, p.SteamId!)] = now - TimeSpan.FromSeconds(Random.Shared.NextDouble() * every.TotalSeconds);
                continue;
            }
            _lastPlayerSnap[(def.Id, p.SteamId!)] = now;
            await SnapshotAsync(def, p, "periodic");
        }
    }

    private static bool Due(ConcurrentDictionary<long, DateTime> last, long id, TimeSpan every)
    {
        var now = DateTime.UtcNow;
        if (last.TryGetValue(id, out var t) && now - t < every) return false;
        last[id] = now;
        return true;
    }

    /// <summary>Copies the player's inventory now. Returns the copy's id (0 when nothing changed).</summary>
    public async Task<long> SnapshotAsync(ServerDefinition def, LivePlayer p, string reason)
    {
        if (p.SteamId is not { Length: > 0 } steam) return 0;
        var info = await _live.RequestAsync(def.Id, new() { ["op"] = "player", ["id"] = p.Id, ["full"] = true }, TimeSpan.FromSeconds(15));
        var toolbelt = info.TryGetProperty("toolbelt", out var t) && t.ValueKind == JsonValueKind.Array ? t.GetRawText() : "[]";
        var bag = info.TryGetProperty("bag", out var b) && b.ValueKind == JsonValueKind.Array ? b.GetRawText() : "[]";
        if (reason == "periodic" && toolbelt == "[]" && bag == "[]") return 0; // empty right after a death or a reset: keep the last full one
        return _live.Data(def).AddInventorySnapshot(steam, p.Name, reason, p.Playfield, toolbelt, bag, DateTime.UtcNow);
    }

    private async Task GodModeAsync(ServerDefinition def, ServerData data)
    {
        var gods = data.Metas().Where(m => m.God).Select(m => m.SteamId).ToHashSet();
        if (gods.Count == 0) return;
        foreach (var p in _live.OnlinePlayers(def.Id))
        {
            if (p.SteamId is null || !gods.Contains(p.SteamId)) continue;
            var max = Math.Max(1, (int)(p.HealthMax > 0 ? p.HealthMax : 500));
            if (p.Health >= max * 0.95) continue;
            await _live.RequestAsync(def.Id, new() { ["op"] = "player_set", ["id"] = p.Id, ["health"] = max, ["food"] = 10000, ["oxygen"] = 10000, ["stamina"] = 10000, ["radiation"] = 0 });
        }
    }

    private Task OnModEventAsync(ServerDefinition def, string ev, JsonElement m)
    {
        if (ev != "stat" || LiveService.Str(m, "kind") != "PlayerDied") return Task.CompletedTask;
        var who = _live.Online(def.Id, LiveService.Int(m, "i1"));
        if (who?.SteamId is not { } steam) return Task.CompletedTask;
        var data = _live.Data(def);
        // The game empties the inventory at death: the last copy taken before is what the player had on them.
        var last = data.LastInventorySnapshot(steam);
        if (last is not null)
        {
            data.AddInventorySnapshot(steam, who.Name, "death", who.Playfield, last.Toolbelt, last.Bag, DateTime.UtcNow);
            data.PlayerEvent(steam, who.Name, "inventory_saved", $"death ({who.Playfield})");
        }
        return Task.CompletedTask;
    }
}
