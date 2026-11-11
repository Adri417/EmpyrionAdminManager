using Microsoft.Data.Sqlite;

namespace Eam.Core.Servers;

/// <summary>One structure as stored by the game in its save database (global.db).</summary>
public sealed record GlobalDbStructure(
    int Id, string Name, int Type, string Playfield, string? SolarSystem, int FactionGroup, int FactionId,
    double X, double Y, double Z, int Blocks, int Devices, int Class, int Core, bool Powered, long LastVisitedTicks);

/// <summary>
/// Reads the save database of the game (Saves\Games\&lt;game&gt;\global.db) directly, read-only.
/// The game is not involved at all: unlike the mod's global structure list, which blocks the whole server for
/// several seconds on a big universe, reading this file has no effect on the players.
/// </summary>
public static class GlobalDb
{
    /// <summary>Saves\Games\&lt;game&gt; of the save the server plays (null when unknown).</summary>
    public static string? SaveGameDir(ServerDefinition def)
    {
        try
        {
            if (!System.IO.File.Exists(def.YamlFile)) return null;
            var y = DedicatedYaml.Load(def.YamlFile);
            var game = y.Get("GameConfig", "GameName");
            if (string.IsNullOrWhiteSpace(game)) return null;
            var saves = y.Get("GameConfig", "SaveDirectory") ?? y.Get("ServerConfig", "SaveDirectory");
            if (string.IsNullOrWhiteSpace(saves)) saves = Path.Combine(def.GameDir, "Saves");
            else if (!Path.IsPathFullyQualified(saves)) saves = Path.Combine(def.GameDir, saves);
            var dir = Path.Combine(saves, "Games", game);
            return Directory.Exists(dir) ? dir : null;
        }
        catch { return null; }
    }

    public static string? File(ServerDefinition def) =>
        SaveGameDir(def) is { } dir && System.IO.File.Exists(Path.Combine(dir, "global.db")) ? Path.Combine(dir, "global.db") : null;

    /// <summary>
    /// Every structure (bases, ships) not removed. One short read-only query: the game keeps writing meanwhile
    /// (the database is in WAL mode, a reader never blocks it). Throws when the file cannot be read.
    /// </summary>
    public static List<GlobalDbStructure> Structures(string file)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = file, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, Pooling = false, DefaultTimeout = 5,
        }.ToString();
        var list = new List<GlobalDbStructure>();
        using var c = new SqliteConnection(cs);
        c.Open();
        using (var pragma = c.CreateCommand())
        {
            pragma.CommandText = "PRAGMA query_only = 1;";
            pragma.ExecuteNonQuery();
        }
        using var q = c.CreateCommand();
        q.CommandText = """
            SELECT e.entityid, e.name, e.etype, p.name, ss.name, e.facgroup, e.facid, e.posx, e.posy, e.posz,
                   s.cntblocks, s.cntdevices, s.classnr, s.coretype, s.ispowered, s.lastvisitedticks
            FROM Entities e
            JOIN Structures s ON s.entityid = e.entityid
            JOIN Playfields p ON p.pfid = e.pfid
            LEFT JOIN SolarSystems ss ON ss.ssid = p.ssid
            WHERE e.isstructure = 1 AND IFNULL(e.isremoved, 0) = 0 AND e.etype IN (2, 3, 4, 5);
            """;
        using var r = q.ExecuteReader();
        static int I(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : (int)r.GetInt64(i);
        static double D(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetDouble(i);
        while (r.Read())
            list.Add(new GlobalDbStructure(I(r, 0), r.IsDBNull(1) ? "" : r.GetString(1), I(r, 2), r.IsDBNull(3) ? "" : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), I(r, 5), I(r, 6), D(r, 7), D(r, 8), D(r, 9), I(r, 10), I(r, 11), I(r, 12), I(r, 13),
                !r.IsDBNull(14) && r.GetInt64(14) != 0, r.IsDBNull(15) ? 0 : r.GetInt64(15)));
        return list;
    }
}
