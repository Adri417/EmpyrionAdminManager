using Eam.Core.Servers;
using Microsoft.Data.Sqlite;

namespace Eam.Tests;

public class GlobalDbTests
{
    [Fact]
    public void ReadsStructuresWhileTheGameKeepsTheDatabaseOpen()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "global.db");
        // The game's own connection: WAL, kept open and writing during the read.
        using var game = new SqliteConnection($"Data Source={file};Pooling=False");
        game.Open();
        using (var c = game.CreateCommand())
        {
            c.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE SolarSystems (ssid INTEGER PRIMARY KEY, name TEXT UNIQUE, startype TEXT NOT NULL, sectorx INTEGER, sectory INTEGER, sectorz INTEGER);
                CREATE TABLE Playfields (ssid INTEGER, pfid INTEGER PRIMARY KEY, pftype INTEGER, planettype TEXT, name TEXT UNIQUE);
                CREATE TABLE Entities (entityid INTEGER PRIMARY KEY, pfid INTEGER NOT NULL, name TEXT, etype INTEGER, facgroup INTEGER, facid INTEGER,
                    posx REAL, posy REAL, posz REAL, isstructure BOOL, isremoved BOOL DEFAULT 0, ispoi BOOL);
                CREATE TABLE Structures (entityid INTEGER PRIMARY KEY, lastvisitedticks INTEGER, cntblocks INTEGER, cntdevices INTEGER,
                    classnr INTEGER, ispowered BOOL, coretype INTEGER);
                INSERT INTO SolarSystems VALUES (1, 'Akua', 'G', 0, 0, 0);
                INSERT INTO Playfields VALUES (1, 10, 1, 'Temperate', 'Akua'), (1, 11, 2, 'Space', 'Akua Sector');
                INSERT INTO Entities VALUES (80205, 10, 'BrikoDepot', 2, 1, 59030, -3356.0, -6483.8, -1477.0, 1, 0, 0),
                                            (96003, 11, 'RE2 Rook', 3, 0, 101, 10813.8, -804.0, 2719.3, 1, 0, 0),
                                            (5, 10, 'Gone', 2, 0, 101, 0, 0, 0, 1, 1, 0),
                                            (6, 10, 'Rock', 7, 255, 0, 0, 0, 0, 1, 0, 0),
                                            (7, 10, 'Player', 1, 1, 7, 0, 0, 0, 0, 0, 0);
                INSERT INTO Structures VALUES (80205, 9892000, 4535, 333, 4, 1, 1), (96003, 9895056, 2070, 272, 3, 0, 1), (5, 1, 1, 1, 1, 0, 0), (6, 1, 1, 0, 0, 0, 0);
                """;
            c.ExecuteNonQuery();
        }
        using var tx = game.BeginTransaction();
        using (var w = game.CreateCommand()) { w.Transaction = tx; w.CommandText = "UPDATE Structures SET lastvisitedticks = 9900000 WHERE entityid = 96003;"; w.ExecuteNonQuery(); }

        var list = GlobalDb.Structures(file); // reads while the game holds a write transaction
        Assert.Equal(2, list.Count);
        var b = Assert.Single(list, x => x.Id == 80205);
        Assert.Equal(("BrikoDepot", 2, "Akua", "Akua", 1, 59030, 4535, 333, 4, true, 9892000L),
            (b.Name, b.Type, b.Playfield, b.SolarSystem, b.FactionGroup, b.FactionId, b.Blocks, b.Devices, b.Class, b.Powered, b.LastVisitedTicks));
        Assert.Equal(9895056, list.Single(x => x.Id == 96003).LastVisitedTicks); // uncommitted change not seen
        tx.Commit();
        Assert.Equal(9900000, GlobalDb.Structures(file).Single(x => x.Id == 96003).LastVisitedTicks);
        game.Close();
    }
}
