using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Eam.Core.Servers;

public sealed record LibraryEntry(string Name, int StructureId, string StructureName, int Type, string Playfield,
    double X, double Y, double Z, int FactionGroup, int FactionId, DateTime CreatedUtc, string By, string? Note, long Size);

/// <summary>
/// Structures exported by EAM (before a removal, or on demand): eam\structures\&lt;name&gt;.dat with a .json description.
/// They can be spawned again later, on the same server.
/// </summary>
public static class StructureLibrary
{
    public static string Dir(ServerDefinition def) => Path.Combine(def.EamDir, "structures");

    public static string NewFile(ServerDefinition def, int structureId)
    {
        Directory.CreateDirectory(Dir(def));
        return Path.Combine(Dir(def), $"{structureId}_{DateTime.UtcNow:yyyyMMdd-HHmmss}.dat");
    }

    public static void Describe(string datFile, StructureRow s, string by, string? note)
    {
        var meta = new LibraryEntry(Path.GetFileNameWithoutExtension(datFile), s.Id, s.Name, s.Type, s.Playfield, s.X, s.Y, s.Z,
            s.FactionGroup, s.FactionId, DateTime.UtcNow, by, note, 0);
        File.WriteAllText(Path.ChangeExtension(datFile, ".json"), JsonSerializer.Serialize(meta, FeatureStore.Json));
    }

    public static IReadOnlyList<LibraryEntry> List(ServerDefinition def)
    {
        var dir = Dir(def);
        if (!Directory.Exists(dir)) return [];
        var list = new List<LibraryEntry>();
        foreach (var json in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var e = JsonSerializer.Deserialize<LibraryEntry>(File.ReadAllText(json), FeatureStore.Json);
                var dat = Path.ChangeExtension(json, ".dat");
                if (e is null || !File.Exists(dat)) continue;
                list.Add(e with { Name = Path.GetFileNameWithoutExtension(json), Size = new FileInfo(dat).Length });
            }
            catch (JsonException) { }
        }
        return list.OrderByDescending(e => e.CreatedUtc).ToList();
    }

    public static string FileOf(ServerDefinition def, string name)
    {
        if (name.Length is 0 or > 80 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("..")) throw new FormatException("invalid_name");
        var file = Path.Combine(Dir(def), name + ".dat");
        if (!File.Exists(file)) throw new FileNotFoundException("library_not_found");
        return file;
    }

    public static void Delete(ServerDefinition def, string name)
    {
        var file = FileOf(def, name);
        File.Delete(file);
        File.Delete(Path.ChangeExtension(file, ".json"));
    }
}

/// <summary>Blueprint files (.epb) sent by an admin, kept in eam\blueprints to be spawned.</summary>
public static class BlueprintFiles
{
    public static string Dir(ServerDefinition def) => Path.Combine(def.EamDir, "blueprints");

    public static string SafeName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? "");
        name = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_').ToArray()).Trim();
        if (name.Length is 0 or > 60) throw new FormatException("invalid_name");
        return name;
    }

    /// <summary>
    /// Type written in the blueprint header (byte 8: 2 base, 3 capital vessel, 4 small vessel, 5 hover vessel), or 0 when unknown.
    /// </summary>
    public static int GuessType(byte[] head)
    {
        if (head.Length < 9) return 0;
        return head[8] is 2 or 3 or 4 or 5 ? head[8] : 0;
    }

    public static IReadOnlyList<object> List(ServerDefinition def)
    {
        var dir = Dir(def);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.epb").Select(f => new FileInfo(f)).OrderBy(f => f.Name)
            .Select(f =>
            {
                var head = new byte[16];
                using (var fs = f.OpenRead()) _ = fs.Read(head, 0, head.Length);
                return (object)new { name = Path.GetFileNameWithoutExtension(f.Name), size = f.Length, type = GuessType(head), modifiedUtc = f.LastWriteTimeUtc };
            }).ToList();
    }
}

public sealed record SharedFolder(string Name, long Size, DateTime ModifiedUtc);

/// <summary>
/// The save's Shared folder keeps one folder per structure (named by its number). The game does not always remove
/// the folder of a structure that no longer exists: EAM lists them and moves them aside (never deleted directly).
/// </summary>
public static class SharedCleanup
{
    public static string SharedDir(ServerDefinition def, string gameName) => Path.Combine(def.GameDir, "Saves", "Games", gameName, "Shared");

