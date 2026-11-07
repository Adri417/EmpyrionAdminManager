using System.Globalization;
using Eam.Core.Data;

namespace Eam.Core.Servers;

public sealed record ChatEntry(long Id, DateTime Utc, string Channel, int SenderId, string SenderName, string? SenderSteamId,
    int RecipientId, string Text, string Source);

public sealed record SeenPlayer(string SteamId, int EntityId, string Name, DateTime FirstSeenUtc, DateTime LastSeenUtc, long PlaytimeSeconds);

/// <summary>The separate database of one game server (eam\server.db in the server's folder).</summary>
public sealed class ServerData
{
    public const int ChatRetentionDays = 90;

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

    private void Prune()
    {
        using var c = _db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM chat WHERE utc < $limit;";
        cmd.Parameters.AddWithValue("$limit", DateTime.UtcNow.AddDays(-ChatRetentionDays).ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static DateTime ParseUtc(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
