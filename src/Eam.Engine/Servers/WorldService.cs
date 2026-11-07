using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Eam.Core.Servers;
using Eam.Engine.Api;

namespace Eam.Engine.Servers;

public sealed record StructureActionRequest(
    string Action, string? Name, string? Playfield, double? X, double? Y, double? Z, string? ToPlayer,
    string? Faction, bool? SaveFirst, string? Note);

public sealed record SpawnRequest(string Source, string Name, string Playfield, double X, double Y, double Z, int? Type,
    int? FactionGroup, int? FactionId, string? NewName);

/// <summary>
/// Step 6: structures (list, actions, library, blueprints, limits and automatic wipes), playfields, maps and galaxy.
/// The structure list is read from the game every few minutes and kept in the server's database,
/// which also gives each structure its history.
/// </summary>
public sealed class WorldService : BackgroundService
{
    private readonly ServerManager _manager;
    private readonly LiveService _live;
    private readonly LiveHub _hub;
    private readonly ILogger<WorldService> _log;
    private readonly ConcurrentDictionary<long, DateTime> _lastRefresh = new();
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<(long, string), DateTime> _reservedHandled = new();

    public WorldService(ServerManager manager, LiveService live, LiveHub hub, ILogger<WorldService> log)
    {
        _manager = manager;
        _live = live;
        _hub = hub;
        _log = log;
        _live.ModEvent += OnModEventAsync;
    }

    public WorldSettings Settings(ServerDefinition def) => FeatureStore.Load<WorldSettings>(_live.Data(def), "world");

    public DateTime? LastRefresh(long id) => _lastRefresh.TryGetValue(id, out var d) ? d : null;

