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
