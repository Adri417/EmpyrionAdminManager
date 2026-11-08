using System.IO.Compression;
using Eam.Core.Servers;

namespace Eam.Tests;

public class ScheduleTests
{
    [Fact]
    public void DailyPicksNextTimeAndDay()
    {
        var s = Schedule.Parse("""{"type":"daily","times":["16:00","04:00"],"days":[1,3]}""");
        var monday3am = new DateTime(2026, 11, 9, 3, 0, 0); // Monday
        Assert.Equal(new DateTime(2026, 11, 9, 4, 0, 0), s.NextAfter(monday3am));
        Assert.Equal(new DateTime(2026, 11, 9, 16, 0, 0), s.NextAfter(new DateTime(2026, 11, 9, 4, 0, 0)));
        Assert.Equal(new DateTime(2026, 11, 11, 4, 0, 0), s.NextAfter(new DateTime(2026, 11, 9, 17, 0, 0))); // Wednesday
    }

    [Fact]
    public void IntervalAddsMinutes() =>
        Assert.Equal(new DateTime(2026, 1, 1, 1, 30, 0), Schedule.Parse("""{"type":"interval","minutes":90}""").NextAfter(new DateTime(2026, 1, 1)));

    [Theory]
    [InlineData("""{"type":"daily","times":[]}""")]
    [InlineData("""{"type":"daily","times":["25:00"]}""")]
    [InlineData("""{"type":"interval","minutes":1}""")]
    [InlineData("""{"type":"weekly"}""")]
    public void RejectsInvalid(string json) => Assert.Throws<FormatException>(() => Schedule.Parse(json));
}

public class OptionsYamlTests
{
    private const string Template = """
        ### Dedicated server settings

        ServerConfig:
            Srv_Port: 30000
            Srv_Name: My Server
            # Srv_Password: Abc # Only use with public servers (not for Local Coop)

            ### Idle playfield servers held in reserve
            # Valid range: 0..10
            # Srv_ReservePlayfields: 1

            ### Restrict allowed Blueprint size class
            MaxAllowedSizeClass: 10

        GameConfig:
            GameName: DediGame                       # Determines name of the save game
            CustomScenario: Default Multiplayer      # Name of custom scenario
            # Notes:
            # - since we need to directly download the file
            # SharedDataURL: https://drive.google.com/file/d/1xxxxxxxxxxK/view?usp=sharing
        """;

    [Fact]
    public void ListsActiveAndCommentedOptionsWithHelp()
    {
        var e = DedicatedYaml.Parse(Template).Entries();
        Assert.Contains(e, x => x.Key == "Srv_Port" && x.Active && x.Value == "30000");
        var pwd = Assert.Single(e, x => x.Key == "Srv_Password");
        Assert.False(pwd.Active);
        Assert.Equal("Abc", pwd.Value);
        Assert.Contains("public servers", pwd.Help);
        Assert.Contains("0..10", e.Single(x => x.Key == "Srv_ReservePlayfields").Help);
        Assert.Contains("save game", e.Single(x => x.Key == "GameName").Help);
        Assert.DoesNotContain(e, x => x.Key == "Notes");
        Assert.Equal("GameConfig", e.Single(x => x.Key == "SharedDataURL").Section);
    }

    [Fact]
    public void SetKeepsCommentAndDisableComments()
    {
        var y = DedicatedYaml.Parse(Template).Set("GameConfig", "GameName", "Saison 7").Set("ServerConfig", "Srv_ReservePlayfields", "2")
            .Disable("ServerConfig", "MaxAllowedSizeClass");
        var text = y.ToString();
        Assert.Contains("GameName: Saison 7", text);
        Assert.Contains("# Determines name of the save game", text);
        Assert.Equal("2", y.Get("ServerConfig", "Srv_ReservePlayfields"));
        Assert.Null(y.Get("ServerConfig", "MaxAllowedSizeClass"));
        Assert.Contains("# MaxAllowedSizeClass: 10", text);
    }

    private const string GameOptions = """
        # !! For latest info on available parameters !!
        Options:
          - ValidFor: [ SP, Survival ]
            DecayTime: 0
            MaxStructures: 2000
          - ValidFor: [ MP, Survival ]
            DecayTime: 24                     # default value
            # ProtectTime: 48                   # default value
            AntiGriefOresZone: PvE            # default value
            UndergroundBpSpawn: Always        # Permission [Always (default), PvE, Never]

          - ValidFor: [ MP, Creative ]
            MaxStructures: 2000
        """;

