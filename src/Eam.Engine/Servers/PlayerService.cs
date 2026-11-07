using System.Text.Json;
using Eam.Core.Servers;
using Eam.Engine.Api;

namespace Eam.Engine.Servers;

public sealed record PlayerActionRequest(
    string Action, string? Text, double? Amount, int? ItemId, int? Count, int? Exp, int? Upgrade,
    string? Playfield, double? X, double? Y, double? Z, string? Duration, int? Permission, string? Reason,
    List<StackDto>? Toolbelt, List<StackDto>? Bag);

public sealed record StackDto(int Id, int Count, int Slot, int Ammo, int Decay);

/// <summary>
/// Step 5: players of a server (online and seen before), their details and every admin action on them.
/// Every action is written in the player's history with the admin's name.
/// </summary>
public sealed class PlayerService
{
    private readonly ServerManager _manager;
    private readonly LiveService _live;
    private readonly ILogger<PlayerService> _log;

    public PlayerService(ServerManager manager, LiveService live, ILogger<PlayerService> log)
    {
        _manager = manager;
        _live = live;
        _log = log;
        _live.PlayerJoined += DeliverOfflineMessagesAsync;
    }

    public object List(long id)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var l = _live.Live(id);
        Dictionary<string, LivePlayer> online;
        lock (l.Gate) online = l.Players.Values.Where(p => p.SteamId is { Length: > 0 }).GroupBy(p => p.SteamId!).ToDictionary(g => g.Key, g => g.First());
        var roles = AdminConfig.ReadElevated(AdminConfig.FileOf(def)).ToDictionary(e => e.SteamId, e => e.Permission);
        return data.Players().Select(p => new
        {
            p.SteamId, p.EntityId, p.Name, p.FirstSeenUtc, p.LastSeenUtc, p.PlaytimeSeconds,
            online = online.ContainsKey(p.SteamId),
            playfield = online.TryGetValue(p.SteamId, out var o) ? o.Playfield : null,
            role = roles.TryGetValue(p.SteamId, out var r) ? AdminConfig.RoleName(r) : "player",
        }).OrderByDescending(p => p.online).ThenByDescending(p => p.LastSeenUtc).ToList();
    }

    public async Task<object> DetailAsync(long id, string steamId)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var seen = data.Players().FirstOrDefault(p => p.SteamId == steamId) ?? throw new ServerException("player_not_found");
        var l = _live.Live(id);
        LivePlayer? online;
        lock (l.Gate) online = l.Players.Values.FirstOrDefault(p => p.SteamId == steamId);
        JsonElement? full = null;
        string? liveError = null;
        if (online is not null)
        {
            try { full = await _live.RequestAsync(id, new() { ["op"] = "player", ["id"] = online.Id, ["full"] = true }); }
            catch (ServerException e) { liveError = e.Code; }
        }
        var roles = AdminConfig.ReadElevated(AdminConfig.FileOf(def));
        return new
        {
            seen.SteamId, seen.EntityId, seen.Name, seen.FirstSeenUtc, seen.LastSeenUtc, seen.PlaytimeSeconds,
            online = online is not null,
            permission = roles.FirstOrDefault(r => r.SteamId == steamId)?.Permission ?? 0,
            info = full,
            liveError,
            pendingMessages = data.PendingOfflineMessages(steamId),
            events = data.PlayerEvents(steamId, 300),
            chat = data.PlayerChat(steamId, 200),
        };
    }

    public object Items(long id, string language)
    {
        var def = _manager.Definition(id);
        string? scenario = null;
        try { scenario = File.Exists(def.YamlFile) ? DedicatedYaml.Load(def.YamlFile).Get("GameConfig", "CustomScenario") : null; } catch { }
        return ItemCatalog.Load(def, scenario, language).Select(i => new { i.Id, i.Name, i.Label, i.IsBlock });
    }

    public async Task<object> BannedAsync(long id)
    {
        var answer = await _live.RequestAsync(id, new() { ["op"] = "banned" });
        var list = new List<object>();
        if (answer.TryGetProperty("list", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var names = _live.Data(_manager.Definition(id)).Players().ToDictionary(p => p.SteamId, p => p.Name);
            foreach (var b in arr.EnumerateArray())
            {
                var steam = b.TryGetProperty("steamId", out var s) ? s.GetString() ?? "" : "";
                long ticks = b.TryGetProperty("until", out var u) && u.ValueKind == JsonValueKind.Number ? (long)u.GetDouble() : 0;
                DateTime? until = null;
                try { if (ticks > 0) until = DateTime.FromBinary(ticks).ToUniversalTime(); } catch { }
                list.Add(new { steamId = steam, name = names.GetValueOrDefault(steam), untilUtc = until });
            }
        }
        return list;
    }

    public async Task<object?> ActAsync(long id, string steamId, PlayerActionRequest r, string by, string role)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var seen = data.Players().FirstOrDefault(p => p.SteamId == steamId) ?? throw new ServerException("player_not_found");
        var l = _live.Live(id);
        LivePlayer? online;
        lock (l.Gate) online = l.Players.Values.FirstOrDefault(p => p.SteamId == steamId);
        int Entity() => online?.Id ?? throw new ServerException("player_offline");
        void Need(string min) { if (!Eam.Core.Security.Roles.AtLeast(role, min)) throw new ServerException("forbidden"); }
        string detail;
        object? result = null;

        switch (r.Action)
        {
            case "message":
            {
                Need("moderator");
                var text = Clean(r.Text, 300);
                if (online is not null)
                {
                    await _live.RequestAsync(id, new() { ["op"] = "msg_player", ["id"] = online.Id, ["text"] = text, ["prio"] = 1, ["secs"] = 15 });
                    detail = text;
                }
                else
                {
                    data.AddOfflineMessage(steamId, text, by);
                    detail = "(offline) " + text;
                    result = new { queued = true };
                }
                break;
            }
            case "kick":
            {
                Need("moderator");
                var reason = Clean(r.Reason ?? "Kicked by an admin", 120).Replace("'", "’");
                await _manager.ConsoleAsync(id, $"kick {Entity()} '{reason}'", by);
                detail = reason;
                break;
            }
            case "ban":
            {
                Need("moderator");
                var d = (r.Duration ?? "1d").Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(d, @"^\d{1,4}[mhdy]$")) throw new ServerException("invalid_duration");
                await _manager.ConsoleAsync(id, $"ban {steamId} {d}", by);
                detail = d + (string.IsNullOrWhiteSpace(r.Reason) ? "" : " · " + Clean(r.Reason, 200));
                break;
            }
            case "unban":
                Need("moderator");
                await _manager.ConsoleAsync(id, $"unban {steamId}", by);
                detail = "";
                break;
            case "credits_add":
            case "credits_set":
            {
                Need("gamemaster");
                var amount = r.Amount ?? throw new ServerException("invalid_amount");
                if (Math.Abs(amount) > 1e12 || r.Action == "credits_set" && amount < 0) throw new ServerException("invalid_amount");
                await _live.RequestAsync(id, new() { ["op"] = r.Action, ["id"] = Entity(), ["credits"] = amount });
                detail = (r.Action == "credits_add" && amount >= 0 ? "+" : "") + amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                break;
            }
            case "progress":
            {
                Need("gamemaster");
                if (r.Exp is < 0 || r.Upgrade is < 0) throw new ServerException("invalid_amount");
                var req = new Dictionary<string, object?> { ["op"] = "player_set", ["id"] = Entity() };
                if (r.Exp is { } xp) req["exp"] = xp;
                if (r.Upgrade is { } up) req["upgrade"] = up;
                await _live.RequestAsync(id, req);
                detail = $"XP {r.Exp?.ToString() ?? "="} · points {r.Upgrade?.ToString() ?? "="}";
                break;
            }
            case "heal":
            {
                Need("gamemaster");
                var max = Math.Max(1, (int)(online?.HealthMax ?? 500));
                await _live.RequestAsync(id, new() { ["op"] = "player_set", ["id"] = Entity(), ["health"] = max, ["food"] = 10000, ["oxygen"] = 10000, ["stamina"] = 10000, ["radiation"] = 0 });
                detail = "";
                break;
            }
            case "item_add":
            {
                Need("gamemaster");
                var item = r.ItemId ?? throw new ServerException("invalid_item");
                var count = r.Count ?? 1;
                if (item <= 0 || count is < 1 or > 100000) throw new ServerException("invalid_item");
                await _live.RequestAsync(id, new() { ["op"] = "item_add", ["id"] = Entity(), ["item"] = item, ["count"] = count });
                detail = $"{count} × {item}";
                break;
            }
            case "inventory_set":
            {
                Need("admin");
                object Stacks(List<StackDto>? s) => (s ?? []).Where(x => x.Id > 0 && x.Count > 0)
                    .Select(x => new Dictionary<string, object?> { ["id"] = x.Id, ["count"] = x.Count, ["slot"] = x.Slot, ["ammo"] = x.Ammo, ["decay"] = x.Decay }).ToList();
                await _live.RequestAsync(id, new() { ["op"] = "inventory_set", ["id"] = Entity(), ["toolbelt"] = Stacks(r.Toolbelt), ["bag"] = Stacks(r.Bag) });
                detail = $"{r.Toolbelt?.Count ?? 0} + {r.Bag?.Count ?? 0} stacks";
                break;
            }
            case "teleport":
            {
                Need("gamemaster");
                var req = new Dictionary<string, object?> { ["op"] = "teleport", ["id"] = Entity(), ["x"] = r.X ?? 0, ["y"] = r.Y ?? 0, ["z"] = r.Z ?? 0 };
                var pf = r.Playfield?.Trim();
                if (!string.IsNullOrEmpty(pf) && !string.Equals(pf, online!.Playfield, StringComparison.OrdinalIgnoreCase)) req["pf"] = pf;
                await _live.RequestAsync(id, req, TimeSpan.FromSeconds(60));
                detail = $"{pf ?? online!.Playfield} ({r.X:0}, {r.Y:0}, {r.Z:0})";
                break;
            }
            case "bp_finish":
                Need("gamemaster");
                await _live.RequestAsync(id, new() { ["op"] = "bp_finish", ["id"] = Entity() });
                detail = "";
                break;
            case "role":
            {
                Need("admin");
                var p = r.Permission ?? 0;
                try { AdminConfig.SetRole(AdminConfig.FileOf(def), steamId, p); }
                catch (FormatException e) { throw new ServerException(e.Message); }
                detail = AdminConfig.RoleName(p);
                result = new { appliesOnRestart = true };
                break;
            }
            default:
                throw new ServerException("unknown_action");
        }
        data.PlayerEvent(steamId, seen.Name, "admin_" + r.Action, detail, by);
        ServerManager.ServerLog(def, $"{by}: {r.Action} on {seen.Name} ({steamId}) {detail}");
        return result;
    }

    private static string Clean(string? text, int max)
    {
        var t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (t.Length == 0) throw new ServerException("message_empty");
        return t.Length > max ? t[..max] : t;
    }

    private async Task DeliverOfflineMessagesAsync(ServerDefinition def, LivePlayer p)
    {
        if (p.SteamId is not { Length: > 0 } steam) return;
        var data = _live.Data(def);
        if (data.PendingOfflineMessages(steam) == 0) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(25)); // let the player finish loading
            foreach (var text in data.TakeOfflineMessages(steam))
            {
                await _live.SendToModAsync(def.Id, new { op = "msg_player", id = p.Id, text, prio = 1, secs = 20 });
                data.PlayerEvent(steam, p.Name, "message_delivered", text);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
        catch (Exception e) { _log.LogWarning("Offline messages for {Name} not delivered: {Err}", p.Name, e.Message); }
    }
}
