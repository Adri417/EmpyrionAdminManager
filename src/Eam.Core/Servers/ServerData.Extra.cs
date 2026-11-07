using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Eam.Core.Servers;

public sealed record StructureRow(
    int Id, string Name, int Type, string Playfield, int FactionGroup, int FactionId, double X, double Y, double Z,
    int Blocks, int Devices, int Class, int Core, bool Powered, DateTime? LastVisitedUtc, string? SolarSystem,
    DateTime FirstSeenUtc, DateTime LastSeenUtc, DateTime? RemovedUtc, DateTime? WarnedUtc);

public sealed record StructureEvent(long Id, DateTime Utc, int StructureId, string Kind, string? Old, string? New, string? By);

public sealed record PositionRow(DateTime Utc, string Kind, string Ref, string Name, string Playfield, double X, double Y, double Z);

public sealed record JournalEntry(long Id, DateTime Utc, string Category, int Severity, string? SteamId, string? Name, string? Playfield, string Text);

public sealed record StatRow(DateTime Utc, string Scope, double Cpu, double Mem, int Players, int Playfields, double Fps, int Structures);

public sealed record CounterRow(string SteamId, string Key, int Count, DateTime LastUtc, string? Value);

public sealed record MinerRow(string SteamId, string Ore, int Level, double Stored, double FuelHours, DateTime UpdatedUtc);

public sealed record GameEventRow(long Id, string Name, string Playfield, double X, double Y, double Z, string Kit, bool KeepInventory, string State, DateTime CreatedUtc);

public sealed record GameEventPlayer(long EventId, string SteamId, string Name, string State, string? Saved, string? BackPlayfield, double BackX, double BackY, double BackZ);

/// <summary>Steps 6 to 9: structures, history, journal, statistics, moderation counters and gameplay tables.</summary>
public sealed partial class ServerData
{
    // ------------------------------------------------------------------ small helpers