    [Fact]
    public void GameOptionsUseTheMultiplayerBlockOfTheMode()
    {
        var g = GameOptionsYaml.Parse(GameOptions);
        var e = g.Entries("Survival");
        Assert.Equal("24", e.Single(x => x.Key == "DecayTime").Value);
        Assert.False(e.Single(x => x.Key == "ProtectTime").Active);
        Assert.Contains("PvE, Never", e.Single(x => x.Key == "UndergroundBpSpawn").Help);
        Assert.DoesNotContain(e, x => x.Key == "MaxStructures");

        g.Set("Survival", "ProtectTime", "72").Set("Survival", "MaxStructures", "500").Disable("Survival", "DecayTime");
        var after = GameOptionsYaml.Parse(g.ToString());
        var e2 = after.Entries("Survival");
        Assert.Equal("72", e2.Single(x => x.Key == "ProtectTime").Value);
        Assert.True(e2.Single(x => x.Key == "ProtectTime").Active);
        Assert.Equal("500", e2.Single(x => x.Key == "MaxStructures").Value);
        Assert.False(e2.Single(x => x.Key == "DecayTime").Active);
        // Other blocks untouched
        Assert.Equal("2000", after.Entries("Creative").Single(x => x.Key == "MaxStructures").Value);
        Assert.Contains("    DecayTime: 0", g.ToString());
    }
}

