using System.Globalization;
using System.Text.Json;
using Eam.Core.Servers;

namespace Eam.Engine.Servers;

public sealed record GameEventRequest(string Name, string Playfield, double X, double Y, double Z, List<KitItem>? Kit, bool KeepInventory);

/// <summary>
/// Step 9: gameplay added by EAM. Autominer (virtual miners bought with credits, commands am:…)
/// and in-game events (registration EV:…, teleport in with an event kit, inventory given back at the end).
/// </summary>
public sealed class GameplayService
{
    private readonly ServerManager _manager;
    private readonly LiveService _live;
    private readonly CommunityService _community;
    private readonly Alerts _alerts;
    private readonly ILogger<GameplayService> _log;

    public GameplayService(ServerManager manager, LiveService live, CommunityService community, Alerts alerts, ILogger<GameplayService> log)
    {
        _manager = manager;
        _live = live;
        _community = community;
        _alerts = alerts;
        _log = log;
        community.CommandHandlers.Add(OnCommandAsync);
        live.PlayerJoined += OnJoinAsync;
    }

    public GameplaySettings Settings(ServerDefinition def) => FeatureStore.Load<GameplaySettings>(_live.Data(def), "gameplay");

    private async Task<bool> OnCommandAsync(ServerDefinition def, LivePlayer p, string text)
    {
        var s = Settings(def);
        try
        {
            if (s.AutominerEnabled && text.StartsWith(s.AutominerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                await AutominerAsync(def, s, p, text[s.AutominerPrefix.Length..].Trim());
                return true;
            }
            if (s.EventsEnabled && text.StartsWith(s.EventsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                await EventCommandAsync(def, p, text[s.EventsPrefix.Length..].Trim());
                return true;
            }
        }
        catch (ServerException e) { await _community.ReplyAsync(def, p, "EAM: " + e.Code + (e.Detail is null ? "" : " " + e.Detail)); return true; }
        return false;
    }

    // ------------------------------------------------------------------ autominer

    /// <summary>Production since the last look, limited by the fuel left and the storage time.</summary>
    internal static (List<MinerRow> Miners, double Fuel) Accrue(IReadOnlyList<MinerRow> miners, IReadOnlyList<OreConfig> ores, double fuel, DateTime lastUtc, DateTime nowUtc, int maxStoredHours)
    {
        var hours = Math.Max(0, (nowUtc - lastUtc).TotalHours);
        var run = Math.Min(hours, fuel);
        var result = new List<MinerRow>();
        foreach (var m in miners)
        {
            var ore = ores.FirstOrDefault(o => o.Key.Equals(m.Ore, StringComparison.OrdinalIgnoreCase));
            if (ore is null) { result.Add(m); continue; }
            var cap = ore.PerHour * m.Level * maxStoredHours;
            result.Add(m with { Stored = Math.Min(cap, m.Stored + ore.PerHour * m.Level * run), UpdatedUtc = nowUtc });
        }
        return (result, Math.Max(0, fuel - run));
    }

    private (List<MinerRow> Miners, double Fuel) Load(ServerData data, GameplaySettings s, string steam)
    {
        var fuelRow = data.Counter(steam, "am_fuel");
        var fuel = double.TryParse(fuelRow?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0;
        var miners = data.Miners(steam);
        var last = fuelRow?.LastUtc ?? DateTime.UtcNow;
        var (updated, left) = Accrue(miners, s.Ores, fuel, last, DateTime.UtcNow, s.MaxStoredHours);
        foreach (var m in updated) data.SaveMiner(m);
        data.SetCounter(steam, "am_fuel", 0, left.ToString(CultureInfo.InvariantCulture));
        return (updated, left);
    }

    private async Task PayAsync(ServerDefinition def, LivePlayer p, double amount)
    {
        if (amount <= 0) return;
        var info = await _live.RequestAsync(def.Id, new() { ["op"] = "player", ["id"] = p.Id });
        if (LiveService.Num(info, "credits") < amount) throw new ServerException("not_enough_credits", $"{amount:0}");
        await _live.RequestAsync(def.Id, new() { ["op"] = "credits_add", ["id"] = p.Id, ["credits"] = -amount });
    }

    private async Task AutominerAsync(ServerDefinition def, GameplaySettings s, LivePlayer p, string command)
    {
        if (p.SteamId is not { } steam) return;
        var data = _live.Data(def);
        var parts = command.Split([' ', ':'], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "info";
        var arg = parts.Length > 1 ? parts[1] : "";
        var (miners, fuel) = Load(data, s, steam);
        OreConfig Ore() => s.Ores.FirstOrDefault(o => o.Key.Equals(arg, StringComparison.OrdinalIgnoreCase))
                           ?? throw new ServerException("unknown_ore", string.Join(", ", s.Ores.Select(o => o.Key)));
        switch (verb)
        {
            case "info":
            case "?":
            {
                var lines = miners.Select(m => $"{m.Ore}: niv./lvl {m.Level}, stock {Math.Floor(m.Stored)}").ToList();
                lines.Add($"Carburant / fuel: {fuel:0.#} h");
                lines.Add($"{s.AutominerPrefix}buy <ore> · {s.AutominerPrefix}get <ore|all> · {s.AutominerPrefix}fuel <h> · {s.AutominerPrefix}sell <ore>");
                lines.Add(string.Join(", ", s.Ores.Select(o => $"{o.Key} ({o.Price})")));
                await _community.ReplyAsync(def, p, string.Join("\n", lines));
                break;
            }
            case "buy":
            {
                var ore = Ore();
                var current = miners.FirstOrDefault(m => m.Ore.Equals(ore.Key, StringComparison.OrdinalIgnoreCase));
                var level = (current?.Level ?? 0) + 1;
                if (level > s.AutominerMaxLevel) throw new ServerException("max_level");
                var price = (double)ore.Price * level;
                await PayAsync(def, p, price);
                data.SaveMiner(new MinerRow(steam, ore.Key, level, current?.Stored ?? 0, 0, DateTime.UtcNow));
                data.AddJournal("autominer", 0, $"{ore.Key} level {level} for {price:0}", steam, p.Name, p.Playfield);
                await _community.ReplyAsync(def, p, $"EAM: {ore.Key} niv./lvl {level} (-{price:0})");
                break;
            }
            case "sell":
            {
                var ore = Ore();
                var current = miners.FirstOrDefault(m => m.Ore.Equals(ore.Key, StringComparison.OrdinalIgnoreCase)) ?? throw new ServerException("unknown_ore");
                var refund = Math.Floor(ore.Price * current.Level / 2.0);
                data.DeleteMiner(steam, current.Ore);
                if (refund > 0) await _live.RequestAsync(def.Id, new() { ["op"] = "credits_add", ["id"] = p.Id, ["credits"] = refund });
                await _community.ReplyAsync(def, p, $"EAM: {ore.Key} vendu / sold (+{refund:0})");
                break;
            }
            case "fuel":
            {
                if (!int.TryParse(arg, out var hours) || hours is < 1 or > 24 * 30) throw new ServerException("invalid_amount");
                await PayAsync(def, p, (double)s.FuelPricePerHour * hours);
                data.SetCounter(steam, "am_fuel", 0, (fuel + hours).ToString(CultureInfo.InvariantCulture));
                await _community.ReplyAsync(def, p, $"EAM: +{hours} h (total {fuel + hours:0.#} h)");
                break;
            }
            case "get":
            {
                var list = arg.Equals("all", StringComparison.OrdinalIgnoreCase) || arg.Length == 0 ? miners
                    : miners.Where(m => m.Ore.Equals(arg, StringComparison.OrdinalIgnoreCase)).ToList();
                var given = new List<string>();
                foreach (var m in list)
                {
                    var ore = s.Ores.FirstOrDefault(o => o.Key.Equals(m.Ore, StringComparison.OrdinalIgnoreCase));
                    var count = (int)Math.Floor(m.Stored);
                    if (ore is null || count <= 0) continue;
                    await _live.RequestAsync(def.Id, new() { ["op"] = "item_add", ["id"] = p.Id, ["item"] = ore.ItemId, ["count"] = count });
                    data.SaveMiner(m with { Stored = m.Stored - count });
                    given.Add($"{count} {m.Ore}");
                }
                await _community.ReplyAsync(def, p, given.Count == 0 ? "EAM: stock vide / nothing stored" : "EAM: " + string.Join(", ", given));
                break;
            }
            default:
                await _community.ReplyAsync(def, p, $"EAM: {s.AutominerPrefix}info");
                break;
        }
    }

    public object Miners(long id)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var names = data.Players().ToDictionary(p => p.SteamId, p => p.Name);
        return data.Miners().Select(m => new { m.SteamId, name = names.GetValueOrDefault(m.SteamId), m.Ore, m.Level, stored = Math.Floor(m.Stored), m.UpdatedUtc });
    }

    /// <summary>The 13 usual ores, with the item numbers of the server's scenario.</summary>
    public object DefaultOres(long id)
    {
        var def = _manager.Definition(id);
        string? scenario = null;
        try { scenario = File.Exists(def.YamlFile) ? DedicatedYaml.Load(def.YamlFile).Get("GameConfig", "CustomScenario") : null; } catch { }
        var items = ItemCatalog.Load(def, scenario, "en");
        return GameplaySettings.DefaultOres
            .Select(n => items.FirstOrDefault(i => i.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
            .Where(i => i is not null)
            .Select(i => new OreConfig { Key = i!.Name.Replace("Ore", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant(), ItemId = i.Id, PerHour = 10, Price = 10000 })
            .ToList();
    }

    // ------------------------------------------------------------------ events

    private async Task EventCommandAsync(ServerDefinition def, LivePlayer p, string command)
    {
        if (p.SteamId is not { } steam) return;
        var data = _live.Data(def);
        var parts = command.Split([' ', ':'], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "list";
        switch (verb)
        {
            case "list":
            {
                var open = data.GameEvents().Where(e => e.State is "open" or "running").ToList();
                await _community.ReplyAsync(def, p, open.Count == 0 ? "EAM: aucun événement / no event"
                    : string.Join("\n", open.Select(e => $"#{e.Id} {e.Name} ({e.State}) → EV:join {e.Id}")));
                break;
            }
            case "join":
            {
                var ev = (parts.Length > 1 && long.TryParse(parts[1], out var eid) ? data.GameEvent(eid) : data.GameEvents().FirstOrDefault(e => e.State == "open"))
                         ?? throw new ServerException("event_not_found");
                if (ev.State != "open") throw new ServerException("event_closed");
                data.SaveGameEventPlayer(new GameEventPlayer(ev.Id, steam, p.Name, "registered", null, null, 0, 0, 0));
                await _community.ReplyAsync(def, p, $"EAM: inscrit à / registered for « {ev.Name} »");
                break;
            }
            case "exit":
            case "leave":
            {
                var mine = data.GameEventPlayers(null, steam).FirstOrDefault(x => x.State is "in" or "registered") ?? throw new ServerException("event_not_found");
                if (mine.State == "registered") data.DeleteGameEventPlayer(mine.EventId, steam);
                else await BringBackAsync(def, mine, p);
                await _community.ReplyAsync(def, p, "EAM: OK");
                break;
            }
            default:
                await _community.ReplyAsync(def, p, "EV:list · EV:join <n> · EV:exit");
                break;
        }
    }

    public object Events(long id)
    {
        var data = _live.Data(_manager.Definition(id));
        return data.GameEvents().Select(e => new
        {
            e.Id, e.Name, e.Playfield, e.X, e.Y, e.Z, kit = JsonSerializer.Deserialize<List<KitItem>>(e.Kit, FeatureStore.Json), e.KeepInventory, e.State, e.CreatedUtc,
            players = data.GameEventPlayers(e.Id).Select(x => new { x.SteamId, x.Name, x.State }),
        });
    }

    public long SaveEvent(long id, long eventId, GameEventRequest r)
    {
        var data = _live.Data(_manager.Definition(id));
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 60 || string.IsNullOrWhiteSpace(r.Playfield)) throw new ServerException("invalid_event");
        var kit = (r.Kit ?? []).Where(k => k.Id > 0 && k.Count > 0).ToList();
        var existing = eventId == 0 ? null : data.GameEvent(eventId) ?? throw new ServerException("event_not_found");
        if (existing is { State: "running" }) throw new ServerException("event_running");
        return data.SaveGameEvent(new GameEventRow(eventId, r.Name.Trim(), r.Playfield.Trim(), r.X, r.Y, r.Z, JsonSerializer.Serialize(kit, FeatureStore.Json),
            r.KeepInventory, existing?.State ?? "draft", existing?.CreatedUtc ?? DateTime.UtcNow));
    }

    public async Task EventActionAsync(long id, long eventId, string action, string by)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var ev = data.GameEvent(eventId) ?? throw new ServerException("event_not_found");
        switch (action)
        {
            case "open":
                data.SaveGameEvent(ev with { State = "open" });
                try { await _live.SendAsync(def.Id, $"Événement / event « {ev.Name} » : EV:join {ev.Id}", "chat", null, "EAM"); }
                catch (ServerException) { } // server stopped: players will see it with EV:list
                break;
            case "start":
            {
                if (ev.State is not ("open" or "draft")) throw new ServerException("event_closed");
                data.SaveGameEvent(ev with { State = "running" });
                var kit = JsonSerializer.Deserialize<List<KitItem>>(ev.Kit, FeatureStore.Json) ?? [];
                foreach (var reg in data.GameEventPlayers(ev.Id).Where(x => x.State == "registered"))
                {
                    var p = _live.OnlineBySteam(id, reg.SteamId);
                    if (p is null) continue;
                    try
                    {
                        string? saved = null;
                        if (!ev.KeepInventory)
                        {
                            var inv = await _live.RequestAsync(id, new() { ["op"] = "inventory_get", ["id"] = p.Id }, TimeSpan.FromSeconds(15));
                            saved = JsonSerializer.Serialize(new { toolbelt = inv.GetProperty("toolbelt"), bag = inv.GetProperty("bag") });
                            data.SaveGameEventPlayer(reg with { State = "in", Saved = saved, BackPlayfield = p.Playfield, BackX = p.X, BackY = p.Y, BackZ = p.Z });
                            var bag = kit.Select((k, i) => new Dictionary<string, object?> { ["id"] = k.Id, ["count"] = k.Count, ["slot"] = i, ["ammo"] = 0, ["decay"] = 0 }).ToList();
                            await _live.RequestAsync(id, new() { ["op"] = "inventory_set", ["id"] = p.Id, ["toolbelt"] = new List<object>(), ["bag"] = bag });
                        }
                        else
                        {
                            data.SaveGameEventPlayer(reg with { State = "in", BackPlayfield = p.Playfield, BackX = p.X, BackY = p.Y, BackZ = p.Z });
                            foreach (var k in kit) await _live.RequestAsync(id, new() { ["op"] = "item_add", ["id"] = p.Id, ["item"] = k.Id, ["count"] = k.Count });
                        }
                        var req = new Dictionary<string, object?> { ["op"] = "teleport", ["id"] = p.Id, ["x"] = ev.X, ["y"] = ev.Y, ["z"] = ev.Z };
                        if (!string.Equals(p.Playfield, ev.Playfield, StringComparison.OrdinalIgnoreCase)) req["pf"] = ev.Playfield;
                        await _live.RequestAsync(id, req, TimeSpan.FromSeconds(60));
                    }
                    catch (ServerException e) { _log.LogWarning("Event {Ev}: {Name} not moved in: {Err}", ev.Name, p.Name, e.Code); }
                }
                await _alerts.RaiseAsync(def, "event", 0, $"{by}: event {ev.Name} started");
                break;
            }
            case "end":
            {
                data.SaveGameEvent(ev with { State = "ended" });
                foreach (var x in data.GameEventPlayers(ev.Id).Where(x => x.State == "in"))
                {
                    var p = _live.OnlineBySteam(id, x.SteamId);
                    if (p is null) { data.SaveGameEventPlayer(x with { State = "pending_return" }); continue; }
                    await BringBackAsync(def, x, p);
                }
                await _alerts.RaiseAsync(def, "event", 0, $"{by}: event {ev.Name} ended");
                break;
            }
            case "delete":
                if (ev.State == "running") throw new ServerException("event_running");
                if (data.GameEventPlayers(ev.Id).Any(x => x.State is "in" or "pending_return")) throw new ServerException("event_running");
                data.DeleteGameEvent(ev.Id);
                break;
            default:
                throw new ServerException("unknown_action");
        }
    }

    /// <summary>Gives the player's inventory back and sends them where they were before the event.</summary>
    private async Task BringBackAsync(ServerDefinition def, GameEventPlayer x, LivePlayer p)
    {
        var data = _live.Data(def);
        try
        {
            if (x.Saved is { } saved)
            {
                var inv = JsonDocument.Parse(saved).RootElement;
                await _live.RequestAsync(def.Id, new() { ["op"] = "inventory_set", ["id"] = p.Id, ["toolbelt"] = inv.GetProperty("toolbelt"), ["bag"] = inv.GetProperty("bag") });
            }
            if (x.BackPlayfield is { Length: > 0 } pf)
            {
                var req = new Dictionary<string, object?> { ["op"] = "teleport", ["id"] = p.Id, ["x"] = x.BackX, ["y"] = x.BackY, ["z"] = x.BackZ };
                if (!string.Equals(p.Playfield, pf, StringComparison.OrdinalIgnoreCase)) req["pf"] = pf;
                await _live.RequestAsync(def.Id, req, TimeSpan.FromSeconds(60));
            }
            data.SaveGameEventPlayer(x with { State = "back" });
        }
        catch (ServerException e)
        {
            data.SaveGameEventPlayer(x with { State = "pending_return" });
            await _alerts.RaiseAsync(def, "event", 1, $"could not bring {p.Name} back: {e.Code}", x.SteamId, p.Name);
        }
    }

    private async Task OnJoinAsync(ServerDefinition def, LivePlayer p)
    {
        if (p.SteamId is not { } steam) return;
        var pending = _live.Data(def).GameEventPlayers(null, steam).FirstOrDefault(x => x.State == "pending_return");
        if (pending is null) return;
        await Task.Delay(TimeSpan.FromSeconds(30));
        await BringBackAsync(def, pending, p);
    }
}
