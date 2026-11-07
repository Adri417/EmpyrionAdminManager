using System.Net;
using System.Net.Sockets;
using System.Text;
using Eam.Core.Config;
using Eam.Core.Data;
using Eam.Core.Servers;

namespace Eam.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Les Rescapés - Saison 6", "les-rescapes-saison-6")]
    [InlineData("  ***  ", "server")]
    [InlineData("CON", "con-server")]
    [InlineData("Ünïcödé Sérvér!!", "unicode-server")]
    public void MakesSafeFolderNames(string name, string expected) => Assert.Equal(expected, Slug.From(name));

    [Fact]
    public void IsLimitedTo40Chars() => Assert.True(Slug.From(new string('a', 100)).Length <= 40);
}

public class PortTests
{
    private static ServerDefinition Srv(int game, int telnet) => new()
    {
        Slug = "s" + game, Name = "S", RootDir = "/x/" + game, GamePort = game, TelnetPort = telnet, TelnetPassword = "p",
    };

    [Fact]
    public void SuggestSkipsUsedBlocks()
    {
        var s = PortAllocator.Suggest([Srv(30000, 30004)], _ => false);
        Assert.Equal(30010, s.GamePort);
        Assert.Equal(30014, s.TelnetPort);
    }

    [Fact]
    public void SuggestSkipsPortsUsedByOtherPrograms()
    {
        var s = PortAllocator.Suggest([], p => p == 30002);
        Assert.Equal(30010, s.GamePort);
    }

    [Theory]
    [InlineData(30000, 30004, null)]
    [InlineData(80, 30004, "port_out_of_range")]
    [InlineData(30000, 30002, "port_overlap")]
    [InlineData(40002, 40010, "port_used_by_server")]
    [InlineData(30000, 40004, "port_used_by_server")]
    public void CheckDetectsConflicts(int game, int telnet, string? expected) =>
        Assert.Equal(expected, PortAllocator.Check(game, telnet, [Srv(40000, 40004)], _ => false));

    [Fact]
    public void CheckDetectsOtherPrograms() =>
        Assert.Equal("port_used_by_program", PortAllocator.Check(30000, 30004, [], p => p == 30001));
}

public class YamlTests
{
    private const string Sample = "# comment kept\r\nServerConfig:\r\n    Srv_Port: 30000 # game port\r\n    # Tel_Pwd: secret\r\n    Srv_Name: Old\r\nGameConfig:\r\n    GameName: DediGame\r\n";

    [Fact]
    public void GetReadsValuesWithoutComments()
    {
        var y = DedicatedYaml.Parse(Sample);
        Assert.Equal("30000", y.Get("ServerConfig", "Srv_Port"));
        Assert.Equal("DediGame", y.Get("GameConfig", "GameName"));
        Assert.Null(y.Get("ServerConfig", "Tel_Pwd"));
    }

    [Fact]
    public void SetKeepsCommentsAndUncomments()
    {
        var y = DedicatedYaml.Parse(Sample)
            .Set("ServerConfig", "Srv_Name", "Les Rescapés: RE3 #1")
            .Set("ServerConfig", "Tel_Pwd", "abc")
            .Set("ServerConfig", "Srv_MaxPlayers", "12");
        var text = y.ToString();
        Assert.Contains("# comment kept", text);
        Assert.Contains("    Tel_Pwd: abc", text);
        Assert.Equal("Les Rescapés: RE3 #1", y.Get("ServerConfig", "Srv_Name"));
        Assert.Equal("12", DedicatedYaml.Parse(text).Get("ServerConfig", "Srv_MaxPlayers"));
        Assert.Equal("GameName: DediGame", DedicatedYaml.Parse(text).ToString().Split("\r\n")[^2].Trim());
    }

    [Fact]
    public void SetCreatesMissingSection()
    {
        var y = DedicatedYaml.Parse("ServerConfig:\n    Srv_Port: 1\n").Set("GameConfig", "Mode", "Survival");
        Assert.Equal("Survival", y.Get("GameConfig", "Mode"));
        Assert.Equal("1", y.Get("ServerConfig", "Srv_Port"));
    }

    [Fact]
    public void DefaultIsUsable()
    {
        var y = DedicatedYaml.Default();
        Assert.Equal("true", y.Get("ServerConfig", "Tel_Enabled"));
        Assert.Equal("DediGame", y.Get("GameConfig", "GameName"));
    }

