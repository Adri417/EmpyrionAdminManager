using Eam.Core.Servers;
using Xunit;

namespace Eam.Tests;

/// <summary>0.9.8: pieces added after the EAH/EWA feature audit.</summary>
public class AuditGapTests
{
    [Fact]
    public void BranchBuildsAreReadFromAppInfo()
    {
        const string text = """
            "530870"
            {
                "common" { "name" "Empyrion - Galactic Survival Dedicated Server" }
                "depots"
                {
                    "530871" { "manifests" { "public" { "gid" "123" } } }
                    "branches"
                    {
                        "public"
                        {
                            "buildid"		"16123456"
                            "timeupdated"		"1730900000"
                        }
                        "experimental"
                        {
                            "buildid"		"16200001"
                            "description"		"Experimental"
                        }
                    }
                }
            }
            """;
        var b = SteamCmd.ParseBranchBuilds(text);
        Assert.Equal("16123456", b["public"]);
        Assert.Equal("16200001", b["experimental"]);
        Assert.Empty(SteamCmd.ParseBranchBuilds("no branches here"));
    }

    [Fact]
    public void ChatSearchFindsTextAndNamesAndEscapesWildcards()
    {
        using var dir = new TempDir();
        var data = new ServerData(Path.Combine(dir.Path, "eam", "server.db"));
        data.AddChat(DateTime.UtcNow, "Global", 1, "Adri", "76561190000000001", -1, "hello world", "game");
        data.AddChat(DateTime.UtcNow, "Faction", 2, "Bob", "76561190000000002", -1, "100% sure", "game");
        data.AddChat(DateTime.UtcNow, "Global", 3, "Carl", "76561190000000003", -1, "1000 credits", "game");
        Assert.Single(data.RecentChat(50, null, "world"));
        Assert.Single(data.RecentChat(50, null, "100%"));      // % is a plain character, not a wildcard
        Assert.Equal(2, data.RecentChat(50, null, "100").Count);
        Assert.Single(data.RecentChat(50, null, "carl"));       // sender name, any case
        Assert.Equal(3, data.RecentChat(50, null, null).Count);
    }
}

public class ProxyPortTests
{
    [Fact]
    public void ProxyPortMustBeFreeAndDifferent()
    {
        var c = new Eam.Core.Config.EamConfig { LocalPort = 8790 };
        c.RemoteAccess.ProxyPort = 8792;
        c.Validate();
        c.RemoteAccess.ProxyPort = 8790;
        Assert.Throws<InvalidDataException>(() => c.Validate());
        c.RemoteAccess.ProxyPort = 70000;
        Assert.Throws<InvalidDataException>(() => c.Validate());
        c.RemoteAccess.ProxyPort = 0;
        c.Validate();
    }
}
