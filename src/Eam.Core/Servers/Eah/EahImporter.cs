using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Eam.Core.Servers.Eah;

/// <summary>What the admin chose to bring over from EAH.</summary>
public sealed class EahImportOptions
{
    public bool Players { get; set; } = true;
    public bool Chat { get; set; } = true;
    public bool History { get; set; } = true;
    public bool Inventories { get; set; } = true;
    public bool Structures { get; set; } = true;
    public bool Statistics { get; set; } = true;
    public bool Settings { get; set; } = true;
    public bool Tasks { get; set; } = true;
    public bool Templates { get; set; } = true;
    /// <summary>Backpack copies older than this are not imported.</summary>
    public int InventoryDays { get; set; } = 30;
}

public sealed record EahPreview(string ConfigDir, string? GameDir, Dictionary<string, long> Counts, Dictionary<string, string?> Server, List<string> Missing);

public sealed class EahImportReport
{
    public Dictionary<string, int> Counts { get; } = new();
    public List<string> Notes { get; } = [];
    public void Add(string key, int n = 1) => Counts[key] = Counts.GetValueOrDefault(key) + n;
}

/// <summary>
/// One-click import of an EAH server: players, notes, chat, histories, backpack copies, structures, statistics,
/// autominers, settings (MOTD, filter, chatbot, Discord, limits...), scheduled tasks and inventory templates.
/// </summary>
public static class EahImporter
{
    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static EahPreview Preview(string configDir, string workDir)
    {
        var counts = new Dictionary<string, long>();
        var missing = new List<string>();
        void Count(string file, string label, params string[] tables)
        {
            var plain = SafeCopy(configDir, file, workDir);
            if (plain is null) { missing.Add(file); return; }
            using var c = EahReader.OpenReadOnly(plain);
            var t = EahReader.Table(c, tables);
            counts[label] = t is null ? 0 : EahReader.Count(c, t);
        }
        try
        {
            Count("EAH_Ply.dat", "players", "clsPlayers", "clsPlayer");
            Count("EAH_Log.dat", "chat", "clsChats", "clsChat");
            Count("EAH_Log.dat", "backpacks", "clsBackpack_Backups", "clsBackpack_Backup");
            Count("EAH_Log.dat", "activities", "clsPlayerActivities", "clsPlayerActivity");
            Count("EAH_Struct.dat", "structures", "clsStructures", "clsStructure");
            Count("EAH_Stat.dat", "statistics", "clsStatistics_Dedis", "clsStatistics_Dedi");
            Count("EAH_AddOn.dat", "autominers", "clsAutoMiners", "clsAutoMiner");
            Count("EAH_Dat.dat", "playfields", "clsPlayfields", "clsPlayfield");
        }
        finally { Cleanup(workDir); }
        var s = EahReader.Settings(configDir);
        counts["tasks"] = TimeTable(configDir).Count;
        counts["chatbotCommands"] = ChatbotOptions(configDir).Count;
        counts["templates"] = TemplateFiles(configDir).Length;
        var server = new Dictionary<string, string?>
        {
            ["serverName"] = s.GetValueOrDefault("Server_Name"),
            ["gamePath"] = s.GetValueOrDefault("Game_Path"),
            ["saveDirectory"] = s.GetValueOrDefault("SaveDirectory"),
            ["gameName"] = s.GetValueOrDefault("GameName"),
            ["dedicated"] = s.GetValueOrDefault("Dedicated"),
            ["telnetPort"] = s.GetValueOrDefault("Telnet_Port"),
        };
        return new EahPreview(configDir, EahReader.FindGameDir(configDir), counts, server, missing);
    }