    [Fact]
    public void SaveIsAtomic()
    {
        using var dir = new TempDir();
        var f = Path.Combine(dir.Path, "dedicated-eam.yaml");
        DedicatedYaml.Default().Set("ServerConfig", "Srv_Name", "Test").Save(f);
        Assert.False(File.Exists(f + ".tmp"));
        Assert.Equal("Test", DedicatedYaml.Load(f).Get("ServerConfig", "Srv_Name"));
    }
}

public class SteamCmdParseTests
{
    [Fact]
    public void ReadsProgress()
    {
        var (p, ok, err) = SteamCmd.Parse(" Update state (0x61) downloading, progress: 42.17 (1234 / 5678)");
        Assert.NotNull(p);
        Assert.Equal("downloading", p!.Stage);
        Assert.Equal(42.17, p.Percent, 2);
        Assert.False(ok); Assert.Null(err);
    }

    [Fact]
    public void ReadsSuccessAndError()
    {
        Assert.True(SteamCmd.Parse("Success! App '530870' fully installed.").success);
        Assert.Equal("ERROR! Failed to install app '530870' (No subscription)",
            SteamCmd.Parse("Error: ERROR! Failed to install app '530870' (No subscription)").error);
        Assert.Equal((null, false, null), SteamCmd.Parse("Loading Steam API...OK"));
    }

    [Fact]
    public void ReadsBuildIdFromManifest()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "steamapps"));
        File.WriteAllText(Path.Combine(dir.Path, "steamapps", "appmanifest_530870.acf"),
            "\"AppState\"\n{\n\t\"appid\"\t\t\"530870\"\n\t\"buildid\"\t\t\"16012345\"\n}\n");
        Assert.Equal("16012345", SteamCmd.ReadBuildId(dir.Path));
    }
}

public class ServerStoreTests
{
    [Fact]
    public void AddUpdateRemoveAndUniqueness()
    {
        using var dir = new TempDir();
        var db = new Database(Path.Combine(dir.Path, "eam.db"));
        db.Migrate();
        var store = new ServerStore(db);
        var a = store.Add(new ServerDefinition
        {
            Slug = "a", Name = "Alpha", RootDir = Path.Combine(dir.Path, "a"), GamePort = 30000, TelnetPort = 30004, TelnetPassword = "x",
        });
        Assert.True(a.Id > 0);
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => store.Add(new ServerDefinition
        {
            Slug = "b", Name = "Beta", RootDir = Path.Combine(dir.Path, "b"), GamePort = 30000, TelnetPort = 30014, TelnetPassword = "x",
        }));
        store.Update(a with { Name = "Alpha 2" });
        Assert.Equal("Alpha 2", store.Find(a.Id)!.Name);
        store.Remove(a.Id);
        Assert.Empty(store.List());
    }
}

public class TelnetTests
{
    [Fact]
    public void StripsNegotiation()
    {
        byte[] raw = [0xFF, 0xFB, 0x01, (byte)'o', (byte)'k', 0xFF, 0xFA, 0x18, 0x01, 0xFF, 0xF0, (byte)'!'];
        Assert.Equal("ok!", Encoding.ASCII.GetString(TelnetClient.StripNegotiation(raw)));
    }

    [Fact]
    public async Task SendsPasswordThenCommand()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new List<string>();
        var server = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync();
            var s = c.GetStream();
            var r = new StreamReader(s, Encoding.UTF8);
            var w = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
            await s.WriteAsync(new byte[] { 0xFF, 0xFB, 0x01 });
            await w.WriteLineAsync("Please enter password:");
            received.Add((await r.ReadLineAsync())!);
            await w.WriteLineAsync("Logged in.");
            received.Add((await r.ReadLineAsync())!);
            await w.WriteLineAsync("Players: 0 — vide");
            await Task.Delay(1500);
        });
        var output = await TelnetClient.SendAsync(port, "s3cret", "plys", TimeSpan.FromSeconds(5));
        await server;
        listener.Stop();
        Assert.Equal(["s3cret", "plys"], received);
        Assert.Equal("Players: 0 — vide", output);
    }

    [Fact]
    public async Task UnreachablePortIsReported()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        Assert.False(await TelnetClient.IsReachableAsync(port));
    }
}