    private int Exec(string sql, params (string, object?)[] args)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    private static string Utc(DateTime d) => d.ToUniversalTime().ToString("O");
    private static string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static DateTime? D(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : ParseUtc(r.GetString(i));
    private static double F(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetDouble(i);
    private static int I(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt32(i);

    // ------------------------------------------------------------------ structures

    private const string StructureCols = "id, name, type, pf, faction_group, faction_id, x, y, z, blocks, devices, class, core, powered, last_visited_utc, solar_system, first_seen_utc, last_seen_utc, removed_utc, warned_utc";

    private static StructureRow MapStructure(SqliteDataReader r) => new(
        r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5), F(r, 6), F(r, 7), F(r, 8),
        I(r, 9), I(r, 10), I(r, 11), I(r, 12), I(r, 13) != 0, D(r, 14), S(r, 15), ParseUtc(r.GetString(16)), ParseUtc(r.GetString(17)), D(r, 18), D(r, 19));

    public IReadOnlyList<StructureRow> Structures(bool includeRemoved = false) =>
        Query($"SELECT {StructureCols} FROM structures {(includeRemoved ? "" : "WHERE removed_utc IS NULL")} ORDER BY pf, name;", MapStructure);

    public IReadOnlyList<StructureRow> RemovedStructures(DateTime sinceUtc) =>
        Query($"SELECT {StructureCols} FROM structures WHERE removed_utc >= $s ORDER BY removed_utc DESC;", MapStructure, ("$s", Utc(sinceUtc)));

    public StructureRow? Structure(int id) => Query($"SELECT {StructureCols} FROM structures WHERE id=$id;", MapStructure, ("$id", id)).FirstOrDefault();

    /// <summary>
    /// Stores a complete list read from the game and returns what changed since the previous one
    /// (created, removed, renamed, moved to another playfield, new owner).
    /// </summary>
    public IReadOnlyList<StructureEvent> SyncStructures(IReadOnlyList<StructureRow> current, DateTime nowUtc)
    {
        var events = new List<StructureEvent>();
        var now = Utc(nowUtc);
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        var known = new Dictionary<int, StructureRow>();
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = $"SELECT {StructureCols} FROM structures WHERE removed_utc IS NULL;";
            using var r = q.ExecuteReader();
            while (r.Read()) { var s = MapStructure(r); known[s.Id] = s; }
        }
        var firstSync = known.Count == 0 && !Query("SELECT 1 FROM structures LIMIT 1;", r => 1).Any();

        void Event(int sid, string kind, string? old, string? @new)
        {
            using var e = c.CreateCommand();
            e.Transaction = tx;
            e.CommandText = "INSERT INTO structure_events(utc, structure_id, kind, old, new) VALUES($u, $s, $k, $o, $n);";
            e.Parameters.AddWithValue("$u", now);
            e.Parameters.AddWithValue("$s", sid);
            e.Parameters.AddWithValue("$k", kind);
            e.Parameters.AddWithValue("$o", (object?)old ?? DBNull.Value);
            e.Parameters.AddWithValue("$n", (object?)@new ?? DBNull.Value);
            e.ExecuteNonQuery();
            events.Add(new StructureEvent(0, nowUtc, sid, kind, old, @new, null));
        }

        foreach (var s in current)
        {
            if (known.Remove(s.Id, out var old))
            {
                if (old.Name != s.Name) Event(s.Id, "renamed", old.Name, s.Name);
                if (!string.Equals(old.Playfield, s.Playfield, StringComparison.Ordinal)) Event(s.Id, "playfield", old.Playfield, s.Playfield);
                if (old.FactionGroup != s.FactionGroup || old.FactionId != s.FactionId)
                    Event(s.Id, "owner", $"{old.FactionGroup}:{old.FactionId}", $"{s.FactionGroup}:{s.FactionId}");
            }
            else if (!firstSync) Event(s.Id, "created", null, $"{s.Name} @ {s.Playfield}");

            using var up = c.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO structures(id, name, type, pf, faction_group, faction_id, x, y, z, blocks, devices, class, core, powered, last_visited_utc, solar_system, first_seen_utc, last_seen_utc, removed_utc)
                VALUES($id, $n, $t, $pf, $fg, $fid, $x, $y, $z, $b, $d, $c, $core, $p, $lv, $ss, $now, $now, NULL)
                ON CONFLICT(id) DO UPDATE SET name=$n, type=$t, pf=$pf, faction_group=$fg, faction_id=$fid, x=$x, y=$y, z=$z, blocks=$b, devices=$d,
                    class=$c, core=$core, powered=$p, last_visited_utc=$lv, solar_system=$ss, last_seen_utc=$now, removed_utc=NULL;
                """;
            up.Parameters.AddWithValue("$id", s.Id);
            up.Parameters.AddWithValue("$n", s.Name);
            up.Parameters.AddWithValue("$t", s.Type);
            up.Parameters.AddWithValue("$pf", s.Playfield);
            up.Parameters.AddWithValue("$fg", s.FactionGroup);
            up.Parameters.AddWithValue("$fid", s.FactionId);
            up.Parameters.AddWithValue("$x", s.X);
            up.Parameters.AddWithValue("$y", s.Y);
            up.Parameters.AddWithValue("$z", s.Z);
            up.Parameters.AddWithValue("$b", s.Blocks);
            up.Parameters.AddWithValue("$d", s.Devices);
            up.Parameters.AddWithValue("$c", s.Class);
            up.Parameters.AddWithValue("$core", s.Core);
            up.Parameters.AddWithValue("$p", s.Powered ? 1 : 0);
            up.Parameters.AddWithValue("$lv", s.LastVisitedUtc is { } lv ? Utc(lv) : DBNull.Value);
            up.Parameters.AddWithValue("$ss", (object?)s.SolarSystem ?? DBNull.Value);
            up.Parameters.AddWithValue("$now", now);
            up.ExecuteNonQuery();

            using var pos = c.CreateCommand();
            pos.Transaction = tx;
            pos.CommandText = "INSERT INTO positions(utc, kind, ref, name, pf, x, y, z) VALUES($u, 's', $r, $n, $pf, $x, $y, $z);";
            pos.Parameters.AddWithValue("$u", now);
            pos.Parameters.AddWithValue("$r", s.Id.ToString(CultureInfo.InvariantCulture));
            pos.Parameters.AddWithValue("$n", s.Name);
            pos.Parameters.AddWithValue("$pf", s.Playfield);
            pos.Parameters.AddWithValue("$x", s.X);
            pos.Parameters.AddWithValue("$y", s.Y);
            pos.Parameters.AddWithValue("$z", s.Z);
            pos.ExecuteNonQuery();
        }

        foreach (var gone in known.Values)
        {
            using var rm = c.CreateCommand();
            rm.Transaction = tx;
            rm.CommandText = "UPDATE structures SET removed_utc=$now WHERE id=$id;";
            rm.Parameters.AddWithValue("$now", now);
            rm.Parameters.AddWithValue("$id", gone.Id);
            rm.ExecuteNonQuery();
            Event(gone.Id, "destroyed", $"{gone.Name} @ {gone.Playfield}", null);
        }
        tx.Commit();
        return events;
    }

    public void StructureEventBy(int structureId, string kind, string? old, string? @new, string by) =>
        Exec("INSERT INTO structure_events(utc, structure_id, kind, old, new, by_user) VALUES($u, $s, $k, $o, $n, $b);",
            ("$u", Utc(DateTime.UtcNow)), ("$s", structureId), ("$k", kind), ("$o", old), ("$n", @new), ("$b", by));

    public void SetStructureWarned(int id, DateTime? utc) =>
        Exec("UPDATE structures SET warned_utc=$w WHERE id=$id;", ("$w", utc is { } u ? Utc(u) : null), ("$id", id));

    public IReadOnlyList<StructureEvent> StructureEvents(int? structureId, int limit = 300, DateTime? fromUtc = null, DateTime? toUtc = null) =>
        Query("""
            SELECT id, utc, structure_id, kind, old, new, by_user FROM structure_events
            WHERE ($s IS NULL OR structure_id=$s) AND ($f IS NULL OR utc >= $f) AND ($t IS NULL OR utc <= $t)
            ORDER BY id DESC LIMIT $l;
            """,
            r => new StructureEvent(r.GetInt64(0), ParseUtc(r.GetString(1)), r.GetInt32(2), r.GetString(3), S(r, 4), S(r, 5), S(r, 6)),
            ("$s", structureId), ("$f", fromUtc is { } f ? Utc(f) : null), ("$t", toUtc is { } t ? Utc(t) : null), ("$l", Math.Clamp(limit, 1, 5000)));

    // ------------------------------------------------------------------ positions ("what happened here")

    public void AddPositions(IEnumerable<PositionRow> rows)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        foreach (var p in rows)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO positions(utc, kind, ref, name, pf, x, y, z) VALUES($u, $k, $r, $n, $pf, $x, $y, $z);";
            cmd.Parameters.AddWithValue("$u", Utc(p.Utc));
            cmd.Parameters.AddWithValue("$k", p.Kind);
            cmd.Parameters.AddWithValue("$r", p.Ref);
            cmd.Parameters.AddWithValue("$n", p.Name);
            cmd.Parameters.AddWithValue("$pf", p.Playfield);
            cmd.Parameters.AddWithValue("$x", p.X);
            cmd.Parameters.AddWithValue("$y", p.Y);
            cmd.Parameters.AddWithValue("$z", p.Z);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Players and structures seen within <paramref name="radius"/> metres of a point, during a period.</summary>
    public IReadOnlyList<PositionRow> PositionsNear(string playfield, double x, double y, double z, double radius, DateTime fromUtc, DateTime toUtc, int limit = 2000) =>
        Query("""
            SELECT utc, kind, ref, name, pf, x, y, z FROM positions
            WHERE pf=$pf AND utc >= $f AND utc <= $t AND x BETWEEN $x0 AND $x1 AND z BETWEEN $z0 AND $z1
              AND ((x-$x)*(x-$x) + (y-$y)*(y-$y) + (z-$z)*(z-$z)) <= $r2
            ORDER BY utc DESC LIMIT $l;
            """,
            MapPosition, ("$pf", playfield), ("$f", Utc(fromUtc)), ("$t", Utc(toUtc)), ("$x", x), ("$y", y), ("$z", z),
            ("$x0", x - radius), ("$x1", x + radius), ("$z0", z - radius), ("$z1", z + radius), ("$r2", radius * radius), ("$l", Math.Clamp(limit, 1, 10000)));

    public IReadOnlyList<PositionRow> PositionsOf(string reference, DateTime fromUtc, DateTime toUtc, int limit = 2000) =>
        Query("SELECT utc, kind, ref, name, pf, x, y, z FROM positions WHERE ref=$r AND utc >= $f AND utc <= $t ORDER BY utc DESC LIMIT $l;",
            MapPosition, ("$r", reference), ("$f", Utc(fromUtc)), ("$t", Utc(toUtc)), ("$l", Math.Clamp(limit, 1, 10000)));

    private static PositionRow MapPosition(SqliteDataReader r) =>
        new(ParseUtc(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7));

    // ------------------------------------------------------------------ journal (alerts, warnings, actions)

    public JournalEntry AddJournal(string category, int severity, string text, string? steamId = null, string? name = null, string? playfield = null)
    {
        var now = DateTime.UtcNow;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO journal(utc, category, severity, steam_id, name, pf, text) VALUES($u, $c, $s, $st, $n, $pf, $t) RETURNING id;";
        cmd.Parameters.AddWithValue("$u", Utc(now));
        cmd.Parameters.AddWithValue("$c", category);
        cmd.Parameters.AddWithValue("$s", severity);
        cmd.Parameters.AddWithValue("$st", (object?)steamId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$n", (object?)name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pf", (object?)playfield ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", text.Length > 1000 ? text[..1000] : text);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        return new JournalEntry(id, now, category, severity, steamId, name, playfield, text);
    }

    public IReadOnlyList<JournalEntry> Journal(string? category, int minSeverity, int limit, long? beforeId = null, string? steamId = null) =>
        Query("""
            SELECT id, utc, category, severity, steam_id, name, pf, text FROM journal
            WHERE ($c IS NULL OR category=$c) AND severity >= $s AND ($b IS NULL OR id < $b) AND ($st IS NULL OR steam_id=$st)
            ORDER BY id DESC LIMIT $l;
            """,
            r => new JournalEntry(r.GetInt64(0), ParseUtc(r.GetString(1)), r.GetString(2), r.GetInt32(3), S(r, 4), S(r, 5), S(r, 6), r.GetString(7)),
            ("$c", category), ("$s", minSeverity), ("$b", beforeId), ("$st", steamId), ("$l", Math.Clamp(limit, 1, 1000)));

    // ------------------------------------------------------------------ statistics

    public void AddStat(StatRow s) =>
        Exec("INSERT INTO stats(utc, scope, cpu, mem, players, playfields, fps, structs) VALUES($u, $sc, $c, $m, $p, $pf, $f, $s);",
            ("$u", Utc(s.Utc)), ("$sc", s.Scope), ("$c", s.Cpu), ("$m", s.Mem), ("$p", s.Players), ("$pf", s.Playfields), ("$f", s.Fps), ("$s", s.Structures));

    public IReadOnlyList<StatRow> Stats(string scope, DateTime fromUtc) =>
        Query("SELECT utc, scope, cpu, mem, players, playfields, fps, structs FROM stats WHERE scope=$s AND utc >= $f ORDER BY utc;",
            r => new StatRow(ParseUtc(r.GetString(0)), r.GetString(1), F(r, 2), F(r, 3), I(r, 4), I(r, 5), F(r, 6), I(r, 7)), ("$s", scope), ("$f", Utc(fromUtc)));

    public IReadOnlyList<string> StatScopes(DateTime fromUtc) =>
        Query("SELECT DISTINCT scope FROM stats WHERE utc >= $f ORDER BY scope;", r => r.GetString(0), ("$f", Utc(fromUtc)));

    // ------------------------------------------------------------------ per-player counters (cooldowns, warnings, mutes, claims)

    public CounterRow? Counter(string steamId, string key) =>
        Query("SELECT steam_id, key, count, last_utc, value FROM counters WHERE steam_id=$s AND key=$k;",
            r => new CounterRow(r.GetString(0), r.GetString(1), r.GetInt32(2), ParseUtc(r.GetString(3)), S(r, 4)), ("$s", steamId), ("$k", key)).FirstOrDefault();

    public IReadOnlyList<CounterRow> Counters(string keyPrefix) =>
        Query("SELECT steam_id, key, count, last_utc, value FROM counters WHERE key LIKE $k ORDER BY last_utc DESC;",
            r => new CounterRow(r.GetString(0), r.GetString(1), r.GetInt32(2), ParseUtc(r.GetString(3)), S(r, 4)), ("$k", keyPrefix + "%"));

    public void SetCounter(string steamId, string key, int count, string? value = null) =>
        Exec("""
            INSERT INTO counters(steam_id, key, count, last_utc, value) VALUES($s, $k, $c, $u, $v)
            ON CONFLICT(steam_id, key) DO UPDATE SET count=$c, last_utc=$u, value=$v;
            """, ("$s", steamId), ("$k", key), ("$c", count), ("$u", Utc(DateTime.UtcNow)), ("$v", value));

    public void DeleteCounter(string steamId, string key) => Exec("DELETE FROM counters WHERE steam_id=$s AND key=$k;", ("$s", steamId), ("$k", key));

    public int DeleteCounters(string keyPrefix) => Exec("DELETE FROM counters WHERE key LIKE $k;", ("$k", keyPrefix + "%"));

    // ------------------------------------------------------------------ autominer

    public IReadOnlyList<MinerRow> Miners(string? steamId = null) =>
        Query("SELECT steam_id, ore, level, stored, fuel_hours, updated_utc FROM miners WHERE ($s IS NULL OR steam_id=$s) ORDER BY steam_id, ore;",
            r => new MinerRow(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetDouble(3), r.GetDouble(4), ParseUtc(r.GetString(5))), ("$s", steamId));

    public void SaveMiner(MinerRow m) =>
        Exec("""
            INSERT INTO miners(steam_id, ore, level, stored, fuel_hours, updated_utc) VALUES($s, $o, $l, $st, $f, $u)
            ON CONFLICT(steam_id, ore) DO UPDATE SET level=$l, stored=$st, fuel_hours=$f, updated_utc=$u;
            """, ("$s", m.SteamId), ("$o", m.Ore), ("$l", m.Level), ("$st", m.Stored), ("$f", m.FuelHours), ("$u", Utc(m.UpdatedUtc)));

    public void DeleteMiner(string steamId, string ore) => Exec("DELETE FROM miners WHERE steam_id=$s AND ore=$o;", ("$s", steamId), ("$o", ore));

    // ------------------------------------------------------------------ in-game events

    private static GameEventRow MapEvent(SqliteDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2), F(r, 3), F(r, 4), F(r, 5),
        r.GetString(6), r.GetInt32(7) != 0, r.GetString(8), ParseUtc(r.GetString(9)));

    public IReadOnlyList<GameEventRow> GameEvents() =>
        Query("SELECT id, name, pf, x, y, z, kit, keep_inventory, state, created_utc FROM game_events ORDER BY id DESC;", MapEvent);

    public GameEventRow? GameEvent(long id) =>
        Query("SELECT id, name, pf, x, y, z, kit, keep_inventory, state, created_utc FROM game_events WHERE id=$id;", MapEvent, ("$id", id)).FirstOrDefault();

    public long SaveGameEvent(GameEventRow e)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = e.Id == 0
            ? "INSERT INTO game_events(name, pf, x, y, z, kit, keep_inventory, state, created_utc) VALUES($n, $pf, $x, $y, $z, $k, $keep, $s, $u) RETURNING id;"
            : "UPDATE game_events SET name=$n, pf=$pf, x=$x, y=$y, z=$z, kit=$k, keep_inventory=$keep, state=$s WHERE id=$id RETURNING id;";
        cmd.Parameters.AddWithValue("$n", e.Name);
        cmd.Parameters.AddWithValue("$pf", e.Playfield);
        cmd.Parameters.AddWithValue("$x", e.X);
        cmd.Parameters.AddWithValue("$y", e.Y);
        cmd.Parameters.AddWithValue("$z", e.Z);
        cmd.Parameters.AddWithValue("$k", e.Kit);
        cmd.Parameters.AddWithValue("$keep", e.KeepInventory ? 1 : 0);
        cmd.Parameters.AddWithValue("$s", e.State);
        cmd.Parameters.AddWithValue("$u", Utc(e.CreatedUtc));
        cmd.Parameters.AddWithValue("$id", e.Id);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    public void DeleteGameEvent(long id)
    {
        Exec("DELETE FROM game_event_players WHERE event_id=$id;", ("$id", id));
        Exec("DELETE FROM game_events WHERE id=$id;", ("$id", id));
    }

    public IReadOnlyList<GameEventPlayer> GameEventPlayers(long? eventId = null, string? steamId = null) =>
        Query("""
            SELECT event_id, steam_id, name, state, saved, back_pf, back_x, back_y, back_z FROM game_event_players
            WHERE ($e IS NULL OR event_id=$e) AND ($s IS NULL OR steam_id=$s) ORDER BY name;
            """,
            r => new GameEventPlayer(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), S(r, 4), S(r, 5), F(r, 6), F(r, 7), F(r, 8)),
            ("$e", eventId), ("$s", steamId));

    public void SaveGameEventPlayer(GameEventPlayer p) =>
        Exec("""
            INSERT INTO game_event_players(event_id, steam_id, name, state, saved, back_pf, back_x, back_y, back_z) VALUES($e, $s, $n, $st, $sv, $pf, $x, $y, $z)
            ON CONFLICT(event_id, steam_id) DO UPDATE SET name=$n, state=$st, saved=$sv, back_pf=$pf, back_x=$x, back_y=$y, back_z=$z;
            """, ("$e", p.EventId), ("$s", p.SteamId), ("$n", p.Name), ("$st", p.State), ("$sv", p.Saved), ("$pf", p.BackPlayfield),
            ("$x", p.BackX), ("$y", p.BackY), ("$z", p.BackZ));

    public void DeleteGameEventPlayer(long eventId, string steamId) =>
        Exec("DELETE FROM game_event_players WHERE event_id=$e AND steam_id=$s;", ("$e", eventId), ("$s", steamId));

    // ------------------------------------------------------------------ maintenance

    /// <summary>Rebuilds the database file to give back unused space.</summary>
    public long Compact()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); VACUUM;";
        cmd.ExecuteNonQuery();
        return new FileInfo(c.DataSource).Length;
    }

    /// <summary>"Fresh start" for a new season: clears the history of the old game, keeps settings and tasks.</summary>
    public void FreshStart(bool keepChat)
    {
        Exec($"""
            DELETE FROM structures; DELETE FROM structure_events; DELETE FROM positions; DELETE FROM journal;
            DELETE FROM counters; DELETE FROM miners; DELETE FROM game_event_players; DELETE FROM game_events;
            DELETE FROM player_events; DELETE FROM offline_messages; DELETE FROM players;
            DELETE FROM inventory_snapshots; DELETE FROM player_meta; DELETE FROM saved_positions;
            {(keepChat ? "" : "DELETE FROM chat;")}
            """);
    }
}