    public static IReadOnlyList<SharedFolder> Obsolete(string sharedDir, ISet<int> alive, TimeSpan minAge)
    {
        if (!Directory.Exists(sharedDir)) return [];
        var limit = DateTime.UtcNow - minAge;
        var list = new List<SharedFolder>();
        foreach (var dir in Directory.EnumerateDirectories(sharedDir))
        {
            var name = Path.GetFileName(dir);
            if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || alive.Contains(id)) continue;
            var info = new DirectoryInfo(dir);
            var files = info.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            var modified = files.Count > 0 ? files.Max(f => f.LastWriteTimeUtc) : info.LastWriteTimeUtc;
            if (modified > limit) continue;
            list.Add(new SharedFolder(name, files.Sum(f => f.Length), modified));
        }
        return list.OrderBy(f => f.ModifiedUtc).ToList();
    }

    /// <summary>Moves the folders to eam\shared-removed\&lt;date&gt; (put them back by hand if needed).</summary>
    public static (int Count, long Bytes, string Target) MoveAside(ServerDefinition def, string sharedDir, IEnumerable<SharedFolder> folders)
    {
        var target = Path.Combine(def.EamDir, "shared-removed", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(target);
        var count = 0;
        long bytes = 0;
        foreach (var f in folders)
        {
            var src = Path.Combine(sharedDir, f.Name);
            if (!Directory.Exists(src)) continue;
            Directory.Move(src, Path.Combine(target, f.Name));
            count++;
            bytes += f.Size;
        }
        return (count, bytes, target);
    }
}

public sealed record LimitViolation(int StructureId, string StructureName, string Playfield, int Type, string Owner, string OwnerKey, string Rule, string Detail);

/// <summary>Structure limits per player or faction (pure logic, no game access).</summary>
public static class LimitChecker
{
    /// <param name="ownerName">Name of a player (factionGroup 1) or faction (0) from its id.</param>
    /// <param name="exempt">True for owners that are never limited (admins, protected factions).</param>
    public static IReadOnlyList<LimitViolation> Check(IReadOnlyList<StructureRow> structures, IEnumerable<LimitRule> rules,
        Func<int, int, string> ownerName, Func<int, int, bool> exempt)
    {
        var result = new List<LimitViolation>();
        var owned = structures.Where(s => FactionGroups.IsPlayerOwned(s.FactionGroup, s.FactionId) && !exempt(s.FactionGroup, s.FactionId)).ToList();
        foreach (var rule in rules)
        {
            var type = rule.Type == "any" ? 0 : EntityTypes.Parse(rule.Type);
            var scoped = owned.Where(s => (type == 0 ? EntityTypes.IsStructure(s.Type) : s.Type == type) && FeatureStore.MatchesPlayfield(rule.Playfields, s.Playfield));
            var desc = $"{rule.Scope} {rule.Type}{(rule.Playfields.Length > 0 ? " @ " + rule.Playfields : "")}";
            // Per player: personal structures (group 1). Per faction: faction structures (group 0).
            var groups = scoped.Where(s => rule.Scope == "player" ? s.FactionGroup == FactionGroups.Player : s.FactionGroup == FactionGroups.Faction)
                .GroupBy(s => s.FactionId);
            foreach (var g in groups)
            {
                var name = ownerName(rule.Scope == "player" ? FactionGroups.Player : FactionGroups.Faction, g.Key);
                var key = $"{(rule.Scope == "player" ? 1 : 0)}:{g.Key}";
                foreach (var s in g)
                {
                    if (rule.MaxClass > 0 && s.Class > rule.MaxClass)
                        result.Add(new LimitViolation(s.Id, s.Name, s.Playfield, s.Type, name, key, desc, $"class {s.Class} > {rule.MaxClass}"));
                    if (rule.MaxBlocks > 0 && s.Blocks > rule.MaxBlocks)
                        result.Add(new LimitViolation(s.Id, s.Name, s.Playfield, s.Type, name, key, desc, $"blocks {s.Blocks} > {rule.MaxBlocks}"));
                }
                if (rule.MaxCount > 0 && g.Count() > rule.MaxCount)
                {
                    // The newest structures (highest numbers) are the ones over the limit.
                    foreach (var s in g.OrderBy(s => s.Id).Skip(rule.MaxCount))
                        result.Add(new LimitViolation(s.Id, s.Name, s.Playfield, s.Type, name, key, desc, $"count {g.Count()} > {rule.MaxCount}"));
                }
            }
        }
        return result.GroupBy(v => (v.StructureId, v.Rule, v.Detail)).Select(g => g.First()).ToList();
    }