public class AdminConfigTests
{
    [Fact]
    public void SetsAndRemovesRolesKeepingBans()
    {
        var file = Path.Combine(Path.GetTempPath(), $"eam-admin-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(file, """
            ---
            # Admin Configuration
            Elevated:
            - Id: 11111111111111111 # comment
              Permission: 6
            Banned:
            - Id: 22222222222222222
              Until: 2030-01-01
            """);
        try
        {
            AdminConfig.SetRole(file, "76561198000000001", 9);
            var e = AdminConfig.ReadElevated(file);
            Assert.Equal(2, e.Count);
            Assert.Equal(9, e.Single(x => x.SteamId == "76561198000000001").Permission);
            AdminConfig.SetRole(file, "11111111111111111", 0);
            Assert.Single(AdminConfig.ReadElevated(file));
            var text = File.ReadAllText(file);
            Assert.Contains("Banned:", text);
            Assert.Contains("Until: 2030-01-01", text);
            Assert.Throws<FormatException>(() => AdminConfig.SetRole(file, "123456789", 5));
        }
        finally { File.Delete(file); }
    }
}

public class BackupTests
{
    [Fact]
    public void CreatesListsRestoresAndPrunes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"eam-bk-{Guid.NewGuid():N}");
        var def = new ServerDefinition { Slug = "t", Name = "T", RootDir = root, GamePort = 30000, TelnetPort = 30004, TelnetPassword = "x" };
        var game = Path.Combine(def.GameDir, "Saves", "Games", "World");
        Directory.CreateDirectory(Path.Combine(game, "Playfields"));
        File.WriteAllText(Path.Combine(game, "Playfields", "a.dat"), "v1");
        File.WriteAllText(Path.Combine(def.GameDir, "Saves", "adminconfig.yaml"), "Elevated: []");
        File.WriteAllText(def.YamlFile, "ServerConfig:\n");
        try
        {
            var b = BackupArchive.Create(def, "World", "manual", "tester", "note", f => File.WriteAllText(f, "db"));
            Assert.Equal("manual", b.Kind);
            Assert.Equal("World", b.GameName);
            using (var z = ZipFile.OpenRead(b.File))
            {
                Assert.NotNull(z.GetEntry("Saves/Games/World/Playfields/a.dat"));
                Assert.NotNull(z.GetEntry("eam/server.db"));
            }

            File.WriteAllText(Path.Combine(game, "Playfields", "a.dat"), "v2");
            File.WriteAllText(Path.Combine(game, "new.dat"), "x");
            var restoredDb = Path.Combine(root, "restored.db");
            Assert.Equal("World", BackupArchive.Restore(def, b, false, restoredDb));
            Assert.Equal("v1", File.ReadAllText(Path.Combine(game, "Playfields", "a.dat")));
            Assert.False(File.Exists(Path.Combine(game, "new.dat")));
            Assert.Equal("db", File.ReadAllText(restoredDb));
            Assert.Empty(Directory.GetDirectories(Path.Combine(def.GameDir, "Saves", "Games")).Where(d => d.Contains(".eam-")));

            for (var i = 0; i < 3; i++) BackupArchive.Create(def, "World", "scheduled", null, null, null);
            Assert.Equal(1, BackupArchive.Prune(BackupArchive.BackupsDir(def), 2));
            var list = BackupArchive.List(def);
            Assert.Equal(3, list.Count); // 2 scheduled + the manual one, never pruned
            Assert.NotNull(BackupArchive.Find(def, b.Name));
            Assert.Null(BackupArchive.Find(def, "../../etc"));
        }
        finally { Directory.Delete(root, true); }
    }
}

public class CatalogTests
{
    [Fact]
    public void ReadsItemsBlocksAndTranslations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"eam-cat-{Guid.NewGuid():N}");
        var def = new ServerDefinition { Slug = "t", Name = "T", RootDir = root, GamePort = 30000, TelnetPort = 30004, TelnetPassword = "x" };
        var sc = Path.Combine(ScenarioLibrary.ScenariosDir(def), "My Scenario");
        Directory.CreateDirectory(Path.Combine(sc, "Content", "Configuration"));
        Directory.CreateDirectory(Path.Combine(sc, "Extras"));
        File.WriteAllText(Path.Combine(sc, "Content", "Configuration", "ItemsConfig.ecf"), "{ +Item Id: 224, Name: IronIngot, Ref: ComponentsTemplate\n}\n");
        File.WriteAllText(Path.Combine(sc, "Content", "Configuration", "BlocksConfig.ecf"), "{ Block Id: 1, Name: Stone\n}\n{ +Block Id: 399, Name: ConcreteBlocks    \n}\n");
        File.WriteAllText(Path.Combine(sc, "Extras", "Localization.csv"),
            "KEY,English,Deutsch,Français\nIronIngot,Iron Ingot,Eisenbarren,Lingot de fer\nConcreteBlocks,[c][ff0000]Concrete[-][/c],Beton,\"Béton, blocs\"\n");
        try
        {
            var fr = ItemCatalog.Load(def, "My Scenario", "fr");
            Assert.Equal("Lingot de fer", fr.Single(i => i.Id == 224 + 8192).Label);
            Assert.Equal("Béton, blocs", fr.Single(i => i.Id == 399).Label);
            Assert.True(fr.Single(i => i.Id == 399).IsBlock);
            Assert.Equal("Concrete", ItemCatalog.Load(def, "My Scenario", "en").Single(i => i.Id == 399).Label);
            var list = ScenarioLibrary.List(def);
            Assert.Equal("My Scenario", Assert.Single(list).Name);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InstallsScenarioFromNestedFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"eam-sc-{Guid.NewGuid():N}");
        var def = new ServerDefinition { Slug = "t", Name = "T", RootDir = Path.Combine(root, "srv"), GamePort = 30000, TelnetPort = 30004, TelnetPassword = "x" };
        var src = Path.Combine(root, "download", "3143225812");
        Directory.CreateDirectory(Path.Combine(src, "Playfields"));
        File.WriteAllText(Path.Combine(src, "gameoptions.yaml"), "Options:\n");
        File.WriteAllText(Path.Combine(src, "description.txt"), "[c][ff0000]Reforged[-][/c] Eden 2");
        try
        {
            Assert.Equal(src, ScenarioLibrary.FindRoot(Path.Combine(root, "download")));
            ScenarioLibrary.Install(def, src, "Reforged Eden 2", "3143225812");
            ScenarioLibrary.Install(def, src, "Reforged Eden 2", "3143225812"); // update in place
            var s = Assert.Single(ScenarioLibrary.List(def));
            Assert.Equal("3143225812", s.WorkshopId);
            Assert.Equal("Reforged Eden 2", s.Description);
            Assert.Throws<ArgumentException>(() => ScenarioLibrary.Install(def, src, "..\\evil", null));
        }
        finally { Directory.Delete(root, true); }
    }
}