    // ------------------------------------------------------------------ background refresh

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stop))
        {
            foreach (var def in _manager.Definitions())
            {
                if (!_live.IsLinked(def.Id)) continue;
                try
                {
                    var s = Settings(def);
                    if (_lastRefresh.TryGetValue(def.Id, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(s.RefreshMinutes)) continue;
                    await RefreshAsync(def.Id);
                    await EnforceAsync(def, s);
                }
                catch (Exception e) when (e is not OperationCanceledException) { _log.LogDebug("Structure refresh of '{Name}' failed: {Err}", def.Name, e.Message); }
            }
        }
    }

    /// <summary>Reads every structure from the game and records what changed.</summary>
    public async Task<int> RefreshAsync(long id)
    {
        var gate = _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(1))) return -1; // already running
        try
        {
            var def = _manager.Definition(id);
            var answer = await _live.RequestAsync(id, new() { ["op"] = "structures" }, TimeSpan.FromSeconds(90));
            var now = DateTime.UtcNow;
            var list = new List<StructureRow>();
            if (answer.TryGetProperty("list", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var s in arr.EnumerateArray())
                {
                    var visited = (long)LiveService.Num(s, "visited");
                    DateTime? lv = null;
                    try { if (visited > 0) lv = DateTime.FromBinary(visited).ToUniversalTime(); } catch { }
                    if (lv is { Year: < 2000 }) lv = null;
                    list.Add(new StructureRow(LiveService.Int(s, "id"), LiveService.Str(s, "name") ?? "", LiveService.Int(s, "type"), LiveService.Str(s, "pf") ?? "",
                        LiveService.Int(s, "fg"), LiveService.Int(s, "fid"), LiveService.Num(s, "x"), LiveService.Num(s, "y"), LiveService.Num(s, "z"),
                        LiveService.Int(s, "blocks"), LiveService.Int(s, "devices"), LiveService.Int(s, "class"), LiveService.Int(s, "core"),
                        LiveService.Str(s, "powered") == "true", lv, LiveService.Str(s, "ss"), now, now, null, null));
                }
            var data = _live.Data(def);
            var events = data.SyncStructures(list, now);
            _lastRefresh[id] = now;
            foreach (var e in events.Where(e => e.Kind is "destroyed" or "owner"))
                data.AddJournal("structure", e.Kind == "destroyed" ? 1 : 0, $"{e.StructureId}: {e.Kind} {e.Old} → {e.New}");
            await _hub.BroadcastAsync(new { type = "structures", id, count = list.Count, changes = events.Count }, CancellationToken.None);
            return list.Count;
        }
        finally { gate.Release(); }
    }

    // ------------------------------------------------------------------ list and details

    public object List(long id)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var owners = OwnerNames(def);
        object Row(StructureRow s) => new
        {
            s.Id, s.Name, type = EntityTypes.Name(s.Type), s.Playfield, s.FactionGroup, s.FactionId, owner = owners(s.FactionGroup, s.FactionId),
            x = Math.Round(s.X), y = Math.Round(s.Y), z = Math.Round(s.Z), s.Blocks, s.Devices, s.Class, s.Core, s.Powered,
            s.LastVisitedUtc, s.SolarSystem, s.FirstSeenUtc, s.LastSeenUtc, s.RemovedUtc, s.WarnedUtc,
        };
        return new
        {
            lastRefreshUtc = LastRefresh(id),
            linked = _live.IsLinked(id),
            list = data.Structures().Where(s => EntityTypes.IsStructure(s.Type)).Select(Row).ToList(),
            removed = data.RemovedStructures(DateTime.UtcNow.AddDays(-2)).Select(Row).ToList(),
            factions = _live.Factions(id).Select(kv => new { id = kv.Key, abbrev = kv.Value }).ToList(),
        };
    }

    public async Task<object> DetailAsync(long id, int structureId)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var s = data.Structure(structureId) ?? throw new ServerException("structure_not_found");
        object? blocks = null;
        string? blockError = null;
        if (_live.IsLinked(id) && s.RemovedUtc is null)
        {
            try
            {
                var answer = await _live.RequestAsync(id, new() { ["op"] = "block_stats", ["id"] = structureId }, TimeSpan.FromSeconds(30));
                if (answer.TryGetProperty("list", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var names = ItemNames(def);
                    blocks = arr.EnumerateArray().Select(b => new { id = LiveService.Int(b, "id"), count = LiveService.Int(b, "count"), name = names.GetValueOrDefault(LiveService.Int(b, "id")) })
                        .OrderByDescending(b => b.count).ToList();
                }
            }
            catch (ServerException e) { blockError = e.Code; }
        }
        return new
        {
            structure = s, type = EntityTypes.Name(s.Type), owner = OwnerNames(def)(s.FactionGroup, s.FactionId),
            history = data.StructureEvents(structureId, 200), blocks, blockError,
        };
    }

    private Dictionary<int, string> ItemNames(ServerDefinition def)
    {
        string? scenario = null;
        try { scenario = File.Exists(def.YamlFile) ? DedicatedYaml.Load(def.YamlFile).Get("GameConfig", "CustomScenario") : null; } catch { }
        try { return ItemCatalog.Load(def, scenario, "fr").GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First().Label); }
        catch { return []; }
    }

    /// <summary>Name of the owner of a structure: player, player faction or game faction.</summary>
    public Func<int, int, string> OwnerNames(ServerDefinition def)
    {
        var players = _live.Data(def).Players().GroupBy(p => p.EntityId).ToDictionary(g => g.Key, g => g.First().Name);
        var factions = _live.Factions(def.Id);
        return (group, fid) => group switch
        {
            FactionGroups.Player => players.TryGetValue(fid, out var n) ? n : $"#{fid}",
            FactionGroups.Faction => factions.TryGetValue(fid, out var f) ? f : $"#{fid}",
            FactionGroups.Admin => "Admin",
            FactionGroups.Zirax => "Zirax",
            FactionGroups.Alien => "Alien",
            FactionGroups.Talon => "Talon",
            FactionGroups.Polaris => "Polaris",
            FactionGroups.Predator or FactionGroups.Prey => "Wildlife",
            FactionGroups.NoFaction => "—",
            _ => $"group {group}",
        };
    }

    // ------------------------------------------------------------------ actions

    public async Task<object?> ActAsync(long id, int structureId, StructureActionRequest r, string by)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var s = data.Structure(structureId) ?? throw new ServerException("structure_not_found");
        string detail;
        object? result = null;
        switch (r.Action)
        {
            case "touch":
                await _live.RequestAsync(id, new() { ["op"] = "touch", ["id"] = structureId });
                detail = "";
                break;
            case "destroy":
                if (r.SaveFirst != false) await ExportAsync(def, s, by, "before removal");
                await _live.RequestAsync(id, new() { ["op"] = "destroy", ["id"] = structureId }, TimeSpan.FromSeconds(30));
                detail = r.SaveFirst != false ? "(saved in the library)" : "";
                break;
            case "rename":
            {
                var name = (r.Name ?? "").Trim();
                if (name.Length is 0 or > 60) throw new ServerException("invalid_name");
                await _live.RequestAsync(id, new() { ["op"] = "rename", ["id"] = structureId, ["pf"] = s.Playfield, ["name"] = name });
                detail = $"{s.Name} → {name}";
                break;
            }
            case "move":
            {
                var req = new Dictionary<string, object?> { ["op"] = "entity_move", ["id"] = structureId };
                string pf;
                if (!string.IsNullOrEmpty(r.ToPlayer))
                {
                    var p = _live.OnlineBySteam(id, r.ToPlayer) ?? throw new ServerException("player_offline");
                    pf = p.Playfield ?? s.Playfield;
                    req["x"] = p.X + 30; req["y"] = p.Y + 20; req["z"] = p.Z + 30;
                }
                else
                {
                    pf = string.IsNullOrWhiteSpace(r.Playfield) ? s.Playfield : r.Playfield.Trim();
                    req["x"] = r.X ?? s.X; req["y"] = r.Y ?? s.Y; req["z"] = r.Z ?? s.Z;
                }
                if (!string.Equals(pf, s.Playfield, StringComparison.OrdinalIgnoreCase)) req["pf"] = pf;
                await _live.RequestAsync(id, req, TimeSpan.FromSeconds(60));
                detail = $"{s.Playfield} → {pf} ({req["x"]:0}, {req["y"]:0}, {req["z"]:0})";
                break;
            }
            case "faction":
            {
                var abbrev = (r.Faction ?? "").Trim();
                if (abbrev.Length is 0 or > 12 || abbrev.Contains('\'')) throw new ServerException("invalid_faction");
                await _manager.ConsoleAsync(id, $"faction entity '{abbrev}' {structureId}", by);
                detail = abbrev;
                break;
            }
            case "export":
            {
                var file = await ExportAsync(def, s, by, string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim());
                detail = Path.GetFileName(file);
                result = new { name = Path.GetFileNameWithoutExtension(file) };
                break;
            }
            case "clone":
            {
                var file = await ExportAsync(def, s, by, "clone");
                var newId = await SpawnFromFileAsync(id, file, string.IsNullOrWhiteSpace(r.Playfield) ? s.Playfield : r.Playfield.Trim(),
                    r.X ?? s.X + 60, r.Y ?? s.Y + 20, r.Z ?? s.Z + 60, s.Type, s.FactionGroup, s.FactionId, string.IsNullOrWhiteSpace(r.Name) ? s.Name + " (copy)" : r.Name.Trim());
                detail = $"→ {newId}";
                result = new { id = newId };
                break;
            }
            default:
                throw new ServerException("unknown_action");
        }
        data.StructureEventBy(structureId, "admin_" + r.Action, null, detail, by);
        ServerManager.ServerLog(def, $"{by}: structure {r.Action} on {s.Name} ({structureId}) {detail}");
        _lastRefresh.TryRemove(id, out _); // read the list again soon
        return result;
    }

    /// <summary>Asks the game to export a structure to EAM's library, and waits for the file.</summary>
    public async Task<string> ExportAsync(ServerDefinition def, StructureRow s, string by, string? note)
    {
        var file = StructureLibrary.NewFile(def, s.Id);
        await _live.RequestAsync(def.Id, new() { ["op"] = "export", ["id"] = s.Id, ["file"] = file, ["pf"] = s.Playfield, ["unload"] = "false" }, TimeSpan.FromSeconds(60));
        for (var i = 0; i < 20 && !File.Exists(file); i++) await Task.Delay(500);
        if (!File.Exists(file)) throw new ServerException("export_failed");
        StructureLibrary.Describe(file, s, by, note);
        return file;
    }

    private async Task<int> SpawnFromFileAsync(long id, string datFile, string pf, double x, double y, double z, int type, int fg, int fid, string name)
    {
        var answer = await _live.RequestAsync(id, new() { ["op"] = "new_id" });
        var newId = LiveService.Int(answer, "id");
        if (newId <= 0) throw new ServerException("game_error", "new_id");
        await _live.RequestAsync(id, new()
        {
            ["op"] = "spawn", ["newId"] = newId, ["pf"] = pf, ["x"] = x, ["y"] = y, ["z"] = z, ["name"] = name, ["type"] = type,
            ["factionGroup"] = fg, ["factionId"] = fid, ["dat"] = datFile, ["prefab"] = "", ["prefabDir"] = "",
        }, TimeSpan.FromSeconds(60));
        return newId;
    }

    /// <summary>Spawns a structure from the library ("library") or from an uploaded blueprint ("blueprint").</summary>
    public async Task<int> SpawnAsync(long id, SpawnRequest r, string by)
    {
        var def = _manager.Definition(id);
        var pf = (r.Playfield ?? "").Trim();
        if (pf.Length == 0) throw new ServerException("invalid_playfield");
        int newId;
        if (r.Source == "library")
        {
            string file;
            try { file = StructureLibrary.FileOf(def, r.Name); }
            catch (FormatException) { throw new ServerException("invalid_name"); }
            catch (FileNotFoundException) { throw new ServerException("library_not_found"); }
            var meta = StructureLibrary.List(def).FirstOrDefault(e => e.Name == r.Name);
            newId = await SpawnFromFileAsync(id, file, pf, r.X, r.Y, r.Z, r.Type ?? meta?.Type ?? EntityTypes.CV,
                r.FactionGroup ?? meta?.FactionGroup ?? FactionGroups.Admin, r.FactionId ?? meta?.FactionId ?? 0, r.NewName?.Trim() is { Length: > 0 } n ? n : meta?.StructureName ?? r.Name);
        }
        else if (r.Source == "blueprint")
        {
            var name = BlueprintFiles.SafeName(r.Name);
            var dir = BlueprintFiles.Dir(def);
            if (!File.Exists(Path.Combine(dir, name + ".epb"))) throw new ServerException("blueprint_not_found");
            var answer = await _live.RequestAsync(id, new() { ["op"] = "new_id" });
            newId = LiveService.Int(answer, "id");
            await _live.RequestAsync(id, new()
            {
                ["op"] = "spawn", ["newId"] = newId, ["pf"] = pf, ["x"] = r.X, ["y"] = r.Y, ["z"] = r.Z,
                ["name"] = r.NewName?.Trim() is { Length: > 0 } n2 ? n2 : name, ["type"] = r.Type ?? EntityTypes.CV,
                ["factionGroup"] = r.FactionGroup ?? FactionGroups.Admin, ["factionId"] = r.FactionId ?? 0, ["prefab"] = name, ["prefabDir"] = dir,
            }, TimeSpan.FromSeconds(60));
        }
        else throw new ServerException("invalid_source");
        _live.Data(def).StructureEventBy(newId, "admin_spawn", null, $"{r.Source} {r.Name} @ {pf}", by);
        ServerManager.ServerLog(def, $"{by}: spawned {r.Source} {r.Name} as {newId} on {pf}");
        _lastRefresh.TryRemove(id, out _);
        return newId;
    }

    public async Task SaveBlueprintAsync(long id, IFormFile file, string by)
    {
        var def = _manager.Definition(id);
        if (file.Length is 0 or > 50 * 1024 * 1024) throw new ServerException("invalid_file");
        if (!file.FileName.EndsWith(".epb", StringComparison.OrdinalIgnoreCase)) throw new ServerException("invalid_file");
        string name;
        try { name = BlueprintFiles.SafeName(file.FileName); } catch (FormatException) { throw new ServerException("invalid_name"); }
        Directory.CreateDirectory(BlueprintFiles.Dir(def));
        var target = Path.Combine(BlueprintFiles.Dir(def), name + ".epb");
        var tmp = target + ".tmp";
        await using (var fs = File.Create(tmp)) await file.CopyToAsync(fs);
        File.Move(tmp, target, overwrite: true);
        ServerManager.ServerLog(def, $"{by}: blueprint {name}.epb added");
    }

    public void DeleteBlueprint(long id, string name)
    {
        var def = _manager.Definition(id);
        string safe;
        try { safe = BlueprintFiles.SafeName(name); } catch (FormatException) { throw new ServerException("invalid_name"); }
        var file = Path.Combine(BlueprintFiles.Dir(def), safe + ".epb");
        if (File.Exists(file)) File.Delete(file);
    }

    public object Library(long id)
    {
        var def = _manager.Definition(id);
        return new { structures = StructureLibrary.List(def), blueprints = BlueprintFiles.List(def) };
    }

    public void DeleteLibrary(long id, string name)
    {
        try { StructureLibrary.Delete(_manager.Definition(id), name); }
        catch (FormatException) { throw new ServerException("invalid_name"); }
        catch (FileNotFoundException) { throw new ServerException("library_not_found"); }
    }

    // ------------------------------------------------------------------ playfields, maps, galaxy

    public object Playfields(long id)
    {
        var def = _manager.Definition(id);
        var snapshot = _live.Live(id);
        Dictionary<string, LivePlayfield> loaded;
        lock (snapshot.Gate) loaded = snapshot.Playfields.Values.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
        var structures = _live.Data(def).Structures().Where(s => EntityTypes.IsStructure(s.Type)).GroupBy(s => s.Playfield, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), System: g.First().SolarSystem), StringComparer.OrdinalIgnoreCase);
        var players = _live.OnlinePlayers(id).GroupBy(p => p.Playfield ?? "").ToDictionary(g => g.Key, g => g.Select(p => p.Name).ToList(), StringComparer.OrdinalIgnoreCase);
        var names = loaded.Keys.Concat(structures.Keys).Concat(players.Keys).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        return names.Select(n => new
        {
            name = n,
            loaded = loaded.TryGetValue(n, out var l) ? new { l.ProcessId, fps = Math.Round(l.Fps, 1), l.MemoryMb, l.Players, l.Structures, l.Mobs, l.UptimeSeconds } : null,
            structures = structures.TryGetValue(n, out var st) ? st.Count : 0,
            system = structures.TryGetValue(n, out var st2) ? st2.System : null,
            players = players.GetValueOrDefault(n) ?? [],
        }).ToList();
    }

    public async Task PlayfieldActionAsync(long id, string playfield, string action, string by)
    {
        var def = _manager.Definition(id);
        playfield = (playfield ?? "").Trim();
        if (playfield.Length is 0 or > 100 || playfield.Contains('\'')) throw new ServerException("invalid_playfield");
        switch (action)
        {
            case "load":
                await _live.RequestAsync(id, new() { ["op"] = "pf_load", ["pf"] = playfield, ["secs"] = 120 }, TimeSpan.FromSeconds(30));
                break;
            case "wipe_poi":
            case "wipe_deposit":
            case "wipe_terrain":
            case "wipe_all":
                await _manager.ConsoleAsync(id, $"wipe '{playfield}' {action[5..]}", by);
                break;
            default:
                throw new ServerException("unknown_action");
        }
        _live.Data(def).AddJournal("playfield", 1, $"{by}: {action} {playfield}", null, by, playfield);
    }

    /// <summary>Everything to draw a playfield: structures, online players and the other entities the game reports.</summary>
    public async Task<object> MapAsync(long id, string playfield)
    {
        var def = _manager.Definition(id);
        var owners = OwnerNames(def);
        var structures = _live.Data(def).Structures().Where(s => string.Equals(s.Playfield, playfield, StringComparison.OrdinalIgnoreCase)).Select(s => new
        {
            s.Id, s.Name, type = EntityTypes.Name(s.Type), x = Math.Round(s.X), y = Math.Round(s.Y), z = Math.Round(s.Z), s.Blocks, s.Class,
            owner = owners(s.FactionGroup, s.FactionId), s.FactionGroup, s.FactionId,
        }).ToList();
        var players = _live.OnlinePlayers(id).Where(p => string.Equals(p.Playfield, playfield, StringComparison.OrdinalIgnoreCase))
            .Select(p => new { p.Name, p.SteamId, x = Math.Round(p.X), y = Math.Round(p.Y), z = Math.Round(p.Z), p.FactionId }).ToList();
        object? entities = null;
        if (_live.IsLinked(id))
        {
            try
            {
                var answer = await _live.RequestAsync(id, new() { ["op"] = "pf_entities", ["pf"] = playfield }, TimeSpan.FromSeconds(15));
                if (answer.TryGetProperty("list", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    entities = arr.EnumerateArray().Select(e => new { id = LiveService.Int(e, "id"), type = LiveService.Int(e, "type"), x = Math.Round(LiveService.Num(e, "x")), y = Math.Round(LiveService.Num(e, "y")), z = Math.Round(LiveService.Num(e, "z")) })
                        .Where(e => !structures.Any(s => s.Id == e.id)).Take(3000).ToList();
            }
            catch (ServerException) { }
        }
        return new { playfield, structures, players, entities };
    }

    public object Galaxy(long id)
    {
        var def = _manager.Definition(id);
        var gameName = SaveName(def);
        IReadOnlyList<GalaxySystem> systems = [];
        string? error = null;
        try { systems = GalaxyReader.Read(GalaxyReader.GlobalDb(def, gameName)); }
        catch (Exception e) { error = e.Message; }
        // Systems where structures are, as reported by the game (always available while the server runs).
        var fromStructures = _live.Data(def).Structures().Where(s => !string.IsNullOrEmpty(s.SolarSystem))
            .GroupBy(s => s.SolarSystem!).Select(g => new { name = g.Key, playfields = g.Select(s => s.Playfield).Distinct().ToList(), structures = g.Count() }).ToList();
        var players = _live.OnlinePlayers(id).Select(p => new { p.Name, p.Playfield }).ToList();
        return new { systems, fromStructures, players, error };
    }

    public static string SaveName(ServerDefinition def)
    {
        try { return File.Exists(def.YamlFile) ? DedicatedYaml.Load(def.YamlFile).Get("GameConfig", "GameName") ?? "DediGame" : "DediGame"; }
        catch { return "DediGame"; }
    }

    // ------------------------------------------------------------------ Shared folder cleanup

    private IReadOnlyList<SharedFolder> ObsoleteShared(ServerDefinition def)
    {
        if (LastRefresh(def.Id) is not { } last || DateTime.UtcNow - last > TimeSpan.FromMinutes(15)) throw new ServerException("structures_not_fresh");
        var alive = _live.Data(def).Structures().Select(s => s.Id).ToHashSet();
        if (alive.Count == 0) throw new ServerException("structures_not_fresh");
        return SharedCleanup.Obsolete(SharedCleanup.SharedDir(def, SaveName(def)), alive, TimeSpan.FromHours(Settings(def).SharedMinAgeHours));
    }

    public object SharedPreview(long id)
    {
        var list = ObsoleteShared(_manager.Definition(id));
        return new { count = list.Count, bytes = list.Sum(f => f.Size), list = list.Take(500) };
    }

    public object SharedClean(long id, string by)
    {
        var def = _manager.Definition(id);
        var list = ObsoleteShared(def);
        var (count, bytes, target) = SharedCleanup.MoveAside(def, SharedCleanup.SharedDir(def, SaveName(def)), list);
        _live.Data(def).AddJournal("maintenance", 1, $"{by}: {count} obsolete Shared folders moved to {target}", null, by);
        return new { count, bytes, target };
    }

    // ------------------------------------------------------------------ limits, automatic wipes

    private Func<int, int, bool> Exempt(ServerDefinition def, WorldSettings s)
    {
        var data = _live.Data(def);
        var admins = s.ExemptAdmins
            ? AdminConfig.ReadElevated(AdminConfig.FileOf(def)).Where(e => e.Permission >= 3).Select(e => e.SteamId).ToHashSet()
            : [];
        var adminEntities = data.Players().Where(p => admins.Contains(p.SteamId)).Select(p => p.EntityId).ToHashSet();
        var factions = s.ExemptFactions.ToHashSet();
        return (group, fid) => !FactionGroups.IsPlayerOwned(group, fid)
                               || group == FactionGroups.Player && adminEntities.Contains(fid)
                               || group == FactionGroups.Faction && factions.Contains(fid);
    }

    public object Violations(long id)
    {
        var def = _manager.Definition(id);
        var s = Settings(def);
        var data = _live.Data(def);
        var list = LimitChecker.Check(data.Structures(), s.Limits, OwnerNames(def), Exempt(def, s));
        var warned = data.Structures().ToDictionary(x => x.Id, x => x.WarnedUtc);
        var wipes = s.AutoWipe.Count > 0
            ? LimitChecker.AutoWipe(data.Structures(), s.AutoWipe, DateTime.UtcNow, Exempt(def, s)).Select(w => new { w.Structure.Id, w.Structure.Name, w.Structure.Playfield, type = EntityTypes.Name(w.Structure.Type), w.Reason }).ToList<object>()
            : [];
        return new
        {
            limits = list.Select(v => new { v.StructureId, v.StructureName, v.Playfield, type = EntityTypes.Name(v.Type), v.Owner, v.Rule, v.Detail, warnedUtc = warned.GetValueOrDefault(v.StructureId) }),
            autoWipe = wipes,
        };
    }

    private async Task EnforceAsync(ServerDefinition def, WorldSettings s)
    {
        var data = _live.Data(def);
        var structures = data.Structures();
        var exempt = Exempt(def, s);
        if (s.LimitsEnabled && s.Limits.Count > 0)
        {
            var violations = LimitChecker.Check(structures, s.Limits, OwnerNames(def), exempt);
            var byId = structures.ToDictionary(x => x.Id);
            var over = violations.Select(v => v.StructureId).ToHashSet();
            // Structures back within the limits lose their warning.
            foreach (var st in structures.Where(x => x.WarnedUtc is not null && !over.Contains(x.Id))) data.SetStructureWarned(st.Id, null);
            foreach (var v in violations.GroupBy(v => v.StructureId).Select(g => g.First()))
            {
                if (!byId.TryGetValue(v.StructureId, out var st)) continue;
                if (st.WarnedUtc is null)
                {
                    data.SetStructureWarned(st.Id, DateTime.UtcNow);
                    data.AddJournal("limit", 1, $"{v.StructureName} ({v.StructureId}, {v.Owner}): {v.Detail} [{v.Rule}]", null, v.Owner, v.Playfield);
                    await WarnOwnerAsync(def, st, $"EAM: « {st.Name} » ({st.Id}) dépasse une limite du serveur ({v.Detail}). Délai : {s.LimitGraceHours} h.");
                }
                else if (s.LimitAction == "destroy" && DateTime.UtcNow - st.WarnedUtc > TimeSpan.FromHours(s.LimitGraceHours))
                {
                    try
                    {
                        await ExportAsync(def, st, "EAM", "limit: " + v.Detail);
                        await _live.RequestAsync(def.Id, new() { ["op"] = "destroy", ["id"] = st.Id }, TimeSpan.FromSeconds(30));
                        data.StructureEventBy(st.Id, "limit_removed", null, v.Detail, "EAM");
                        data.AddJournal("limit", 2, $"{st.Name} ({st.Id}, {v.Owner}) removed: {v.Detail} (saved in the library)", null, v.Owner, st.Playfield);
                    }
                    catch (ServerException e) { _log.LogWarning("Limit removal of {Id} failed: {Err}", st.Id, e.Code); }
                }
            }
        }
        if (s.AutoWipeEnabled && s.AutoWipe.Count > 0)
        {
            foreach (var (st, reason) in LimitChecker.AutoWipe(structures, s.AutoWipe, DateTime.UtcNow, exempt).Take(20))
            {
                try
                {
                    await ExportAsync(def, st, "EAM", "auto wipe: " + reason);
                    await _live.RequestAsync(def.Id, new() { ["op"] = "destroy", ["id"] = st.Id }, TimeSpan.FromSeconds(30));
                    data.StructureEventBy(st.Id, "auto_wipe", null, reason, "EAM");
                    data.AddJournal("autowipe", 1, $"{st.Name} ({st.Id}) removed: {reason} (saved in the library)", null, null, st.Playfield);
                }
                catch (ServerException e) { _log.LogWarning("Auto wipe of {Id} failed: {Err}", st.Id, e.Code); }
            }
        }
    }

    private async Task WarnOwnerAsync(ServerDefinition def, StructureRow st, string text)
    {
        var targets = _live.OnlinePlayers(def.Id).Where(p => st.FactionGroup == FactionGroups.Player ? p.Id == st.FactionId : p.FactionId == st.FactionId).ToList();
        foreach (var p in targets)
            try { await _live.SendToModAsync(def.Id, new { op = "msg_player", id = p.Id, text, prio = 0, secs = 20 }); } catch { }
        if (targets.Count == 0 && st.FactionGroup == FactionGroups.Player)
        {
            var owner = _live.Data(def).Players().FirstOrDefault(p => p.EntityId == st.FactionId);
            if (owner is not null) _live.Data(def).AddOfflineMessage(owner.SteamId, text, "EAM");
        }
    }

    // ------------------------------------------------------------------ reserved playfields

    private async Task OnModEventAsync(ServerDefinition def, string ev, JsonElement m)
    {
        if (ev is not ("player_info" or "player_playfield")) return;
        var s = Settings(def);
        if (!s.ReservedEnabled || s.Reserved.Count == 0) return;
        var p = _live.Online(def.Id, LiveService.Int(m, "id"));
        if (p?.Playfield is not { } pf || p.SteamId is not { } steam) return;
        var rule = s.Reserved.FirstOrDefault(r => FeatureStore.MatchesPlayfield(r.Playfield, pf));
        if (rule is null) return;
        if (rule.SteamIds.Contains(steam) || rule.Factions.Contains(p.FactionId) || rule.Origins.Contains(p.Origin)) return;
        if (AdminConfig.ReadElevated(AdminConfig.FileOf(def)).Any(e => e.SteamId == steam && e.Permission >= 3)) return;
        // One action per player and playfield every 2 minutes (the game needs time to move them).
        var key = (def.Id, steam + "|" + pf);
        if (_reservedHandled.TryGetValue(key, out var at) && DateTime.UtcNow - at < TimeSpan.FromMinutes(2)) return;
        _reservedHandled[key] = DateTime.UtcNow;
        var data = _live.Data(def);
        try
        {
            await _live.SendToModAsync(def.Id, new { op = "msg_player", id = p.Id, text = $"EAM: « {pf} » est réservé. / \"{pf}\" is reserved.", prio = 0, secs = 10 });
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (rule.Action == "kick") await _manager.ConsoleAsync(def.Id, $"kick {p.Id} 'Reserved playfield'", "EAM");
            else
            {
                var req = new Dictionary<string, object?> { ["op"] = "teleport", ["id"] = p.Id, ["x"] = rule.X, ["y"] = rule.Y, ["z"] = rule.Z };
                if (!string.Equals(rule.TargetPlayfield, pf, StringComparison.OrdinalIgnoreCase)) req["pf"] = rule.TargetPlayfield;
                await _live.RequestAsync(def.Id, req, TimeSpan.FromSeconds(60));
            }
            data.AddJournal("reserved", 1, $"{p.Name} entered reserved playfield {pf}: {rule.Action}", steam, p.Name, pf);
        }
        catch (Exception e) { _log.LogWarning("Reserved playfield action failed: {Err}", e.Message); }
    }
}
