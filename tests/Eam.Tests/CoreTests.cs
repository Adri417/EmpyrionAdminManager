using Eam.Core.Config;
using Eam.Core.Data;
using Eam.Core.Security;
using Microsoft.Extensions.Time.Testing;

namespace Eam.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "eam-test-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(Path, true); } catch { } }
}

public class PasswordTests
{
    [Fact]
    public void HashThenVerify()
    {
        var h = PasswordHasher.Hash("correct horse battery");
        Assert.True(PasswordHasher.Verify("correct horse battery", h));
        Assert.False(PasswordHasher.Verify("wrong", h));
        Assert.False(PasswordHasher.Verify("x", "garbage"));
    }

    [Fact]
    public void SamePasswordGivesDifferentHashes() =>
        Assert.NotEqual(PasswordHasher.Hash("abcdefghijk"), PasswordHasher.Hash("abcdefghijk"));
}

public class UserStoreTests
{
    private static UserStore NewStore(TempDir dir)
    {
        var db = new Database(System.IO.Path.Combine(dir.Path, "eam.db"));
        db.Migrate();
        return new UserStore(db);
    }

    [Fact]
    public void FirstOwnerOnlyOnce()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        Assert.True(store.NeedsSetup);
        var u = store.CreateFirstOwner("admin", "a-long-password");
        Assert.Equal(Roles.Owner, u.Role);
        var ex = Assert.Throws<UserStoreException>(() => store.CreateFirstOwner("other", "a-long-password"));
        Assert.Equal("already_set_up", ex.Code);
    }

    [Fact]
    public void RejectsBadInput()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        Assert.Equal("password_too_short", Assert.Throws<UserStoreException>(() => store.Create("bob", "short", Roles.Viewer, null)).Code);
        Assert.Equal("invalid_username", Assert.Throws<UserStoreException>(() => store.Create("b o b", "a-long-password", Roles.Viewer, null)).Code);
        Assert.Equal("invalid_role", Assert.Throws<UserStoreException>(() => store.Create("bob", "a-long-password", "god", null)).Code);
        Assert.Equal("invalid_steam_id", Assert.Throws<UserStoreException>(() => store.Create("bob", "a-long-password", Roles.Viewer, "123")).Code);
        store.Create("bob", "a-long-password", Roles.Viewer, "76561198000000000");
        Assert.Equal("username_taken", Assert.Throws<UserStoreException>(() => store.Create("BOB", "a-long-password", Roles.Viewer, null)).Code);
    }

    [Fact]
    public void LastOwnerIsProtected()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        var owner = store.CreateFirstOwner("admin", "a-long-password");
        Assert.Equal("last_owner", Assert.Throws<UserStoreException>(() => store.Delete(owner.Id)).Code);
        Assert.Equal("last_owner", Assert.Throws<UserStoreException>(() => store.Update(owner.Id, Roles.Admin, null)).Code);
    }

    [Fact]
    public void PasswordChangeRotatesStamp()
    {
        using var dir = new TempDir();
        var store = NewStore(dir);
        var u = store.CreateFirstOwner("admin", "a-long-password");
        store.ChangePassword(u.Id, "another-long-password");
        Assert.NotEqual(u.SecurityStamp, store.FindById(u.Id)!.SecurityStamp);
        Assert.Null(store.CheckPassword("admin", "a-long-password"));
        Assert.NotNull(store.CheckPassword("admin", "another-long-password"));
    }
}

public class RoleTests
{
    [Fact]
    public void Hierarchy()
    {
        Assert.True(Roles.AtLeast(Roles.Owner, Roles.Viewer));
        Assert.True(Roles.AtLeast(Roles.Admin, Roles.Admin));
        Assert.False(Roles.AtLeast(Roles.Moderator, Roles.Admin));
    }
}

public class ThrottleTests
{
    [Fact]
    public void BlocksAfterFiveFailuresThenReleases()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var t = new LoginThrottle(time);
        for (var i = 0; i < 4; i++) t.Fail("ip");
        Assert.False(t.IsBlocked("ip", out _));
        t.Fail("ip");
        Assert.True(t.IsBlocked("ip", out _));
        time.Advance(TimeSpan.FromMinutes(16));
        Assert.False(t.IsBlocked("ip", out _));
    }
}

public class ConfigTests
{
    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        using var dir = new TempDir();
        var paths = new EamPaths(dir.Path);
        var cfg = new EamConfig { LocalPort = 9000 };
        cfg.RemoteAccess.Enabled = true;
        cfg.Save(paths);
        var again = EamConfig.Load(paths);
        Assert.Equal(9000, again.LocalPort);
        Assert.True(again.RemoteAccess.Enabled);
    }

    [Fact]
    public void RejectsSamePortTwice()
    {
        var cfg = new EamConfig { LocalPort = 9000 };
        cfg.RemoteAccess = new RemoteAccessConfig { Enabled = true, Port = 9000 };
        Assert.Throws<InvalidDataException>(cfg.Validate);
    }
}

