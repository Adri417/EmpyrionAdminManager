using System.Globalization;
using Eam.Core.Data;

namespace Eam.Core.Servers;

public sealed record ChatEntry(long Id, DateTime Utc, string Channel, int SenderId, string SenderName, string? SenderSteamId,
    int RecipientId, string Text, string Source);

public sealed record ScheduledTask(long Id, string Kind, bool Enabled, string Schedule, string Params, DateTime? LastRunUtc, string? LastResult);

public sealed record PlayerEventEntry(long Id, DateTime Utc, string Kind, string? Detail, string? By);

public sealed record SeenPlayer(string SteamId, int EntityId, string Name, DateTime FirstSeenUtc, DateTime LastSeenUtc, long PlaytimeSeconds);

/// <summary>The separate database of one game server (eam\server.db in the server's folder).</summary>
public sealed partial class ServerData
{
    public const int ChatRetentionDays = 90;
    public const int PositionRetentionDays = 14;

    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE chat (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            utc          TEXT NOT NULL,
            channel      TEXT NOT NULL,
            sender_id    INTEGER NOT NULL,
            sender_name  TEXT NOT NULL,
            sender_steam TEXT NULL,
            recipient_id INTEGER NOT NULL DEFAULT -1,
            text         TEXT NOT NULL,
            source       TEXT NOT NULL
        );
        CREATE INDEX chat_utc ON chat(utc);
        CREATE TABLE players (
            steam_id       TEXT PRIMARY KEY,
            entity_id      INTEGER NOT NULL,
            name           TEXT NOT NULL,
            first_seen_utc TEXT NOT NULL,
            last_seen_utc  TEXT NOT NULL,
            playtime_sec   INTEGER NOT NULL DEFAULT 0
        );
        """,
        // 2: settings, scheduled tasks, player history and offline messages (steps 4 and 5)
        """
        CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE tasks (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            kind        TEXT NOT NULL,
            enabled     INTEGER NOT NULL DEFAULT 1,
            schedule    TEXT NOT NULL,
            params      TEXT NOT NULL DEFAULT '{}',
            last_run_utc TEXT NULL,
            last_result TEXT NULL
        );
        CREATE TABLE player_events (
            id        INTEGER PRIMARY KEY AUTOINCREMENT,
            utc       TEXT NOT NULL,
            steam_id  TEXT NOT NULL,
            name      TEXT NOT NULL,
            kind      TEXT NOT NULL,
            detail    TEXT NULL,
            by_user   TEXT NULL
        );
        CREATE INDEX player_events_steam ON player_events(steam_id, id);
        CREATE TABLE offline_messages (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            steam_id      TEXT NOT NULL,
            text          TEXT NOT NULL,
            created_utc   TEXT NOT NULL,
            by_user       TEXT NOT NULL,
            delivered_utc TEXT NULL
        );
        """,
        // 3: steps 6 to 9 (structures, history, journal, statistics, chat moderation, gameplay)
        """
        CREATE TABLE structures (
            id INTEGER PRIMARY KEY, name TEXT NOT NULL, type INTEGER NOT NULL, pf TEXT NOT NULL,
            faction_group INTEGER NOT NULL, faction_id INTEGER NOT NULL, x REAL, y REAL, z REAL,
            blocks INTEGER, devices INTEGER, class INTEGER, core INTEGER, powered INTEGER, last_visited_utc TEXT NULL,
            solar_system TEXT NULL, first_seen_utc TEXT NOT NULL, last_seen_utc TEXT NOT NULL, removed_utc TEXT NULL,
            warned_utc TEXT NULL
        );
        CREATE TABLE structure_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT, utc TEXT NOT NULL, structure_id INTEGER NOT NULL,
            kind TEXT NOT NULL, old TEXT NULL, new TEXT NULL, by_user TEXT NULL
        );
        CREATE INDEX structure_events_sid ON structure_events(structure_id, id);
        CREATE TABLE positions (
            utc TEXT NOT NULL, kind TEXT NOT NULL, ref TEXT NOT NULL, name TEXT NOT NULL, pf TEXT NOT NULL,
            x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL
        );
        CREATE INDEX positions_pf ON positions(pf, utc);
        CREATE INDEX positions_ref ON positions(ref, utc);
        CREATE TABLE journal (
            id INTEGER PRIMARY KEY AUTOINCREMENT, utc TEXT NOT NULL, category TEXT NOT NULL, severity INTEGER NOT NULL,
            steam_id TEXT NULL, name TEXT NULL, pf TEXT NULL, text TEXT NOT NULL
        );
        CREATE INDEX journal_cat ON journal(category, id);
        CREATE TABLE stats (
            utc TEXT NOT NULL, scope TEXT NOT NULL, cpu REAL, mem REAL, players INTEGER, playfields INTEGER, fps REAL, structs INTEGER
        );
        CREATE INDEX stats_utc ON stats(scope, utc);
        CREATE TABLE counters (
            steam_id TEXT NOT NULL, key TEXT NOT NULL, count INTEGER NOT NULL DEFAULT 0, last_utc TEXT NOT NULL, value TEXT NULL,
            PRIMARY KEY (steam_id, key)
        );
        CREATE TABLE miners (
            steam_id TEXT NOT NULL, ore TEXT NOT NULL, level INTEGER NOT NULL, stored REAL NOT NULL DEFAULT 0,
            fuel_hours REAL NOT NULL DEFAULT 0, updated_utc TEXT NOT NULL, PRIMARY KEY (steam_id, ore)
        );
        CREATE TABLE game_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, pf TEXT NOT NULL, x REAL, y REAL, z REAL,
            kit TEXT NOT NULL DEFAULT '[]', keep_inventory INTEGER NOT NULL DEFAULT 0, state TEXT NOT NULL, created_utc TEXT NOT NULL
        );
        CREATE TABLE game_event_players (
            event_id INTEGER NOT NULL, steam_id TEXT NOT NULL, name TEXT NOT NULL, state TEXT NOT NULL,
            saved TEXT NULL, back_pf TEXT NULL, back_x REAL, back_y REAL, back_z REAL, PRIMARY KEY (event_id, steam_id)
        );
        """,
    ];

    private readonly Database _db;

    public ServerData(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        _db = new Database(file, Migrations, pooling: false); // no file kept open: the server folder can be renamed or moved
        _db.Migrate();
        Prune();
    }

    public ChatEntry AddChat(DateTime utc, string channel, int senderId, string senderName, string? senderSteam, int recipientId, string text, string source)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chat (utc, channel, sender_id, sender_name, sender_steam, recipient_id, text, source)
            VALUES ($u, $c, $s, $n, $st, $r, $t, $src) RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$u", utc.ToString("O"));
        cmd.Parameters.AddWithValue("$c", channel);
        cmd.Parameters.AddWithValue("$s", senderId);
        cmd.Parameters.AddWithValue("$n", senderName);
        cmd.Parameters.AddWithValue("$st", (object?)senderSteam ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$r", recipientId);
        cmd.Parameters.AddWithValue("$t", text);
        cmd.Parameters.AddWithValue("$src", source);
        var id = Convert.ToInt64(cmd.ExecuteScalar());
        return new ChatEntry(id, utc, channel, senderId, senderName, senderSteam, recipientId, text, source);
    }

    /// <summary>Latest messages, oldest first. <paramref name="beforeId"/> pages back in history.</summary>
    public IReadOnlyList<ChatEntry> RecentChat(int limit, long? beforeId = null)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, utc, channel, sender_id, sender_name, sender_steam, recipient_id, text, source FROM chat
            WHERE ($b IS NULL OR id < $b) ORDER BY id DESC LIMIT $l;
            """;
        cmd.Parameters.AddWithValue("$b", (object?)beforeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$l", Math.Clamp(limit, 1, 500));
        using var r = cmd.ExecuteReader();
        var list = new List<ChatEntry>();
        while (r.Read())
            list.Add(new ChatEntry(r.GetInt64(0), ParseUtc(r.GetString(1)), r.GetString(2), r.GetInt32(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetInt32(6), r.GetString(7), r.GetString(8)));
        list.Reverse();
        return list;
    }

    /// <summary>Records a player as seen now; <paramref name="addSeconds"/> adds online time.</summary>
    public void PlayerSeen(string steamId, int entityId, string name, long addSeconds = 0)
    {
        if (string.IsNullOrEmpty(steamId)) return;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO players (steam_id, entity_id, name, first_seen_utc, last_seen_utc, playtime_sec)
            VALUES ($s, $e, $n, $now, $now, $add)
            ON CONFLICT(steam_id) DO UPDATE SET entity_id=$e, name=$n, last_seen_utc=$now, playtime_sec=playtime_sec+$add;
            """;
        cmd.Parameters.AddWithValue("$s", steamId);
        cmd.Parameters.AddWithValue("$e", entityId);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$add", Math.Max(0, addSeconds));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<SeenPlayer> Players()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT steam_id, entity_id, name, first_seen_utc, last_seen_utc, playtime_sec FROM players ORDER BY last_seen_utc DESC;";
        using var r = cmd.ExecuteReader();
        var list = new List<SeenPlayer>();
        while (r.Read())
            list.Add(new SeenPlayer(r.GetString(0), r.GetInt32(1), r.GetString(2), ParseUtc(r.GetString(3)), ParseUtc(r.GetString(4)), r.GetInt64(5)));
        return list;
    }

    // ------------------------------------------------------------------ settings

    public string? Setting(string key)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key=$k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string? value)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = value is null ? "DELETE FROM settings WHERE key=$k;" : "INSERT INTO settings(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value=$v;";
        cmd.Parameters.AddWithValue("$k", key);
        if (value is not null) cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ scheduled tasks

    public IReadOnlyList<ScheduledTask> Tasks()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, kind, enabled, schedule, params, last_run_utc, last_result FROM tasks ORDER BY id;";
        using var r = cmd.ExecuteReader();
        var list = new List<ScheduledTask>();
        while (r.Read())
            list.Add(new ScheduledTask(r.GetInt64(0), r.GetString(1), r.GetInt32(2) != 0, r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : ParseUtc(r.GetString(5)), r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    public long SaveTask(ScheduledTask t)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = t.Id == 0
            ? "INSERT INTO tasks(kind, enabled, schedule, params) VALUES($k, $e, $s, $p) RETURNING id;"
            : "UPDATE tasks SET kind=$k, enabled=$e, schedule=$s, params=$p WHERE id=$id RETURNING id;";
        cmd.Parameters.AddWithValue("$k", t.Kind);
        cmd.Parameters.AddWithValue("$e", t.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$s", t.Schedule);
        cmd.Parameters.AddWithValue("$p", t.Params);
        cmd.Parameters.AddWithValue("$id", t.Id);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    public void DeleteTask(long id)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM tasks WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void TaskRan(long id, DateTime utc, string result)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE tasks SET last_run_utc=$u, last_result=$r WHERE id=$id;";
        cmd.Parameters.AddWithValue("$u", utc.ToString("O"));
        cmd.Parameters.AddWithValue("$r", result);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ player history and offline messages

    public void PlayerEvent(string steamId, string name, string kind, string? detail = null, string? by = null)
    {
        if (string.IsNullOrEmpty(steamId)) return;
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO player_events(utc, steam_id, name, kind, detail, by_user) VALUES($u, $s, $n, $k, $d, $b);";
        cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$s", steamId);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$d", (object?)detail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$b", (object?)by ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<PlayerEventEntry> PlayerEvents(string steamId, int limit = 200)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, utc, kind, detail, by_user FROM player_events WHERE steam_id=$s ORDER BY id DESC LIMIT $l;";
        cmd.Parameters.AddWithValue("$s", steamId);
        cmd.Parameters.AddWithValue("$l", Math.Clamp(limit, 1, 1000));
        using var r = cmd.ExecuteReader();
        var list = new List<PlayerEventEntry>();
        while (r.Read()) list.Add(new PlayerEventEntry(r.GetInt64(0), ParseUtc(r.GetString(1)), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    public IReadOnlyList<ChatEntry> PlayerChat(string steamId, int limit = 200)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, utc, channel, sender_id, sender_name, sender_steam, recipient_id, text, source FROM chat
            WHERE sender_steam=$s ORDER BY id DESC LIMIT $l;
            """;
        cmd.Parameters.AddWithValue("$s", steamId);
        cmd.Parameters.AddWithValue("$l", Math.Clamp(limit, 1, 1000));
        using var r = cmd.ExecuteReader();
        var list = new List<ChatEntry>();
        while (r.Read())
            list.Add(new ChatEntry(r.GetInt64(0), ParseUtc(r.GetString(1)), r.GetString(2), r.GetInt32(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetInt32(6), r.GetString(7), r.GetString(8)));
        return list;
    }

    public void AddOfflineMessage(string steamId, string text, string by)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO offline_messages(steam_id, text, created_utc, by_user) VALUES($s, $t, $u, $b);";
        cmd.Parameters.AddWithValue("$s", steamId);
        cmd.Parameters.AddWithValue("$t", text);
        cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$b", by);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Messages waiting for this player; marked as delivered.</summary>
    public IReadOnlyList<string> TakeOfflineMessages(string steamId)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, text FROM offline_messages WHERE steam_id=$s AND delivered_utc IS NULL ORDER BY id;";
        cmd.Parameters.AddWithValue("$s", steamId);
        var list = new List<(long, string)>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1)));
        foreach (var (id, _) in list)
        {
            using var u = c.CreateCommand();
            u.Transaction = tx;
            u.CommandText = "UPDATE offline_messages SET delivered_utc=$d WHERE id=$id;";
            u.Parameters.AddWithValue("$d", DateTime.UtcNow.ToString("O"));
            u.Parameters.AddWithValue("$id", id);
            u.ExecuteNonQuery();
        }
        tx.Commit();
        return list.Select(x => x.Item2).ToList();
    }

    public int PendingOfflineMessages(string steamId)
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM offline_messages WHERE steam_id=$s AND delivered_utc IS NULL;";
        cmd.Parameters.AddWithValue("$s", steamId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Consistent copy of this database (SQLite backup API), safe while EAM keeps writing.</summary>
    public void SnapshotTo(string file)
    {
        using var src = _db.Open();
        using var dst = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={file};Pooling=False");
        dst.Open();
        src.BackupDatabase(dst);
    }

    private void Prune()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            DELETE FROM chat WHERE utc < $limit;
            DELETE FROM positions WHERE utc < $pos;
            DELETE FROM stats WHERE utc < $pos;
            DELETE FROM journal WHERE utc < $limit;
            """;
        cmd.Parameters.AddWithValue("$pos", DateTime.UtcNow.AddDays(-PositionRetentionDays).ToString("O"));
        cmd.Parameters.AddWithValue("$limit", DateTime.UtcNow.AddDays(-ChatRetentionDays).ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static DateTime ParseUtc(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
