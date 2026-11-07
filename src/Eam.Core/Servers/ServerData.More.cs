using System.Security.Cryptography;
using System.Text;

namespace Eam.Core.Servers;

public sealed record InventorySnapshot(long Id, string SteamId, string Name, DateTime Utc, string Reason, string? Playfield, string Toolbelt, string Bag);
public sealed record PlayerMeta(string SteamId, string? Note, bool Marked, bool BanExempt, bool God);
public sealed record InventoryTemplate(string Name, string Toolbelt, string Bag, DateTime Utc);
public sealed record SavedPosition(string Name, string Playfield, double X, double Y, double Z);

/// <summary>Version 0.9.5: inventory history, player notes and flags, inventory templates, saved positions.</summary>
public sealed partial class ServerData
{
    public const int InventoryRetentionDaysDefault = 14;

    private static string Hash(string a, string b) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(a + "|" + b)));

    private static InventorySnapshot MapSnapshot(Microsoft.Data.Sqlite.SqliteDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetString(2), ParseUtc(r.GetString(3)), r.GetString(4), S(r, 5), r.GetString(6), r.GetString(7));

    private const string SnapshotCols = "id, steam_id, name, utc, reason, pf, toolbelt, bag";

    /// <summary>Stores the inventory unless it is the same as the last one (periodic snapshots only). Returns the new id, or 0.</summary>
    public long AddInventorySnapshot(string steamId, string name, string reason, string? playfield, string toolbeltJson, string bagJson, DateTime utc)
    {
        var hash = Hash(toolbeltJson, bagJson);
        if (reason == "periodic")
        {
            var last = Query("SELECT hash FROM inventory_snapshots WHERE steam_id=$s ORDER BY id DESC LIMIT 1;", r => r.GetString(0), ("$s", steamId)).FirstOrDefault();
            if (last == hash) return 0;
        }
        return Query("""
            INSERT INTO inventory_snapshots(steam_id, name, utc, reason, pf, toolbelt, bag, hash) VALUES($s, $n, $u, $r, $p, $t, $b, $h) RETURNING id;
            """, r => r.GetInt64(0), ("$s", steamId), ("$n", name), ("$u", Utc(utc)), ("$r", reason), ("$p", playfield), ("$t", toolbeltJson), ("$b", bagJson), ("$h", hash)).First();
    }

    public InventorySnapshot? LastInventorySnapshot(string steamId) =>
        Query($"SELECT {SnapshotCols} FROM inventory_snapshots WHERE steam_id=$s ORDER BY id DESC LIMIT 1;", MapSnapshot, ("$s", steamId)).FirstOrDefault();

    public InventorySnapshot? InventorySnapshotById(long id) =>
        Query($"SELECT {SnapshotCols} FROM inventory_snapshots WHERE id=$i;", MapSnapshot, ("$i", id)).FirstOrDefault();

    public IReadOnlyList<InventorySnapshot> InventorySnapshots(string? steamId, int limit = 200, string? reason = null) =>
        Query($"SELECT {SnapshotCols} FROM inventory_snapshots WHERE ($s IS NULL OR steam_id=$s) AND ($r IS NULL OR reason=$r) ORDER BY id DESC LIMIT $l;",
            MapSnapshot, ("$s", steamId), ("$r", reason), ("$l", limit));

    public int PruneInventorySnapshots(int days) =>
        Exec("DELETE FROM inventory_snapshots WHERE utc < $u AND reason <> 'kept';", ("$u", Utc(DateTime.UtcNow.AddDays(-Math.Max(1, days)))));

    public void KeepInventorySnapshot(long id, bool keep) =>
        Exec("UPDATE inventory_snapshots SET reason = CASE WHEN $k THEN 'kept' ELSE 'manual' END WHERE id=$i;", ("$k", keep), ("$i", id));

    // ------------------------------------------------------------------ player notes and flags

    public PlayerMeta Meta(string steamId) =>
        Query("SELECT steam_id, note, marked, ban_exempt, god FROM player_meta WHERE steam_id=$s;",
            r => new PlayerMeta(r.GetString(0), S(r, 1), r.GetInt32(2) != 0, r.GetInt32(3) != 0, r.GetInt32(4) != 0), ("$s", steamId)).FirstOrDefault()
        ?? new PlayerMeta(steamId, null, false, false, false);

    public IReadOnlyList<PlayerMeta> Metas() =>
        Query("SELECT steam_id, note, marked, ban_exempt, god FROM player_meta;",
            r => new PlayerMeta(r.GetString(0), S(r, 1), r.GetInt32(2) != 0, r.GetInt32(3) != 0, r.GetInt32(4) != 0));

    public void SaveMeta(PlayerMeta m) => Exec("""
        INSERT INTO player_meta(steam_id, note, marked, ban_exempt, god, updated_utc) VALUES($s, $n, $m, $b, $g, $u)
        ON CONFLICT(steam_id) DO UPDATE SET note=$n, marked=$m, ban_exempt=$b, god=$g, updated_utc=$u;
        """, ("$s", m.SteamId), ("$n", string.IsNullOrWhiteSpace(m.Note) ? null : m.Note.Trim()), ("$m", m.Marked ? 1 : 0), ("$b", m.BanExempt ? 1 : 0),
        ("$g", m.God ? 1 : 0), ("$u", Utc(DateTime.UtcNow)));

    public void ResetPlaytime(string steamId) => Exec("UPDATE players SET playtime_sec=0 WHERE steam_id=$s;", ("$s", steamId));

    /// <summary>Forgets players not seen for this many days (EAM's data only; their game files are not touched).</summary>
    public int ForgetInactivePlayers(int days)
    {
        var limit = Utc(DateTime.UtcNow.AddDays(-Math.Max(1, days)));
        return Exec("""
            DELETE FROM inventory_snapshots WHERE steam_id IN (SELECT steam_id FROM players WHERE last_seen_utc < $u) AND reason <> 'kept';
            DELETE FROM players WHERE last_seen_utc < $u AND steam_id NOT IN (SELECT steam_id FROM player_meta WHERE marked=1);
            """, ("$u", limit));
    }

    // ------------------------------------------------------------------ inventory templates

    public IReadOnlyList<InventoryTemplate> InventoryTemplates() =>
        Query("SELECT name, toolbelt, bag, utc FROM inventory_templates ORDER BY name;", r => new InventoryTemplate(r.GetString(0), r.GetString(1), r.GetString(2), ParseUtc(r.GetString(3))));

    public void SaveInventoryTemplate(string name, string toolbelt, string bag) => Exec("""
        INSERT INTO inventory_templates(name, toolbelt, bag, utc) VALUES($n, $t, $b, $u) ON CONFLICT(name) DO UPDATE SET toolbelt=$t, bag=$b, utc=$u;
        """, ("$n", name), ("$t", toolbelt), ("$b", bag), ("$u", Utc(DateTime.UtcNow)));

    public void DeleteInventoryTemplate(string name) => Exec("DELETE FROM inventory_templates WHERE name=$n;", ("$n", name));

    // ------------------------------------------------------------------ saved positions

    public IReadOnlyList<SavedPosition> SavedPositions() =>
        Query("SELECT name, pf, x, y, z FROM saved_positions ORDER BY name;", r => new SavedPosition(r.GetString(0), r.GetString(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4)));

    public void SavePosition(SavedPosition p) => Exec("""
        INSERT INTO saved_positions(name, pf, x, y, z, utc) VALUES($n, $p, $x, $y, $z, $u) ON CONFLICT(name) DO UPDATE SET pf=$p, x=$x, y=$y, z=$z, utc=$u;
        """, ("$n", p.Name), ("$p", p.Playfield), ("$x", p.X), ("$y", p.Y), ("$z", p.Z), ("$u", Utc(DateTime.UtcNow)));

    public void DeletePosition(string name) => Exec("DELETE FROM saved_positions WHERE name=$n;", ("$n", name));

    /// <summary>When each structure last changed playfield (structure id → time).</summary>
    public Dictionary<int, DateTime> LastPlayfieldChanges() =>
        Query("SELECT structure_id, MAX(utc) FROM structure_events WHERE kind='playfield' GROUP BY structure_id;", r => (r.GetInt32(0), ParseUtc(r.GetString(1))))
            .ToDictionary(x => x.Item1, x => x.Item2);

    /// <summary>The owner before the last owner change ("group:id"), if EAM saw one.</summary>
    public string? PreviousOwner(int structureId) =>
        Query("SELECT old FROM structure_events WHERE structure_id=$s AND kind='owner' ORDER BY id DESC LIMIT 1;", r => S(r, 0), ("$s", structureId)).FirstOrDefault();
}