public class YamlIndentTests
{
    [Fact]
    public void NewAndCommentedKeysUseTheIndentationOfTheSection()
    {
        // Empyrion's own file: 2 spaces, Tel_Pwd only as a comment, Tel_Port missing.
        var y = Eam.Core.Servers.DedicatedYaml.Parse("ServerConfig:\n  Srv_Port: 40000\n  # Tel_Pwd: x\nGameConfig:\n  GameName: RE2 S7\n");
        y.Set("ServerConfig", "Tel_Pwd", "secret");
        y.Set("ServerConfig", "Tel_Port", "40004");
        var text = y.ToString();
        Assert.Contains("\r\n  Tel_Pwd: secret\r\n", text);
        Assert.Contains("\r\n  Tel_Port: 40004\r\n", text);
        Assert.DoesNotContain("    Tel_", text);
    }

    [Fact]
    public void AKeyWrittenWithTheWrongIndentationIsRepaired()
    {
        var y = Eam.Core.Servers.DedicatedYaml.Parse("ServerConfig:\n  Srv_Port: 40000\n    Tel_Pwd: old\n");
        y.Set("ServerConfig", "Tel_Pwd", "new");
        Assert.Contains("\r\n  Tel_Pwd: new", y.ToString());
    }
}

public class EahModDetectionTests
{
    [Fact]
    public void EahPluginAndGameModAreRecognised()
    {
        var root = Path.Combine(Path.GetTempPath(), "eam-mods-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "EPM")); Directory.CreateDirectory(Path.Combine(root, "Other"));
        Directory.CreateDirectory(Path.Combine(root, "Mine")); Directory.CreateDirectory(Path.Combine(root, "RescaBridge"));
        File.WriteAllText(Path.Combine(root, "Mine", "Info.yaml"), "Name: EAH Mod\nAuthor: x\n");
        File.WriteAllText(Path.Combine(root, "RescaBridge", "Info.yaml"), "Name: RescaBridge\n");
        File.WriteAllText(Path.Combine(root, "Other", "EAH_Data.dll"), "");
        try
        {
            Assert.True(Eam.Engine.Servers.ServerManager.IsEahMod(Path.Combine(root, "EPM")));
            Assert.True(Eam.Engine.Servers.ServerManager.IsEahMod(Path.Combine(root, "Other")));
            Assert.True(Eam.Engine.Servers.ServerManager.IsEahMod(Path.Combine(root, "Mine")));
            Assert.False(Eam.Engine.Servers.ServerManager.IsEahMod(Path.Combine(root, "RescaBridge")));
        }
        finally { Directory.Delete(root, true); }
    }
}

public class ItemCatalogTests
{
    [Fact]
    public void ScenarioOverridesTheGameAndBlocksWithoutIdComeFromTheSave()
    {
        var root = Path.Combine(Path.GetTempPath(), "eam-cat-" + Guid.NewGuid().ToString("N"));
        var def = new Eam.Core.Servers.ServerDefinition { Id = 1, Name = "t", Slug = "t", RootDir = root, GamePort = 30000, TelnetPort = 30004, TelnetPassword = "x" };
        try
        {
            var game = def.GameDir;
            Directory.CreateDirectory(Path.Combine(game, "Content", "Configuration"));
            File.WriteAllText(Path.Combine(game, "Content", "Configuration", "ItemsConfig.ecf"), "{ +Item Id: 80, Name: OxygenBottleLarge\n}\n{ +Item Id: 224, Name: IronIngot\n}\n");
            File.WriteAllText(Path.Combine(game, "Content", "Configuration", "BlocksConfig.ecf"), "{ +Block Id: 10, Name: Old\n}\n");
            var sc = Path.Combine(game, "Content", "Scenarios", "S");
            Directory.CreateDirectory(Path.Combine(sc, "Content", "Configuration"));
            File.WriteAllText(Path.Combine(sc, "Content", "Configuration", "ItemsConfig.ecf"), "{ +Item Id: 320, Name: AlienTooth\n}\n");
            File.WriteAllText(Path.Combine(sc, "Content", "Configuration", "BlocksConfig.ecf"), "{ +Block Name: NewBlock\n}\n");
            Directory.CreateDirectory(Path.Combine(sc, "SharedData", "Content", "Bundles", "ItemIcons"));
            File.WriteAllText(Path.Combine(sc, "SharedData", "Content", "Bundles", "ItemIcons", "OxygenBottleLarge.png"), "png");
            File.WriteAllText(def.YamlFile, "ServerConfig:\n  Srv_Port: 30000\nGameConfig:\n  GameName: G\n  CustomScenario: S\n");
            var save = Path.Combine(game, "Saves", "Games", "G");
            Directory.CreateDirectory(save);
            var map = new List<byte>(new byte[9]);
            map.Add(8); map.AddRange(System.Text.Encoding.ASCII.GetBytes("NewBlock")); map.Add(0x10); map.Add(0x08); // 2064
            File.WriteAllBytes(Path.Combine(save, "blocksmap.dat"), map.ToArray());

            var items = Eam.Core.Servers.ItemCatalog.Load(def, "S", "en");
            Assert.Contains(items, i => i.Id == 8272 && i.Name == "OxygenBottleLarge");
            Assert.Contains(items, i => i.Id == 8512 && i.Name == "AlienTooth");
            Assert.Contains(items, i => i.Id == 2064 && i.Name == "NewBlock" && i.IsBlock);
            Assert.EndsWith("OxygenBottleLarge.png", Eam.Core.Servers.ItemIcons.Find(def, "S", 8272, "OxygenBottleLarge"));
            Assert.Null(Eam.Core.Servers.ItemIcons.Find(def, "S", 8416, "IronIngot"));
        }
        finally { Directory.Delete(root, true); }
    }
}