    /// <summary>Structures matching an automatic wipe rule.</summary>
    public static IReadOnlyList<(StructureRow Structure, string Reason)> AutoWipe(IReadOnlyList<StructureRow> structures, IEnumerable<AutoWipeRule> rules,
        DateTime nowUtc, Func<int, int, bool> exempt)
    {
        var result = new List<(StructureRow, string)>();
        foreach (var s in structures)
        {
            if (!EntityTypes.IsStructure(s.Type) || exempt(s.FactionGroup, s.FactionId)) continue;
            foreach (var r in rules)
            {
                var type = r.Type == "any" ? 0 : EntityTypes.Parse(r.Type);
                if (type != 0 && s.Type != type) continue;
                if (!FeatureStore.MatchesPlayfield(r.Playfields, s.Playfield)) continue;
                if (r.NotVisitedDays > 0 && s.LastVisitedUtc is { } lv && lv.Year > 2000 && nowUtc - lv > TimeSpan.FromDays(r.NotVisitedDays))
                { result.Add((s, $"not visited for {(int)(nowUtc - lv).TotalDays} days")); break; }
                if (r.OlderThanHours > 0 && nowUtc - s.FirstSeenUtc > TimeSpan.FromHours(r.OlderThanHours))
                { result.Add((s, $"older than {r.OlderThanHours} h")); break; }
            }
        }
        return result;
    }
}

public sealed record GalaxySystem(string Name, double X, double Y, double Z, string? StarClass, List<GalaxyPlayfield> Playfields);
public sealed record GalaxyPlayfield(string Name, string? Type);

/// <summary>
/// Reads the galaxy (solar systems and their playfields) from the save's global.db, read-only.
/// The schema is not documented: tables and columns are found by name, and nothing is shown when they are missing.
/// </summary>
public static class GalaxyReader
{
    public static string GlobalDb(ServerDefinition def, string gameName) => Path.Combine(def.GameDir, "Saves", "Games", gameName, "global.db");

    public static IReadOnlyList<GalaxySystem> Read(string file)
    {
        if (!File.Exists(file)) return [];
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        using (var busy = c.CreateCommand()) { busy.CommandText = "PRAGMA busy_timeout=3000;"; busy.ExecuteNonQuery(); }
        var tables = Names(c, "SELECT name FROM sqlite_master WHERE type='table';");
        var ssTable = tables.FirstOrDefault(t => t.Equals("SolarSystems", StringComparison.OrdinalIgnoreCase));
        if (ssTable is null) return [];
        var ssCols = Columns(c, ssTable);
        var ssId = Pick(ssCols, "ssid", "id", "solarsystemid");
        var ssName = Pick(ssCols, "name");
        var sx = Pick(ssCols, "sectorx", "x");
        var sy = Pick(ssCols, "sectory", "y");
        var sz = Pick(ssCols, "sectorz", "z");
        var star = Pick(ssCols, "startype", "starclass", "type");
        if (ssId is null || ssName is null || sx is null || sy is null || sz is null) return [];

        var systems = new Dictionary<long, GalaxySystem>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT \"{ssId}\", \"{ssName}\", \"{sx}\", \"{sy}\", \"{sz}\"{(star is null ? "" : $", \"{star}\"")} FROM \"{ssTable}\" LIMIT 20000;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                systems[Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture)] = new GalaxySystem(Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture) ?? "?",
                    Num(r.GetValue(2)), Num(r.GetValue(3)), Num(r.GetValue(4)), star is null || r.IsDBNull(5) ? null : Convert.ToString(r.GetValue(5), CultureInfo.InvariantCulture), []);
        }

        var pfTable = tables.FirstOrDefault(t => t.Equals("Playfields", StringComparison.OrdinalIgnoreCase));
        if (pfTable is not null)
        {
            var cols = Columns(c, pfTable);
            var pfSs = Pick(cols, "ssid", "solarsystemid");
            var pfName = Pick(cols, "name");
            var pfType = Pick(cols, "planettype", "pftype", "type");
            if (pfSs is not null && pfName is not null)
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT \"{pfSs}\", \"{pfName}\"{(pfType is null ? "" : $", \"{pfType}\"")} FROM \"{pfTable}\" LIMIT 50000;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (r.IsDBNull(0) || !systems.TryGetValue(Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture), out var ss)) continue;
                    ss.Playfields.Add(new GalaxyPlayfield(Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture) ?? "?",
                        pfType is null || r.IsDBNull(2) ? null : Convert.ToString(r.GetValue(2), CultureInfo.InvariantCulture)));
                }
            }
        }
        return systems.Values.ToList();
    }

    private static double Num(object v) => v is DBNull ? 0 : Convert.ToDouble(v, CultureInfo.InvariantCulture);

    private static List<string> Names(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static List<string> Columns(SqliteConnection c, string table) => Names(c, $"SELECT name FROM pragma_table_info('{table.Replace("'", "''")}');");

    private static string? Pick(List<string> cols, params string[] wanted) =>
        wanted.Select(w => cols.FirstOrDefault(c => c.Equals(w, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(c => c is not null);
}