    public static EahImportReport Import(ServerData data, string configDir, string workDir, EahImportOptions o)
    {
        var report = new EahImportReport();
        try
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // name -> steam id
            var steamByEntity = new Dictionary<long, string>();
            var ply = SafeCopy(configDir, "EAH_Ply.dat", workDir);
            if (ply is not null)
                using (var c = EahReader.OpenReadOnly(ply))
                    Players(data, c, o, report, names, steamByEntity);
            else report.Notes.Add("EAH_Ply.dat not found: no players imported.");

            var log = SafeCopy(configDir, "EAH_Log.dat", workDir);
            if (log is not null)
                using (var c = EahReader.OpenReadOnly(log))
                {
                    if (o.Chat) Chat(data, c, report, names);
                    if (o.History) History(data, c, report, names);
                    if (o.Inventories) Backpacks(data, c, o, report);
                }

            if (o.Structures && SafeCopy(configDir, "EAH_Struct.dat", workDir) is { } st)
                using (var c = EahReader.OpenReadOnly(st)) Structures(data, c, report);

            if (o.Statistics && SafeCopy(configDir, "EAH_Stat.dat", workDir) is { } stat)
                using (var c = EahReader.OpenReadOnly(stat)) Statistics(data, c, report);

            if (o.Players && SafeCopy(configDir, "EAH_AddOn.dat", workDir) is { } addon)
                using (var c = EahReader.OpenReadOnly(addon)) Miners(data, c, report, names);

            if (o.Settings)
            {
                Settings(data, configDir, report);
                if (SafeCopy(configDir, "EAH_Dat.dat", workDir) is { } dat)
                    using (var c = EahReader.OpenReadOnly(dat)) Playfields(data, c, report);
            }
            if (o.Tasks) Tasks(data, configDir, report);
            if (o.Templates) Templates(data, configDir, report);
            data.SetSetting("eah_import_utc", DateTime.UtcNow.ToString("O"));
        }
        finally { Cleanup(workDir); }
        return report;
    }

    private static string? SafeCopy(string configDir, string file, string workDir)
    {
        try { return EahReader.PlainCopy(configDir, file, workDir); }
        catch (InvalidDataException) { return null; }
    }

    private static void Cleanup(string workDir)
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { /* temp files only */ }
    }

    private static string Steam(long id) => id.ToString(CultureInfo.InvariantCulture);
    private static bool IsSteam(string? s) => s is { Length: 17 } && s.All(char.IsAsciiDigit);

    private static SqliteCommand Cmd(SqliteConnection c, SqliteTransaction tx, string sql)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void Run(SqliteCommand cmd, params (string, object?)[] args)
    {
        cmd.Parameters.Clear();
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ players

    private static void Players(ServerData data, SqliteConnection c, EahImportOptions o, EahImportReport report,
        Dictionary<string, string> names, Dictionary<long, string> steamByEntity)
    {
        var t = EahReader.Table(c, "clsPlayers", "clsPlayer");
        if (t is null) return;
        var rows = EahReader.Rows(c, t).ToList();
        foreach (var r in rows)
        {
            var steam = Steam(r.Long("ID"));
            if (!IsSteam(steam)) continue;
            if (r.Str("Name") is { Length: > 0 } n) names[n] = steam;
            var entity = r.Long("Private_ID");
            if (entity > 0) steamByEntity[entity] = steam;
        }
        if (!o.Players) return;
        data.InTransaction((conn, tx) =>
        {
            using var up = Cmd(conn, tx, """
                INSERT INTO players(steam_id, entity_id, name, first_seen_utc, last_seen_utc, playtime_sec) VALUES($s, $e, $n, $f, $l, $p)
                ON CONFLICT(steam_id) DO UPDATE SET
                  first_seen_utc = MIN(first_seen_utc, excluded.first_seen_utc),
                  last_seen_utc = MAX(last_seen_utc, excluded.last_seen_utc),
                  playtime_sec = MAX(playtime_sec, excluded.playtime_sec),
                  entity_id = CASE WHEN entity_id > 0 THEN entity_id ELSE excluded.entity_id END;
                """);
            using var meta = Cmd(conn, tx, """
                INSERT INTO player_meta(steam_id, note, marked, ban_exempt, god, updated_utc) VALUES($s, $n, $m, $b, 0, $u)
                ON CONFLICT(steam_id) DO UPDATE SET
                  note = CASE WHEN note IS NULL OR note = '' THEN excluded.note ELSE note || char(10) || excluded.note END,
                  marked = MAX(marked, excluded.marked), ban_exempt = MAX(ban_exempt, excluded.ban_exempt), updated_utc = excluded.updated_utc;
                """);
            var now = ServerData.UtcText(DateTime.UtcNow);
            foreach (var r in rows)
            {
                var steam = Steam(r.Long("ID"));
                if (!IsSteam(steam)) continue;
                var name = r.Str("Name") is { Length: > 0 } n ? n : steam;
                var first = r.Date("Creation") ?? r.Date("Letzte_Aenderung") ?? DateTime.UtcNow;
                var last = new[] { r.Date("Last_Activity"), r.Date("Letzte_Aenderung"), r.Date("Online_Since") }.Where(d => d is not null).Max() ?? first;
                if (last < first) last = first;
                // Playtime is kept in ticks (100 ns).
                var playtime = r.Long("Playtime") / 10_000_000;
                Run(up, ("$s", steam), ("$e", r.Long("Private_ID")), ("$n", name), ("$f", ServerData.UtcText(first)), ("$l", ServerData.UtcText(last)), ("$p", Math.Max(0, playtime)));
                report.Add("players");
                var note = r.Str("Notes");
                var marked = r.Bool("Remember");
                var exempt = r.Bool("HinderAutoBan");
                if (!string.IsNullOrWhiteSpace(note) || marked || exempt)
                {
                    Run(meta, ("$s", steam), ("$n", string.IsNullOrWhiteSpace(note) ? null : note.Trim()), ("$m", marked ? 1 : 0), ("$b", exempt ? 1 : 0), ("$u", now));
                    report.Add("notes");
                }
            }
        });
    }

    // ------------------------------------------------------------------ chat and histories

    private static void Chat(ServerData data, SqliteConnection c, EahImportReport report, Dictionary<string, string> names)
    {
        var t = EahReader.Table(c, "clsChats", "clsChat");
        if (t is null) return;
        var from = DateTime.UtcNow.AddDays(-ServerData.ChatRetentionDays);
        data.InTransaction((conn, tx) =>
        {
            using var ins = Cmd(conn, tx, """
                INSERT INTO chat(utc, channel, sender_id, sender_name, sender_steam, recipient_id, text, source)
                VALUES($u, $c, -1, $n, $s, -1, $t, 'eah');
                """);
            foreach (var r in EahReader.Rows(c, t))
            {
                var utc = r.Date("DateT");
                var text = r.Str("Text");
                if (utc is null || utc < from || string.IsNullOrEmpty(text)) continue;
                var channel = r.Long("Reciever") switch { 1 => "SinglePlayer", 2 => "Faction", 3 => "Server", 4 => "Alliance", _ => "Global" };
                var name = r.Str("Name") ?? "?";
                Run(ins, ("$u", ServerData.UtcText(utc.Value)), ("$c", channel), ("$n", name), ("$s", names.GetValueOrDefault(name)), ("$t", text));
                report.Add("chat");
            }
        });
    }

    private static void History(ServerData data, SqliteConnection c, EahImportReport report, Dictionary<string, string> names)
    {
        data.InTransaction((conn, tx) =>
        {
            using var ev = Cmd(conn, tx, "INSERT INTO player_events(utc, steam_id, name, kind, detail, by_user) VALUES($u, $s, $n, $k, $d, 'EAH');");
            if (EahReader.Table(c, "clsPlayerActivities", "clsPlayerActivity") is { } act)
                foreach (var r in EahReader.Rows(c, act))
                {
                    var steam = Steam(r.Long("SteamID"));
                    if (!IsSteam(steam) || r.Date("DateT") is not { } utc) continue;
                    var kind = r.Long("Activity") switch
                    {
                        2 => "death", 4 => "credits", 6 => "faction", 7 => "name", 9 => "online", 10 => "playfield", 11 => "ban",
                        12 => "entity", 15 => "permission", 16 => "console", var x => "eah_" + x,
                    };
                    var before = r.Str("Before");
                    var after = r.Str("After");
                    var detail = string.IsNullOrEmpty(before) ? after : $"{before} -> {after}";
                    Run(ev, ("$u", ServerData.UtcText(utc)), ("$s", steam), ("$n", r.Str("Name") ?? steam), ("$k", kind), ("$d", detail));
                    report.Add("history");
                }
            if (EahReader.Table(c, "clsPlayer_Logs", "clsPlayer_Log") is { } plog)
                foreach (var r in EahReader.Rows(c, plog))
                {
                    var steam = Steam(r.Long("SteamID"));
                    if (!IsSteam(steam) || r.Date("DateT") is not { } utc) continue;
                    var kind = r.Long("LogType") switch
                    {
                        10 => "survival", 11 => "autominer_buy", 12 => "autominer_sell", 13 => "autominer_tax", 30 => "autominer_fuel",
                        35 => "limit", 46 => "kick", 47 => "ban", 52 => "event_reward", 53 => "warp_rescue", 56 => "event_spawn", _ => "info",
                    };
                    Run(ev, ("$u", ServerData.UtcText(utc)), ("$s", steam), ("$n", r.Str("Name") ?? steam), ("$k", kind), ("$d", r.Str("Text")));
                    report.Add("history");
                }
            if (EahReader.Table(c, "clsPlayerWarning_Logs", "clsPlayerWarning_Log") is { } warn)
            {
                using var j = Cmd(conn, tx, "INSERT INTO journal(utc, category, severity, steam_id, name, pf, text) VALUES($u, $c, 1, $s, $n, NULL, $t);");
                foreach (var r in EahReader.Rows(c, warn))
                {
                    if (r.Date("DateT") is not { } utc) continue;
                    var steam = r.Long("Typ") == 0 ? Steam(r.Long("ID")) : null;
                    var category = r.Long("Warning") switch { 1 => "chat", 6 => "security", _ => "limit" };
                    Run(j, ("$u", ServerData.UtcText(utc)), ("$c", category), ("$s", IsSteam(steam) ? steam : null), ("$n", r.Str("Name")), ("$t", r.Str("Info") ?? "EAH warning"));
                    report.Add("journal");
                }
            }
            if (EahReader.Table(c, "clsIntruder_Logs", "clsIntruder_Log") is { } intr)
            {
                using var j = Cmd(conn, tx, "INSERT INTO journal(utc, category, severity, steam_id, name, pf, text) VALUES($u, 'attack', 1, $s, $n, $p, $t);");
                foreach (var r in EahReader.Rows(c, intr))
                {
                    if (r.Date("DateT") is not { } utc) continue;
                    var name = r.Str("Name");
                    var text = $"{name} near {r.Str("Intruded_Structure_Name")} ({r.Long("Intruded_Structure_ID")}, {r.Str("Intruded_Structure_Owner")}), {r.Double("Distance"):0} m"
                        + (r.Bool("Death") ? ", died" : "");
                    Run(j, ("$u", ServerData.UtcText(utc)), ("$s", name is null ? null : names.GetValueOrDefault(name)), ("$n", name), ("$p", r.Str("Playfield")), ("$t", text));
                    report.Add("journal");
                }
            }
            if (EahReader.Table(c, "clsEntity_Coordinates_Logs", "clsEntity_Coordinates_Log") is { } coords)
            {
                var from = DateTime.UtcNow.AddDays(-ServerData.PositionRetentionDays);
                using var pos = Cmd(conn, tx, "INSERT INTO positions(utc, kind, ref, name, pf, x, y, z) VALUES($u, $k, $r, $n, $p, $x, $y, $z);");
                foreach (var r in EahReader.Rows(c, coords))
                {
                    if (r.Date("DateT") is not { } utc || utc < from) continue;
                    var isPlayer = r.Long("EntityBaseType") == 0;
                    var reference = isPlayer ? Steam(r.Long("ID")) : r.Long("ID").ToString(CultureInfo.InvariantCulture);
                    if (isPlayer && !IsSteam(reference)) reference = r.Str("Name") is { } n2 && names.TryGetValue(n2, out var s2) ? s2 : reference;
                    Run(pos, ("$u", ServerData.UtcText(utc)), ("$k", isPlayer ? "p" : "s"), ("$r", reference), ("$n", r.Str("Name") ?? reference),
                        ("$p", r.Str("Playfield") ?? ""), ("$x", r.Double("EW")), ("$y", r.Double("Height")), ("$z", r.Double("NS")));
                    report.Add("positions");
                }
            }
        });
    }

    private static void Backpacks(ServerData data, SqliteConnection c, EahImportOptions o, EahImportReport report)
    {
        var t = EahReader.Table(c, "clsBackpack_Backups", "clsBackpack_Backup");
        if (t is null) return;
        var from = DateTime.UtcNow.AddDays(-Math.Clamp(o.InventoryDays, 1, 365));
        data.InTransaction((conn, tx) =>
        {
            using var ins = Cmd(conn, tx, """
                INSERT INTO inventory_snapshots(steam_id, name, utc, reason, pf, toolbelt, bag, hash) VALUES($s, $n, $u, 'eah', NULL, $t, $b, $h);
                """);
            foreach (var r in EahReader.Rows(c, t))
            {
                var steam = Steam(r.Long("SteamID"));
                if (!IsSteam(steam) || r.Date("DateT") is not { } utc || utc < from) continue;
                var (toolbelt, bag) = EahReader.DecodeInventory(r.Str("Inventory"));
                if (toolbelt.Count == 0 && bag.Count == 0) continue;
                var tj = EahReader.ToJson(toolbelt);
                var bj = EahReader.ToJson(bag);
                Run(ins, ("$s", steam), ("$n", r.Str("Name") ?? steam), ("$u", ServerData.UtcText(utc)), ("$t", tj), ("$b", bj), ("$h", ServerData.InventoryHash(tj, bj)));
                report.Add("backpacks");
            }
        });
        // Keep what was imported: EAM prunes copies older than its own setting.
        var sec = FeatureStore.Load<SecuritySettings>(data, "security");
        if (sec.InventoryDays < o.InventoryDays)
        {
            sec.InventoryDays = Math.Clamp(o.InventoryDays, 1, 365);
            try { FeatureStore.Save(data, "security", sec); report.Notes.Add($"Inventory history now kept {sec.InventoryDays} days (like the imported copies)."); }
            catch (FormatException) { }
        }
    }

    // ------------------------------------------------------------------ structures, statistics, autominers

    private static int StructureType(long eah) => eah switch { 1 => EntityTypes.SV, 2 => EntityTypes.CV, 3 => EntityTypes.BA, 4 => EntityTypes.HV, _ => 0 };

    private static void Structures(ServerData data, SqliteConnection c, EahImportReport report)
    {
        data.InTransaction((conn, tx) =>
        {
            if (EahReader.Table(c, "clsStructures", "clsStructure") is { } t)
            {
                using var ins = Cmd(conn, tx, """
                    INSERT OR IGNORE INTO structures(id, name, type, pf, faction_group, faction_id, x, y, z, blocks, devices, class, core, powered,
                      last_visited_utc, solar_system, first_seen_utc, last_seen_utc, removed_utc)
                    VALUES($id, $n, $t, $p, $g, $f, $x, $y, $z, $b, $d, $c, 1, $pw, $lv, $ss, $fs, $ls, $rm);
                    """);
                foreach (var r in EahReader.Rows(c, t))
                {
                    var type = StructureType(r.Long("Typ"));
                    var id = r.Long("ID");
                    if (type == 0 || id <= 0 || id > int.MaxValue) continue;
                    var created = r.Date("Created_Date") ?? r.Date("LastChange") ?? DateTime.UtcNow;
                    var last = r.Date("LastChange") ?? created;
                    var removed = r.Bool("Deleted") ? r.Date("Deleted_Date") ?? last : (DateTime?)null;
                    Run(ins, ("$id", id), ("$n", r.Str("Name") ?? ""), ("$t", type), ("$p", r.Str("Playfield") ?? ""), ("$g", r.Long("Owner_Typ")),
                        ("$f", r.Long("Owner_ID")), ("$x", r.Double("EW")), ("$y", r.Double("Height")), ("$z", r.Double("NS")), ("$b", r.Long("Blocks")),
                        ("$d", r.Long("Devices")), ("$c", r.Long("StructureClass")), ("$pw", r.Bool("Powered") ? 1 : 0),
                        ("$lv", r.Date("LastVisited") is { } lv ? ServerData.UtcText(lv) : null), ("$ss", r.Str("SolarSystem")),
                        ("$fs", ServerData.UtcText(created)), ("$ls", ServerData.UtcText(last)), ("$rm", removed is null ? null : ServerData.UtcText(removed.Value)));
                    report.Add("structures");
                }
            }
            if (EahReader.Table(c, "clsStructure_Logs", "clsStructure_Log") is { } log)
            {
                using var ev = Cmd(conn, tx, "INSERT INTO structure_events(utc, structure_id, kind, old, new, by_user) VALUES($u, $s, $k, $o, $n, 'EAH');");
                foreach (var r in EahReader.Rows(c, log))
                {
                    var id = r.Long("ID");
                    if (id <= 0 || id > int.MaxValue || r.Date("DateT") is not { } utc) continue;
                    void Event(string kind, string? old, string? @new)
                    {
                        Run(ev, ("$u", ServerData.UtcText(utc)), ("$s", id), ("$k", kind), ("$o", old), ("$n", @new));
                        report.Add("structureEvents");
                    }
                    if (r.Str("Old_Name") is { } on && r.Str("New_Name") is { } nn && on != nn) Event("renamed", on, nn);
                    if (r.Str("Old_Playfield") is { } op && r.Str("New_Playfield") is { } np && op != np) Event("playfield", op, np);
                    var oldOwner = $"{r.Long("Old_Owner_Typ")}:{r.Long("Old_Owner_ID")}";
                    var newOwner = $"{r.Long("New_Owner_Typ")}:{r.Long("New_Owner_ID")}";
                    if (oldOwner != newOwner && (r.Long("Old_Owner_ID") != 0 || r.Long("New_Owner_ID") != 0)) Event("owner", oldOwner, newOwner);
                }
            }
        });
    }

    private static void Statistics(ServerData data, SqliteConnection c, EahImportReport report)
    {
        var from = DateTime.UtcNow.AddDays(-14);
        data.InTransaction((conn, tx) =>
        {
            using var ins = Cmd(conn, tx, "INSERT INTO stats(utc, scope, cpu, mem, players, playfields, fps, structs) VALUES($u, $sc, $c, $m, $p, $pf, $f, $s);");
            if (EahReader.Table(c, "clsStatistics_Dedis", "clsStatistics_Dedi") is { } t)
                foreach (var r in EahReader.Rows(c, t))
                {
                    if (r.Date("DateT") is not { } utc || utc < from) continue;
                    Run(ins, ("$u", ServerData.UtcText(utc)), ("$sc", "server"), ("$c", r.Double("CPU")), ("$m", r.Double("Memory")),
                        ("$p", r.Long("Player")), ("$pf", r.Long("Playfields")), ("$f", r.Double("FPS")), ("$s", 0));
                    report.Add("statistics");
                }
            if (EahReader.Table(c, "clsStatistics_Playfields", "clsStatistics_Playfield") is { } p)
                foreach (var r in EahReader.Rows(c, p))
                {
                    if (r.Date("DateT") is not { } utc || utc < from || r.Str("Playfield") is not { Length: > 0 } pf) continue;
                    Run(ins, ("$u", ServerData.UtcText(utc)), ("$sc", "pf:" + pf), ("$c", 0), ("$m", r.Double("Memory")),
                        ("$p", r.Long("Player")), ("$pf", 1), ("$f", r.Double("FPS")), ("$s", r.Long("Structs")));
                    report.Add("statistics");
                }
        });
    }

    private static void Miners(ServerData data, SqliteConnection c, EahImportReport report, Dictionary<string, string> names)
    {
        var t = EahReader.Table(c, "clsAutoMiners", "clsAutoMiner");
        if (t is null) return;
        var ores = FeatureStore.Load<GameplaySettings>(data, "gameplay").Ores;
        var skipped = 0;
        data.InTransaction((conn, tx) =>
        {
            using var ins = Cmd(conn, tx, """
                INSERT INTO miners(steam_id, ore, level, stored, fuel_hours, updated_utc) VALUES($s, $o, $l, $a, $f, $u)
                ON CONFLICT(steam_id, ore) DO UPDATE SET level = MAX(level, excluded.level), stored = stored + excluded.stored, fuel_hours = MAX(fuel_hours, excluded.fuel_hours);
                """);
            foreach (var r in EahReader.Rows(c, t))
            {
                if (r.Bool("IsFaction")) { skipped++; continue; }
                var steam = Steam(r.Long("Owner_ID"));
                if (!IsSteam(steam)) steam = r.Str("Owner_Name") is { } n && names.TryGetValue(n, out var s) ? s : "";
                var item = (int)r.Long("Mining_Item");
                var ore = ores.FirstOrDefault(x => x.ItemId == item || x.ItemId == item + 4096 || x.ItemId + 4096 == item)?.Key;
                if (!IsSteam(steam) || ore is null) { skipped++; continue; }
                Run(ins, ("$s", steam), ("$o", ore), ("$l", Math.Max(1, r.Long("Mining_Level"))), ("$a", r.Double("Amount")), ("$f", r.Double("Fuel")),
                    ("$u", ServerData.UtcText(DateTime.UtcNow)));
                report.Add("autominers");
            }
        });
        if (skipped > 0) report.Notes.Add($"{skipped} autominer(s) not imported (faction miners, or ore not set up in EAM's Autominer).");
    }

    // ------------------------------------------------------------------ settings

    private static bool Yes(Dictionary<string, string> s, string key) =>
        s.TryGetValue(key, out var v) && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1");

    private static int Int(Dictionary<string, string> s, string key, int def) =>
        s.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : def;

    private static void Settings(ServerData data, string configDir, EahImportReport report)
    {
        var s = EahReader.Settings(configDir);
        if (s.Count == 0) { report.Notes.Add("Settings.XML not found: EAH settings not imported."); return; }

        var com = FeatureStore.Load<CommunitySettings>(data, "community");
        if (s.GetValueOrDefault("MOTD") is { Length: > 0 } motd) com.Motd = motd;
        if (s.GetValueOrDefault("Welcome_Message") is { Length: > 0 } welcome) com.Welcome = welcome;
        if (s.ContainsKey("ChatBotOn")) com.ChatbotEnabled = Yes(s, "ChatBotOn");
        if (s.ContainsKey("SwearWordFilterOn")) com.FilterEnabled = Yes(s, "SwearWordFilterOn");
        var words = EahReader.SplitList(s.GetValueOrDefault("SwearFilter_Words"));
        var listFile = Path.Combine(configDir, "SwearWordList.txt");
        if (File.Exists(listFile)) words.AddRange(File.ReadAllLines(listFile).Select(w => w.Trim()).Where(w => w.Length > 0));
        if (words.Count > 0) com.FilterWords = com.FilterWords.Concat(words).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (s.ContainsKey("SwearFilter_OnlyGlobalChat")) com.FilterGlobalOnly = Yes(s, "SwearFilter_OnlyGlobalChat");
        com.FilterWarnings = Math.Clamp(Int(s, "SwearFilter_MaxWarnings", com.FilterWarnings), 1, 20);
        var alerts = EahReader.SplitList(s.GetValueOrDefault("Chat_Alerts")).Concat(EahReader.SplitList(s.GetValueOrDefault("SupportAlertWords"))).ToList();
        if (alerts.Count > 0) com.AlertWords = com.AlertWords.Concat(alerts).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var token = EahReader.DecryptSecret(s.GetValueOrDefault("Discord_Token")) ?? "";
        if (token.Length > 0) com.DiscordToken = token;
        if (s.GetValueOrDefault("Discord_Public_Channel") is { Length: > 0 } pub && pub.All(char.IsAsciiDigit)) com.DiscordPublicChannel = pub;
        if (s.GetValueOrDefault("Discord_Admin_Channel") is { Length: > 0 } adm && adm.All(char.IsAsciiDigit)) com.DiscordAdminChannel = adm;
        if (com.DiscordToken.Length > 20 && com.DiscordPublicChannel.Length > 0) com.DiscordEnabled = true;
        foreach (var cb in ChatbotOptions(configDir))
            if (!com.Commands.Any(x => x.Command.Equals(cb.Command, StringComparison.OrdinalIgnoreCase)))
            {
                com.Commands.Add(cb);
                report.Add("chatbotCommands");
            }
        var survival = Path.Combine(configDir, "..", "Inventory Templates", "Survival.xml");
        if (File.Exists(survival) && com.SurvivalKit.Count == 0)
        {
            var (tb, bag) = EahReader.LoadTemplate(survival);
            com.SurvivalKit = tb.Concat(bag).Select(x => new KitItem { Id = x.Id, Count = x.Count }).ToList();
        }
        SaveFeature(data, "community", com, report);

        var gp = FeatureStore.Load<GameplaySettings>(data, "gameplay");
        if (s.ContainsKey("Activate_AutoMiner")) gp.AutominerEnabled = Yes(s, "Activate_AutoMiner");
        if (s.ContainsKey("Activate_Events")) gp.EventsEnabled = Yes(s, "Activate_Events");
        SaveFeature(data, "gameplay", gp, report);

        var sec = FeatureStore.Load<SecuritySettings>(data, "security");
        if (s.ContainsKey("Activate_BackpackBackup")) sec.InventoryHistoryEnabled = Yes(s, "Activate_BackpackBackup");
        if (s.ContainsKey("Check_Cheater")) sec.AntiCheatEnabled = Yes(s, "Check_Cheater");
        if (s.ContainsKey("AutoBan")) sec.AutoBanExtreme = Yes(s, "AutoBan");
        if (s.ContainsKey("Intruder_Log")) sec.AttackLogEnabled = Yes(s, "Intruder_Log");
        SaveFeature(data, "security", sec, report);

        var world = FeatureStore.Load<WorldSettings>(data, "world");
        var publicHours = Int(s, "SetStructuresToPublicIfNotTouchedForHours", 0);
        if (publicHours > 0) world.UntouchedPublicHours = publicHours;
        SaveFeature(data, "world", world, report);
        report.Add("settings");
    }

    private static void SaveFeature<T>(ServerData data, string key, T value, EahImportReport report) where T : IFeatureSettings
    {
        try { FeatureStore.Save(data, key, value); }
        catch (FormatException e) { report.Notes.Add($"Settings '{key}' not imported ({e.Message}): check them in EAM."); }
    }

    private static void Playfields(ServerData data, SqliteConnection c, EahImportReport report)
    {
        var t = EahReader.Table(c, "clsPlayfields", "clsPlayfield");
        if (t is null) return;
        var world = FeatureStore.Load<WorldSettings>(data, "world");
        var com = FeatureStore.Load<CommunitySettings>(data, "community");
        var types = new[] { ("BA", "Base"), ("CV", "CV"), ("SV", "SV"), ("HV", "HV") };
        var limits = 0;
        foreach (var r in EahReader.Rows(c, t))
        {
            var pf = r.Str("Name");
            if (string.IsNullOrWhiteSpace(pf)) continue;
            foreach (var (eam, eah) in types)
            {
                var player = (int)r.Long("MaxPlayer" + eah);
                var maxClass = (int)r.Long("MaxClass" + eah);
                var maxBlocks = (int)r.Long("MaxBlocks" + eah);
                if (player > 0 || maxClass > 0 || maxBlocks > 0)
                {
                    world.Limits.Add(new LimitRule { Scope = "player", Playfields = pf, Type = eam, MaxCount = Math.Max(0, player), MaxClass = Math.Max(0, maxClass), MaxBlocks = Math.Max(0, maxBlocks) });
                    limits++;
                }
                var faction = (int)r.Long("MaxFaction" + eah);
                if (faction > 0)
                {
                    world.Limits.Add(new LimitRule { Scope = "faction", Playfields = pf, Type = eam, MaxCount = faction });
                    limits++;
                }
            }
            var notTouched = (int)r.Long("WipeStructureAfterNotTouchHours");
            var older = (int)r.Long("WipeStructureAfterHours");
            var onPf = (int)r.Long("WipeAfterHoursOnPlayfield");
            if (notTouched > 0 || older > 0 || onPf > 0)
                world.AutoWipe.Add(new AutoWipeRule
                {
                    Playfields = pf,
                    NotVisitedDays = notTouched > 0 ? Math.Max(1, (notTouched + 23) / 24) : 0,
                    OlderThanHours = Math.Max(0, older),
                    OnPlayfieldHours = Math.Max(0, onPf),
                });
            if (r.Str("EntranceMessage") is { Length: > 0 } msg && !com.PlayfieldMessages.Any(m => m.Playfield.Equals(pf, StringComparison.OrdinalIgnoreCase)))
                com.PlayfieldMessages.Add(new PlayfieldMessage { Playfield = pf, Text = msg });
        }
        if (limits > 0) report.Add("limits", limits);
        if (world.AutoWipe.Count > 0) report.Add("autoWipes", world.AutoWipe.Count);
        SaveFeature(data, "world", world, report);
        SaveFeature(data, "community", com, report);
    }

    // ------------------------------------------------------------------ chatbot options, scheduled tasks, templates

    public static List<CustomCommand> ChatbotOptions(string configDir)
    {
        var list = new List<CustomCommand>();
        var doc = EahReader.LoadXml(Path.Combine(configDir, "Chatbot_Option.XML"));
        if (doc is null) return list;
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "CBO"))
        {
            var active = (string?)e.Attribute("AC");
            if (active is not null && active.Equals("false", StringComparison.OrdinalIgnoreCase)) continue;
            var cmd = ((string?)e.Attribute("C") ?? "").Trim();
            var answer = ((string?)e.Attribute("A") ?? "").Trim();
            if (cmd.Length == 0 || answer.Length == 0) continue;
            list.Add(new CustomCommand { Command = cmd.Length > 32 ? cmd[..32] : cmd, Description = ((string?)e.Attribute("I") ?? "").Trim(), Response = answer });
        }
        return list;
    }

    public sealed record EahTask(string Kind, bool Enabled, object Schedule, object Params, string Source);

    /// <summary>EAH's TimeTable.XML converted to EAM tasks. Actions EAM does not have are left out.</summary>
    public static List<EahTask> TimeTable(string configDir)
    {
        var list = new List<EahTask>();
        var doc = EahReader.LoadXml(Path.Combine(configDir, "TimeTable.XML"));
        if (doc is null) return list;
        foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "TT"))
        {
            int A(string n) => int.TryParse((string?)e.Attribute(n), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : -1;
            var action = A("A");
            var repeat = A("W");
            var p = ((string?)e.Attribute("P") ?? "").Trim();
            var active = !string.Equals((string?)e.Attribute("AC"), "false", StringComparison.OrdinalIgnoreCase);
            var subs = e.Elements().Where(x => x.Name.LocalName == "TTS").Select(x => (string?)x.Attribute("A")).ToList();
            var backupFirst = subs.Any(a => a is "3" or "50" or "28");
            // EAH lists playfields separated by '#' ("Akua#Tallodar#"); "all" (or nothing) means every playfield.
            List<string> Pfs()
            {
                var l = EahReader.SplitList(p);
                return l.Count == 0 || l.Any(x => x.Equals("all", StringComparison.OrdinalIgnoreCase) || x == "*") ? ["*"] : l;
            }
            (string Kind, object Params)? mapped = action switch
            {
                0 or 7 when p.Length > 0 => ("message", new { text = p, mode = "chat" }),
                3 or 28 or 50 => ("backup", new { }),
                4 or 10 => ("stop", new { warnMinutes = 5 }),
                5 or 9 or 11 => ("restart", new { warnMinutes = 5, backupFirst }),
                6 => ("start", new { }),
                17 or 57 => ("backup_structures", new { }),
                18 => ("shared_cleanup", new { }),
                21 => ("limits", new { }),
                24 => ("cleanup_players", new { days = 90 }),
                31 => ("wipe", new { wipe = "all", playfields = Pfs(), backupFirst = true }),
                32 => ("wipe", new { wipe = "deposit", playfields = Pfs() }),
                36 => ("wipe", new { wipe = "poi", playfields = Pfs() }),
                38 => ("wipe", new { wipe = "terrain", playfields = Pfs() }),
                _ => null,
            };
            if (mapped is null) continue;
            var time = EahReader.ToUtc((string?)e.Attribute("DZ"))?.ToLocalTime();
            var hhmm = (time ?? DateTime.Today.AddHours(4)).ToString("HH:mm", CultureInfo.InvariantCulture);
            int[] minutes = [0, 1, 2, 3, 5, 10, 15, 20, 30, 45, 60, 120, 180, 360, 720, 1440];
            object? schedule = repeat switch
            {
                >= 1 and <= 15 => new { type = "interval", minutes = Math.Max(5, minutes[repeat]) },
                16 => new { type = "daily", times = new[] { hhmm }, days = Array.Empty<int>() },
                >= 17 and <= 23 => new { type = "daily", times = new[] { hhmm }, days = new[] { (repeat - 16) % 7 } },
                24 => new { type = "monthly", times = new[] { hhmm }, day = Math.Clamp((time ?? DateTime.Today).Day, 1, 28) },
                _ => null,
            };
            if (schedule is null) continue;
            list.Add(new EahTask(mapped.Value.Kind, active, schedule, mapped.Value.Params, $"A={action} W={repeat}"));
        }
        return list;
    }

    private static void Tasks(ServerData data, string configDir, EahImportReport report)
    {
        var existing = data.Tasks().Select(t => t.Kind + "|" + t.Schedule + "|" + t.Params).ToHashSet();
        foreach (var t in TimeTable(configDir))
        {
            var schedule = JsonSerializer.Serialize(t.Schedule);
            var pars = JsonSerializer.Serialize(t.Params, Camel);
            if (!existing.Add(t.Kind + "|" + schedule + "|" + pars)) continue;
            // Same state as in EAH (on or off).
            data.SaveTask(new ScheduledTask(0, t.Kind, t.Enabled, schedule, pars, null, "imported from EAH" + (t.Enabled ? "" : " (was off)")));
            report.Add("tasks");
        }
        if (report.Counts.GetValueOrDefault("tasks") > 0) report.Notes.Add("Tasks keep their EAH state: stop EAH for this server so the two tools never restart or wipe it twice.");
    }

    public static string[] TemplateFiles(string configDir)
    {
        var dir = Path.GetFullPath(Path.Combine(configDir, "..", "Inventory Templates"));
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.xml") : [];
    }

    private static void Templates(ServerData data, string configDir, EahImportReport report)
    {
        foreach (var file in TemplateFiles(configDir))
        {
            var (tb, bag) = EahReader.LoadTemplate(file);
            if (tb.Count == 0 && bag.Count == 0) continue;
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Length > 40) name = name[..40];
            data.SaveInventoryTemplate(name, EahReader.ToJson(tb), EahReader.ToJson(bag));
            report.Add("templates");
        }
    }
}
