using System.Text;
using System.Text.Json;
using Eam.Core.Servers;
using Eam.Core.Servers.Eah;
using Microsoft.Data.Sqlite;

namespace Eam.Tests;

/// <summary>Builds a fake EAH installation (encrypted databases + XML settings) and imports it.</summary>
public class EahImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eam-eah-" + Guid.NewGuid().ToString("N")[..8]);
    private string Config => Path.Combine(_root, "DedicatedServer", "EmpyrionAdminHelper", "Config");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch { }
    }

    private void Db(string file, string sql)
    {
        Directory.CreateDirectory(Config);
        var plain = Path.Combine(_root, "plain-" + file);
        using (var c = new SqliteConnection($"Data Source={plain};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        File.WriteAllBytes(Path.Combine(Config, file), EahReader.EncryptForTests(File.ReadAllBytes(plain)));
    }

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private void BuildFakeEah()
    {
        var now = DateTime.UtcNow;
        Directory.CreateDirectory(Path.Combine(_root, "DedicatedServer"));
        File.WriteAllText(Path.Combine(_root, "DedicatedServer", "EmpyrionDedicated.exe"), "");
        File.WriteAllText(Path.Combine(_root, "dedicated.yaml"), "ServerConfig:\n  Srv_Port: 31564\n  Srv_Name: Old EAH server\n  Tel_Port: 12350\n  Tel_Pwd: secret12\n");
        Db("EAH_Ply.dat", $"""
            CREATE TABLE clsPlayers ([Key] integer PRIMARY KEY, ID integer, Name text, Private_ID integer, Playtime integer, Notes text, Remember bit, HinderAutoBan bit, Creation datetime, Last_Activity datetime);
            INSERT INTO clsPlayers(ID, Name, Private_ID, Playtime, Notes, Remember, HinderAutoBan, Creation, Last_Activity)
              VALUES (76561198000000011, 'Nova', 1201, {TimeSpan.FromHours(3).Ticks}, 'Trusted builder', 1, 1, '{Local(now.AddDays(-20))}', '{Local(now.AddHours(-1))}');
            INSERT INTO clsPlayers(ID, Name, Private_ID, Playtime, Creation, Last_Activity) VALUES (76561198000000012, 'Kestrel', 1202, 0, '{Local(now.AddDays(-2))}', '{Local(now.AddHours(-5))}');
            """);
        var inv = EahReader.EncodeInventoryForTests([(1, 4101, 1), (3, 4300, 50), (10, 2248, 999), (12, 4400, 5)]);
        Db("EAH_Log.dat", $"""
            CREATE TABLE clsChats ([Key] integer PRIMARY KEY, DateT datetime, Faction text, Name text, Text text, Translation text, Color text, Reciever int);
            INSERT INTO clsChats(DateT, Name, Text, Reciever) VALUES ('{Local(now.AddHours(-2))}', 'Nova', 'hello from EAH', 0), ('{Local(now.AddDays(-200))}', 'Nova', 'too old', 0);
            CREATE TABLE clsBackpack_Backups ([Key] integer PRIMARY KEY, DateT datetime, SteamID integer, Name text, Warning int, Inventory text);
            INSERT INTO clsBackpack_Backups(DateT, SteamID, Name, Warning, Inventory) VALUES ('{Local(now.AddHours(-3))}', 76561198000000011, 'Nova', 0, '{inv}');
            CREATE TABLE clsPlayerActivities ([Key] integer PRIMARY KEY, DateT datetime, SteamID integer, Name text, Activity int, Before text, After text);
            INSERT INTO clsPlayerActivities(DateT, SteamID, Name, Activity, Before, After) VALUES ('{Local(now.AddHours(-4))}', 76561198000000011, 'Nova', 10, 'Akua', 'Omicron');
            CREATE TABLE clsPlayer_Logs ([Key] integer PRIMARY KEY, DateT datetime, SteamID integer, Name text, LogType int, Text text);
            INSERT INTO clsPlayer_Logs(DateT, SteamID, Name, LogType, Text) VALUES ('{Local(now.AddHours(-6))}', 76561198000000012, 'Kestrel', 46, 'kicked for spam');
            """);
        Db("EAH_Struct.dat", $"""
            CREATE TABLE clsStructures ([Key] integer PRIMARY KEY, ID integer, Name text, Typ int, Owner_Typ int, Owner_ID integer, Playfield text, Blocks int, Devices int, StructureClass int, Deleted bit, Created_Date datetime, LastChange datetime, NS real, Height real, EW real);
            INSERT INTO clsStructures(ID, Name, Typ, Owner_Typ, Owner_ID, Playfield, Blocks, Devices, StructureClass, Deleted, Created_Date, LastChange, NS, Height, EW)
              VALUES (5001, 'Iron Haven', 3, 1, 1201, 'Akua', 5000, 200, 9, 0, '{Local(now.AddDays(-10))}', '{Local(now.AddDays(-1))}', 10, 20, 30);
            CREATE TABLE clsStructure_Logs ([Key] integer PRIMARY KEY, ID integer, DateT datetime, Old_Name text, New_Name text, Old_Owner_Typ int, New_Owner_Typ int, Old_Owner_ID integer, New_Owner_ID integer, Old_Playfield text, New_Playfield text);
            INSERT INTO clsStructure_Logs(ID, DateT, Old_Name, New_Name, Old_Owner_Typ, New_Owner_Typ, Old_Owner_ID, New_Owner_ID, Old_Playfield, New_Playfield)
              VALUES (5001, '{Local(now.AddDays(-2))}', 'Base', 'Iron Haven', 1, 1, 1202, 1201, 'Akua', 'Akua');
            """);
        Db("EAH_Dat.dat", """
            CREATE TABLE clsPlayfields ([Key] integer PRIMARY KEY, Name text, MaxPlayerCV int, MaxFactionBase int, MaxClassCV int, MaxBlocksCV int, WipeStructureAfterNotTouchHours int, EntranceMessage text);
            INSERT INTO clsPlayfields(Name, MaxPlayerCV, MaxFactionBase, MaxClassCV, WipeStructureAfterNotTouchHours, EntranceMessage) VALUES ('Akua', 2, 3, 20, 240, 'Welcome on Akua');
            """);
        var token = EahReader.EncryptSecretForTests("MTAxNjQ4.discord-token-1234567890");
        File.WriteAllText(Path.Combine(Config, "Settings.XML"), $"""
            <?xml version="1.0" encoding="utf-16"?>
            <DataStore><Header/><Body><Version>2</Version><Einstellung>
              <MOTD>Have fun</MOTD><Welcome_Message>Welcome!</Welcome_Message><ChatBotOn>true</ChatBotOn>
              <SwearWordFilterOn>true</SwearWordFilterOn><SwearFilter_Words>badword1,badword2</SwearFilter_Words>
              <Discord_Token>{token}</Discord_Token><Discord_Public_Channel>123456789012345678</Discord_Public_Channel>
              <Activate_AutoMiner>true</Activate_AutoMiner><Activate_BackpackBackup>true</Activate_BackpackBackup>
              <Server_Name>Old EAH server</Server_Name><Dedicated>dedicated.yaml</Dedicated>
            </Einstellung></Body></DataStore>
            """, Encoding.Unicode);
        File.WriteAllText(Path.Combine(Config, "TimeTable.XML"), """
            <?xml version="1.0" encoding="utf-16"?>
            <DataStore><Body>
              <TT W="16" A="5" P="" DZ="2026-01-01 04:00:00" AC="true"><TTS A="3"/></TT>
              <TT W="17" A="36" P="Akua,Omicron" DZ="2026-01-01 06:30:00" AC="true"/>
              <TT W="11" A="0" P="Vote for us!" AC="true"/>
              <TT W="16" A="32" P="Akua#Tallodar#Rogue PX-37 Moon 2#" DZ="2026-01-01 04:55:00" AC="true"/>
              <TT W="16" A="31" P="all" DZ="2026-01-01 04:55:00" AC="false"/>
              <TT W="24" A="50" P="" DZ="2026-01-15 03:00:00" AC="true"/>
              <TT W="16" A="47" P="cmd.exe" AC="true"/>
            </Body></DataStore>
            """, Encoding.Unicode);
        File.WriteAllText(Path.Combine(Config, "Chatbot_Option.XML"), """
            <?xml version="1.0" encoding="utf-16"?>
            <DataStore><Body><CBO C="discord" I="Discord link" A="discord.gg/abc" D="false" AC="true"/></Body></DataStore>
            """, Encoding.Unicode);
        var templates = Path.Combine(Config, "..", "Inventory Templates");
        Directory.CreateDirectory(templates);
        File.WriteAllText(Path.Combine(templates, "Starter.xml"), """
            <?xml version="1.0" encoding="utf-16"?>
            <Inventar><Inv P="1" ID="4101" M="1" A="0" H="0"/><Inv P="10" ID="2248" M="100" A="0" H="0"/></Inventar>
            """, Encoding.Unicode);
    }

    [Fact]
    public void FindsConfigFromTheServerFolder()
    {
        BuildFakeEah();
        Assert.Equal(Path.GetFullPath(Config), Path.GetFullPath(EahReader.FindConfig(_root)!));
        Assert.Equal(Path.GetFullPath(_root), Path.GetFullPath(EahReader.FindGameDir(Config)!));
    }

    [Fact]
    public void DecodesBackpacksAndSecrets()
    {
        var (tb, bag) = EahReader.DecodeInventory(EahReader.EncodeInventoryForTests([(1, 4101, 1), (9, 4102, 2), (10, 2248, 999), (49, 7, 3)]));
        Assert.Equal([0, 8], tb.Select(s => s.Slot));
        Assert.Equal([0, 39], bag.Select(s => s.Slot));
        Assert.Equal(999, bag[0].Count);
        Assert.Equal("hello", EahReader.DecryptSecret(EahReader.EncryptSecretForTests("hello")));
        Assert.Null(EahReader.DecryptSecret("zz"));
    }

    [Fact]
    public void ImportsEverything()
    {
        BuildFakeEah();
        var dbFile = Path.Combine(_root, "eam", "server.db");
        var data = new ServerData(dbFile);
        var preview = EahImporter.Preview(Config, Path.Combine(_root, "work1"));
        Assert.Equal(2, preview.Counts["players"]);
        Assert.Equal(1, preview.Counts["backpacks"]);
        Assert.Equal(6, preview.Counts["tasks"]);

        var report = EahImporter.Import(data, Config, Path.Combine(_root, "work2"), new EahImportOptions());
        Assert.False(Directory.Exists(Path.Combine(_root, "work2")));

        var nova = data.Players().Single(p => p.Name == "Nova");
        Assert.Equal("76561198000000011", nova.SteamId);
        Assert.Equal(3 * 3600, nova.PlaytimeSeconds);
        var meta = data.Meta(nova.SteamId);
        Assert.True(meta.Marked && meta.BanExempt);
        Assert.Equal("Trusted builder", meta.Note);

        var chat = data.RecentChat(10);
        Assert.Equal(["hello from EAH"], chat.Select(c => c.Text));
        Assert.Equal(nova.SteamId, chat[0].SenderSteamId);

        var snap = Assert.Single(data.InventorySnapshots(nova.SteamId));
        Assert.Equal("eah", snap.Reason);
        var bag = JsonDocument.Parse(snap.Bag).RootElement;
        Assert.Equal(2, bag.GetArrayLength());
        Assert.Equal(2248, bag[0].GetProperty("id").GetInt32());
        Assert.Equal(2, JsonDocument.Parse(snap.Toolbelt).RootElement[1].GetProperty("slot").GetInt32());

        Assert.Contains(data.PlayerEvents(nova.SteamId), e => e.Kind == "playfield" && e.Detail == "Akua -> Omicron");
        Assert.Contains(data.PlayerEvents("76561198000000012"), e => e.Kind == "kick");

        var st = data.Structure(5001)!;
        Assert.Equal(EntityTypes.BA, st.Type);
        Assert.Equal("1:1202", data.PreviousOwner(5001));

        var com = FeatureStore.Load<CommunitySettings>(data, "community");
        Assert.Equal("Have fun", com.Motd);
        Assert.Equal("Welcome!", com.Welcome);
        Assert.True(com.FilterEnabled);
        Assert.Contains("badword2", com.FilterWords);
        Assert.Equal("MTAxNjQ4.discord-token-1234567890", com.DiscordToken);
        Assert.True(com.DiscordEnabled);
        Assert.Contains(com.Commands, c => c.Command == "discord" && c.Response == "discord.gg/abc");
        Assert.Contains(com.PlayfieldMessages, m => m.Playfield == "Akua");

        var world = FeatureStore.Load<WorldSettings>(data, "world");
        Assert.Contains(world.Limits, l => l.Scope == "player" && l.Type == "CV" && l.MaxCount == 2 && l.MaxClass == 20);
        Assert.Contains(world.Limits, l => l.Scope == "faction" && l.Type == "BA" && l.MaxCount == 3);
        Assert.Contains(world.AutoWipe, w => w.Playfields == "Akua" && w.NotVisitedDays == 10);
        Assert.True(FeatureStore.Load<GameplaySettings>(data, "gameplay").AutominerEnabled);

        var tasks = data.Tasks();
        Assert.Equal(6, tasks.Count);
        // Same on/off state as in EAH.
        Assert.Equal(5, tasks.Count(t => t.Enabled));
        // '#'-separated playfields are split; two wipes at the same time stay two tasks; "all" = every playfield.
        var deposit = tasks.Single(t => t.Params.Contains("\"deposit\""));
        Assert.Contains("[\"Akua\",\"Tallodar\",\"Rogue PX-37 Moon 2\"]", deposit.Params);
        // EAH "Wipe all" (A=31) = POI wipe; P="all" = every playfield.
        var all = tasks.Single(t => t.Params.Contains("\"wipe\":\"poi\"") && t.Params.Contains("[\"*\"]"));
        Assert.False(all.Enabled);
        // Monthly stays monthly.
        Assert.Contains("\"monthly\"", tasks.Single(t => t.Kind == "backup").Schedule);
        var restart = tasks.Single(t => t.Kind == "restart");
        Assert.Contains("\"backupFirst\":true", restart.Params);
        Assert.Contains("04:00", restart.Schedule);
        var wipe = tasks.Single(t => t.Kind == "wipe" && t.Params.Contains("Omicron"));
        Assert.Contains("Omicron", wipe.Params);
        Assert.Contains("\"days\":[1]", wipe.Schedule);
        Assert.Contains("\"minutes\":120", tasks.Single(t => t.Kind == "message").Schedule);

        Assert.Contains(data.InventoryTemplates(), t => t.Name == "Starter");
        Assert.Equal(2, report.Counts["players"]);

        // A second import does not duplicate the tasks.
        EahImporter.Import(data, Config, Path.Combine(_root, "work3"), new EahImportOptions { Chat = false, History = false, Inventories = false });
        Assert.Equal(6, data.Tasks().Count);
    }
}
