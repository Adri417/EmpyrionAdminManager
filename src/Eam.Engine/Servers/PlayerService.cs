using System.Text.Json;
using Eam.Core.Servers;
using Eam.Engine.Api;

namespace Eam.Engine.Servers;

public sealed record PlayerActionRequest(
    string Action, string? Text, double? Amount, int? ItemId, int? Count, int? Exp, int? Upgrade,
    string? Playfield, double? X, double? Y, double? Z, string? Duration, int? Permission, string? Reason,
    List<StackDto>? Toolbelt, List<StackDto>? Bag,
    long? SnapshotId = null, string? Name = null, bool? Replace = null, int? Origin = null, int? FactionGroup = null, int? FactionId = null,
    int? FactionRole = null, string? StartPlayfield = null, string? Note = null, bool? Marked = null, bool? BanExempt = null, bool? On = null,
    string? Target = null, int? StructureId = null, List<StackDto>? Items = null);

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
        manager.KickAllPlayers = (id, reason) => KickAllAsync(id, reason, "EAM (restart)");
        manager.RedAlert = live.RedAlertAsync;
        manager.ServerChat = live.ServerChatAsync;
        _live = live;
        _log = log;
        _live.PlayerJoined += DeliverOfflineMessagesAsync;
        _live.PlayerJoined += ApplyPendingInventoryAsync;
    }

    public object List(long id)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var l = _live.Live(id);
        Dictionary<string, LivePlayer> online;
        lock (l.Gate) online = l.Players.Values.Where(p => p.SteamId is { Length: > 0 }).GroupBy(p => p.SteamId!).ToDictionary(g => g.Key, g => g.First());
        var roles = AdminConfig.ReadElevated(AdminConfig.FileOf(def)).ToDictionary(e => e.SteamId, e => e.Permission);
        var metas = data.Metas().ToDictionary(m => m.SteamId);
        return data.Players().Select(p => new
        {
            p.SteamId, p.EntityId, p.Name, p.FirstSeenUtc, p.LastSeenUtc, p.PlaytimeSeconds,
            online = online.ContainsKey(p.SteamId),
            playfield = online.TryGetValue(p.SteamId, out var o) ? o.Playfield : null,
            role = roles.TryGetValue(p.SteamId, out var r) ? AdminConfig.RoleName(r) : "player",
            marked = metas.TryGetValue(p.SteamId, out var m) && m.Marked,
            god = m?.God == true,
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
            pendingInventory = data.Counter(steamId, "pending_inventory") is not null,
            events = data.PlayerEvents(steamId, 300),
            chat = data.PlayerChat(steamId, 200),
            meta = data.Meta(steamId),
            journal = data.Journal(null, 0, 200, null, steamId),
            inventories = data.InventorySnapshots(steamId, 50).Select(x => new { x.Id, x.Utc, x.Reason, x.Playfield, items = CountItems(x.Toolbelt) + CountItems(x.Bag) }),
            saves = PlayerSaves(id, steamId),
            survivalClaimed = data.Counter(steamId, "cb_survival") is not null,
        };
    }

    /// <summary>Picture of an item for this server (its scenario's own icons first, see <see cref="ItemIcons"/>).</summary>
    public string? IconFile(long id, int itemId)
    {
        var def = _manager.Definition(id);
        string? scenario = null;
        try { scenario = File.Exists(def.YamlFile) ? DedicatedYaml.Load(def.YamlFile).Get("GameConfig", "CustomScenario") : null; } catch { }
        var name = ItemCatalog.Load(def, scenario, "en").FirstOrDefault(i => i.Id == itemId)?.Name;
        return ItemIcons.Find(def, scenario, itemId, name);
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
                _ = Entity();
                await KickAsync(id, online!, reason, by);
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
                // Edited only while the player is offline (nobody moves items meanwhile): the new inventory is
                // kept and put in place at the next login, after a copy of the old one.
                if (online is not null) throw new ServerException("inventory_player_online", online.Playfield ?? "");
                var pending = System.Text.Json.JsonSerializer.Serialize(new { toolbelt = r.Toolbelt ?? [], bag = r.Bag ?? [], by });
                data.SetCounter(steamId, "pending_inventory", 1, pending);
                detail = $"(next login) {r.Toolbelt?.Count ?? 0} + {r.Bag?.Count ?? 0} stacks";
                result = new { queued = true };
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
            // ---- version 0.9.5
            case "god":
            {
                Need("gamemaster");
                var m = data.Meta(steamId);
                data.SaveMeta(m with { God = r.On ?? !m.God });
                detail = (r.On ?? !m.God) ? "on" : "off";
                break;
            }
            case "meta":
            {
                Need("moderator");
                var m = data.Meta(steamId);
                data.SaveMeta(m with { Note = r.Note ?? m.Note, Marked = r.Marked ?? m.Marked, BanExempt = r.BanExempt ?? m.BanExempt });
                detail = $"note/marked {r.Marked?.ToString() ?? "="} / ban exempt {r.BanExempt?.ToString() ?? "="}";
                break;
            }
            case "survival_reset":
                Need("gamemaster");
                data.DeleteCounter(steamId, "cb_survival");
                data.DeleteCounter(steamId, "cb_heal");
                detail = "";
                break;
            case "playtime_reset":
                Need("admin");
                data.ResetPlaytime(steamId);
                detail = "";
                break;
            case "player_set":
            {
                Need("admin");
                var req = new Dictionary<string, object?> { ["op"] = "player_set", ["id"] = Entity() };
                if (r.Origin is { } o) req["origin"] = o;
                if (r.FactionGroup is { } fg) req["factionGroup"] = fg;
                if (r.FactionId is { } fi) req["factionId"] = fi;
                if (r.FactionRole is { } fr) req["factionRole"] = fr;
                if (!string.IsNullOrWhiteSpace(r.StartPlayfield)) req["startPlayfield"] = r.StartPlayfield.Trim();
                if (req.Count == 2) throw new ServerException("invalid_value");
                await _live.RequestAsync(id, req);
                detail = string.Join(", ", req.Where(k => k.Key is not ("op" or "id")).Select(k => $"{k.Key}={k.Value}"));
                break;
            }
            case "bp_resources":
            {
                Need("gamemaster");
                var items = (r.Items ?? []).Where(x => x.Id > 0 && x.Count > 0).Select(x => new Dictionary<string, object?> { ["id"] = x.Id, ["count"] = x.Count }).ToList();
                if (items.Count == 0) throw new ServerException("invalid_item");
                await _live.RequestAsync(id, new() { ["op"] = "bp_resources", ["id"] = Entity(), ["items"] = items, ["replace"] = r.Replace == true ? 1 : 0 });
                detail = string.Join(", ", items.Select(i => $"{i["count"]} × {i["id"]}")) + (r.Replace == true ? " (replace)" : "");
                break;
            }
            case "inventory_restore":
            case "inventory_template_load":
            {
                Need("admin");
                string toolbelt, bag;
                if (r.Action == "inventory_restore")
                {
                    var snap = data.InventorySnapshotById(r.SnapshotId ?? 0) ?? throw new ServerException("not_found");
                    if (snap.SteamId != steamId) throw new ServerException("not_found");
                    (toolbelt, bag) = (snap.Toolbelt, snap.Bag);
                    detail = $"copy #{snap.Id} of {snap.Utc:yyyy-MM-dd HH:mm} ({snap.Reason})";
                }
                else
                {
                    var tpl = data.InventoryTemplates().FirstOrDefault(t => t.Name == r.Name) ?? throw new ServerException("not_found");
                    (toolbelt, bag) = (tpl.Toolbelt, tpl.Bag);
                    detail = "template " + tpl.Name;
                }
                if (online is null) throw new ServerException("player_offline");
                await SaveCurrentAsync(def, online, "before_restore");
                if (r.Replace == false)
                {
                    // "Give the items": added to what the player has now (anything that does not fit drops in an "items" box).
                    foreach (var st in JsonDocument.Parse(toolbelt).RootElement.EnumerateArray().Concat(JsonDocument.Parse(bag).RootElement.EnumerateArray()))
                        await _live.RequestAsync(id, new() { ["op"] = "item_add", ["id"] = Entity(), ["item"] = st.GetProperty("id").GetInt32(), ["count"] = st.GetProperty("count").GetInt32() });
                    detail += " (added)";
                }
                else
                    await _live.RequestAsync(id, new() { ["op"] = "inventory_set", ["id"] = Entity(), ["toolbelt"] = JsonDocument.Parse(toolbelt).RootElement, ["bag"] = JsonDocument.Parse(bag).RootElement });
                break;
            }
            case "inventory_snapshot":
            {
                Need("gamemaster");
                if (online is null) throw new ServerException("player_offline");
                var newId = await SaveCurrentAsync(def, online, "manual");
                detail = "#" + newId;
                result = new { id = newId };
                break;
            }
            case "inventory_keep":
                Need("admin");
                data.KeepInventorySnapshot(r.SnapshotId ?? 0, r.On ?? true);
                detail = "#" + r.SnapshotId;
                break;
            case "inventory_template_save":
            {
                Need("admin");
                var name = (r.Name ?? "").Trim();
                if (name.Length is 0 or > 40) throw new ServerException("invalid_name");
                var info = await _live.RequestAsync(id, new() { ["op"] = "player", ["id"] = Entity(), ["full"] = true });
                data.SaveInventoryTemplate(name, Raw(info, "toolbelt"), Raw(info, "bag"));
                detail = name;
                break;
            }
            case "teleport_position":
            {
                Need("gamemaster");
                var pos = data.SavedPositions().FirstOrDefault(p => p.Name == r.Name) ?? throw new ServerException("not_found");
                await TeleportAsync(id, online, pos.Playfield, pos.X, pos.Y, pos.Z);
                detail = $"{pos.Name}: {pos.Playfield} ({pos.X:0}, {pos.Y:0}, {pos.Z:0})";
                break;
            }
            case "teleport_to_player":
            {
                Need("gamemaster");
                var key = (r.Target ?? "").Trim();
                var target = _live.OnlineBySteam(id, key)
                    ?? _live.OnlinePlayers(id).FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ServerException("player_offline");
                await TeleportAsync(id, online, target.Playfield, target.X, target.Y + 2, target.Z);
                detail = "→ " + target.Name;
                break;
            }
            case "teleport_to_structure":
            {
                Need("gamemaster");
                var st = data.Structure(r.StructureId ?? 0) ?? throw new ServerException("not_found");
                await TeleportAsync(id, online, st.Playfield, st.X, st.Y + 10, st.Z);
                detail = $"→ {st.Name} ({st.Id})";
                break;
            }
            case "save_file":
            {
                Need("admin");
                var src = PlayerFile(def, steamId);
                if (!File.Exists(src)) throw new ServerException("not_found");
                var dir = Path.Combine(def.EamDir, "player-saves", steamId);
                Directory.CreateDirectory(dir);
                var name = $"{DateTime.Now:yyyyMMdd-HHmmss}.ply";
                File.Copy(src, Path.Combine(dir, name));
                detail = name;
                result = new { file = name };
                break;
            }
            case "load_file":
            case "reset":
            {
                Need("admin");
                if (online is not null) throw new ServerException("player_must_be_offline");
                var dst = PlayerFile(def, steamId);
                var aside = Path.Combine(def.EamDir, "players-removed", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                if (File.Exists(dst)) { Directory.CreateDirectory(aside); File.Move(dst, Path.Combine(aside, Path.GetFileName(dst))); }
                if (r.Action == "load_file")
                {
                    var name = Path.GetFileName(r.Name ?? "");
                    var src = Path.Combine(def.EamDir, "player-saves", steamId, name);
                    if (name.Length == 0 || !File.Exists(src)) throw new ServerException("not_found");
                    File.Copy(src, dst);
                    detail = name;
                }
                else detail = "player file moved to " + aside;
                break;
            }
            case "impersonate":
            case "impersonate_end":
            {
                Need("admin");
                var me = _live.OnlineBySteam(id, r.Target ?? "") ?? throw new ServerException("admin_not_in_game");
                if (r.Action == "impersonate")
                {
                    if (online is null) throw new ServerException("player_offline");
                    var mine = await _live.RequestAsync(id, new() { ["op"] = "player", ["id"] = me.Id, ["full"] = true });
                    data.AddInventorySnapshot(me.SteamId!, me.Name, "before_impersonate", me.Playfield, Raw(mine, "toolbelt"), Raw(mine, "bag"), DateTime.UtcNow);
                    data.SetCounter(me.SteamId!, "impersonate", 1, JsonSerializer.Serialize(new { pf = me.Playfield, me.X, me.Y, me.Z, fg = LiveService.Int(mine, "factionGroup"), fid = LiveService.Int(mine, "factionId") }));
                    var his = await _live.RequestAsync(id, new() { ["op"] = "player", ["id"] = online.Id, ["full"] = true });
                    await _live.RequestAsync(id, new() { ["op"] = "inventory_set", ["id"] = me.Id, ["toolbelt"] = his.GetProperty("toolbelt"), ["bag"] = his.GetProperty("bag") });
                    await _live.RequestAsync(id, new() { ["op"] = "player_set", ["id"] = me.Id, ["factionGroup"] = LiveService.Int(his, "factionGroup"), ["factionId"] = LiveService.Int(his, "factionId") });
                    await TeleportAsync(id, me, online.Playfield, online.X, online.Y + 2, online.Z);
                    detail = $"{me.Name} takes the place of {online.Name}";
                }
                else
                {
                    var back = data.InventorySnapshots(me.SteamId, 1, "before_impersonate").FirstOrDefault() ?? throw new ServerException("not_found");
                    await _live.RequestAsync(id, new() { ["op"] = "inventory_set", ["id"] = me.Id, ["toolbelt"] = JsonDocument.Parse(back.Toolbelt).RootElement, ["bag"] = JsonDocument.Parse(back.Bag).RootElement });
                    if (data.Counter(me.SteamId!, "impersonate")?.Value is { } v)
                    {
                        var o = JsonDocument.Parse(v).RootElement;
                        await _live.RequestAsync(id, new() { ["op"] = "player_set", ["id"] = me.Id, ["factionGroup"] = LiveService.Int(o, "fg"), ["factionId"] = LiveService.Int(o, "fid") });
                        await TeleportAsync(id, me, LiveService.Str(o, "pf"), LiveService.Num(o, "X"), LiveService.Num(o, "Y"), LiveService.Num(o, "Z"));
                        data.DeleteCounter(me.SteamId!, "impersonate");
                    }
                    detail = $"{me.Name} back to themself";
                }
                break;
            }
            default:
                throw new ServerException("unknown_action");
        }
        data.PlayerEvent(steamId, seen.Name, "admin_" + r.Action, detail, by);
        ServerManager.ServerLog(def, $"{by}: {r.Action} on {seen.Name} ({steamId}) {detail}");
        return result;
    }

    private static int CountItems(string json) { try { return JsonDocument.Parse(json).RootElement.GetArrayLength(); } catch { return 0; } }

    private static string Raw(JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.GetRawText() : "[]";

    public static string PlayerFile(ServerDefinition def, string steamId) =>
        Path.Combine(def.GameDir, "Saves", "Games", WorldService.SaveName(def), "Players", steamId + ".ply");

    private async Task<long> SaveCurrentAsync(ServerDefinition def, LivePlayer? p, string reason)
    {
        if (p?.SteamId is null) throw new ServerException("player_offline");
        var info = await _live.RequestAsync(def.Id, new() { ["op"] = "player", ["id"] = p.Id, ["full"] = true });
        return _live.Data(def).AddInventorySnapshot(p.SteamId, p.Name, reason, p.Playfield, Raw(info, "toolbelt"), Raw(info, "bag"), DateTime.UtcNow);
    }

    private async Task TeleportAsync(long id, LivePlayer? p, string? pf, double x, double y, double z)
    {
        if (p is null) throw new ServerException("player_offline");
        var req = new Dictionary<string, object?> { ["op"] = "teleport", ["id"] = p.Id, ["x"] = x, ["y"] = y, ["z"] = z };
        if (!string.IsNullOrEmpty(pf) && !string.Equals(pf, p.Playfield, StringComparison.OrdinalIgnoreCase)) req["pf"] = pf;
        await _live.RequestAsync(id, req, TimeSpan.FromSeconds(60));
    }

    /// <summary>Kicks every online player (before a maintenance).</summary>
    /// <summary>
    /// Kicks by Steam ID (works whatever playfield the player is on). Sent through the EAM mod when it is
    /// connected, otherwise through telnet; the game's answer is written in the server log, and a warning
    /// is added if the player is still online a few seconds later.
    /// </summary>
    private async Task KickAsync(long id, LivePlayer p, string reason, string by)
    {
        var def = _manager.Definition(id);
        var target = p.SteamId is { Length: > 0 } steam ? steam : p.Id.ToString();
        var cmd = $"kick {target} '{reason}'";
        string answer;
        try { await _live.SendToModAsync(id, new { op = "console", cmd }); answer = "(sent through the EAM mod)"; }
        catch (ServerException) { answer = await _manager.ConsoleAsync(id, cmd, by); }
        ServerManager.ServerLog(def, $"Kick {p.Name} ({target}) by {by}: {(string.IsNullOrWhiteSpace(answer) ? "no answer" : answer.Trim())}");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            if (_live.Online(id, p.Id) is not null)
            {
                // Second try through telnet with the entity number, in case this game version wants it.
                try
                {
                    var again = await _manager.ConsoleAsync(id, $"kick {p.Id} '{reason}'", by);
                    ServerManager.ServerLog(def, $"Kick {p.Name}: still online after 10 s, tried again with the entity id: {again.Trim()}");
                }
                catch (Exception e) { ServerManager.ServerLog(def, $"Kick {p.Name}: still online after 10 s ({e.Message})"); }
            }
        });
    }

    /// <summary>Ban or unban any Steam ID, even a player never seen on this server.</summary>
    public async Task BanSteamAsync(long id, Eam.Engine.Api.BanSteamRequest r, string by)
    {
        var steam = (r.SteamId ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(steam, @"^\d{17}$")) throw new ServerException("invalid_steam_id");
        var def = _manager.Definition(id);
        if (r.Unban)
        {
            await _manager.ConsoleAsync(id, $"unban {steam}", by);
            ServerManager.ServerLog(def, $"{by}: unban {steam}");
            return;
        }
        var d = (r.Duration ?? "1d").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(d, @"^\d{1,4}[mhdy]$")) throw new ServerException("invalid_duration");
        await _manager.ConsoleAsync(id, $"ban {steam} {d}", by);
        ServerManager.ServerLog(def, $"{by}: ban {steam} {d}{(string.IsNullOrWhiteSpace(r.Reason) ? "" : " · " + Clean(r.Reason, 200))}");
    }

    /// <summary>Factions known by the game, with their members online now.</summary>
    public object Factions(long id)
    {
        var online = _live.OnlinePlayers(id);
        return _live.FactionDetails(id).Select(f => new
        {
            f.Id, f.Abbrev, f.Name,
            online = online.Where(p => p.FactionId == f.Id).Select(p => new { p.Name, p.SteamId, p.Playfield }).ToList(),
        }).OrderBy(f => f.Abbrev).ToList();
    }

    public async Task<int> KickAllAsync(long id, string reason, string by)
    {
        var n = 0;
        reason = Clean(string.IsNullOrWhiteSpace(reason) ? "Server maintenance" : reason, 120).Replace("'", "’");
        foreach (var p in _live.OnlinePlayers(id))
        {
            try { await KickAsync(id, p, reason, by); n++; } catch (ServerException) { }
        }
        ServerManager.ServerLog(_manager.Definition(id), $"{by}: kicked all players ({n})");
        return n;
    }

    public object Inventories(long id, string? steamId, string? reason)
    {
        var data = _live.Data(_manager.Definition(id));
        return data.InventorySnapshots(string.IsNullOrEmpty(steamId) ? null : steamId, 300, string.IsNullOrEmpty(reason) ? null : reason).Select(s => new
        {
            s.Id, s.SteamId, s.Name, s.Utc, s.Reason, s.Playfield,
            toolbelt = JsonDocument.Parse(s.Toolbelt).RootElement, bag = JsonDocument.Parse(s.Bag).RootElement,
        });
    }

    public object PlayerSaves(long id, string steamId)
    {
        var dir = Path.Combine(_manager.Definition(id).EamDir, "player-saves", steamId);
        return Directory.Exists(dir) ? new DirectoryInfo(dir).EnumerateFiles("*.ply").OrderByDescending(f => f.Name).Select(f => new { file = f.Name, size = f.Length }).ToList() : [];
    }

    private static string Clean(string? text, int max)
    {
        var t = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (t.Length == 0) throw new ServerException("message_empty");
        return t.Length > max ? t[..max] : t;
    }

    private sealed record PendingInventory(List<StackDto> Toolbelt, List<StackDto> Bag, string? By);

    /// <summary>Inventory edited while the player was offline: applied once they are in game (old one kept first).</summary>
    private async Task ApplyPendingInventoryAsync(ServerDefinition def, LivePlayer p)
    {
        if (p.SteamId is not { Length: > 0 } steam) return;
        var data = _live.Data(def);
        if (data.Counter(steam, "pending_inventory")?.Value is not { Length: > 0 } json) return;
        try
        {
            var inv = System.Text.Json.JsonSerializer.Deserialize<PendingInventory>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            await Task.Delay(TimeSpan.FromSeconds(20)); // let the player finish loading
            await SaveCurrentAsync(def, p, "before_edit");
            object Stacks(List<StackDto>? s) => (s ?? []).Where(x => x.Id > 0 && x.Count > 0)
                .Select(x => new Dictionary<string, object?> { ["id"] = x.Id, ["count"] = x.Count, ["slot"] = x.Slot, ["ammo"] = x.Ammo, ["decay"] = x.Decay }).ToList();
            await _live.RequestAsync(def.Id, new() { ["op"] = "inventory_set", ["id"] = p.Id, ["toolbelt"] = Stacks(inv.Toolbelt), ["bag"] = Stacks(inv.Bag) });
            data.DeleteCounter(steam, "pending_inventory");
            data.PlayerEvent(steam, p.Name, "inventory_applied", $"{inv.Toolbelt.Count} + {inv.Bag.Count} stacks", inv.By);
            ServerManager.ServerLog(def, $"Inventory edited by {inv.By} applied to {p.Name} at login");
        }
        catch (Exception e) { _log.LogWarning("Pending inventory for {Name} not applied: {Err}", p.Name, e.Message); }
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
