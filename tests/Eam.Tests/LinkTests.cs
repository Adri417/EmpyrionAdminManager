using System.Net;
using System.Net.Sockets;
using Eam.Core.Servers;
using Eam.Link;

namespace Eam.Tests;

public class MiniJsonTests
{
    [Fact]
    public void RoundTrip()
    {
        var src = new Dictionary<string, object>
        {
            ["ev"] = "player_info", ["id"] = 42, ["name"] = "Adrien \"le chef\" \n é", ["fps"] = 29.5f,
            ["ok"] = true, ["none"] = null!, ["ids"] = new List<int> { 1, 2, 3 },
            ["nested"] = new Dictionary<string, object> { ["a"] = 1.5 },
        };
        var json = MiniJson.Write(src);
        var back = MiniJson.ParseObject(json);
        Assert.Equal("player_info", back.Str("ev"));
        Assert.Equal(42, back.Num("id"));
        Assert.Equal("Adrien \"le chef\" \n é", back.Str("name"));
        Assert.Equal(29.5, back.Num("fps"), 3);
        Assert.Equal(true, back["ok"]);
        Assert.Null(back["none"]);
        Assert.Equal(3, ((List<object>)back["ids"]).Count);
        Assert.Equal(1.5, ((Dictionary<string, object>)back["nested"]).Num("a"));
        // Compatible with System.Text.Json
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("player_info", doc.RootElement.GetProperty("ev").GetString());
    }

    [Theory]
    [InlineData("{\"a\":")]
    [InlineData("{\"a\":1} x")]
    [InlineData("[1,2")]
    public void RejectsBrokenJson(string json) => Assert.ThrowsAny<FormatException>(() => MiniJson.Parse(json));

    [Fact]
    public void ParsesUnicodeEscapes() => Assert.Equal("é", MiniJson.ParseObject("{\"t\":\"\\u00e9\"}").Str("t"));
}

public class LinkTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static async Task<T> Eventually<T>(Func<T?> probe) where T : class
    {
        for (var i = 0; i < 100; i++)
        {
            if (probe() is { } v) return v;
            await Task.Delay(30);
        }
        throw new TimeoutException();
    }

    [Fact]
    public async Task EngineAndModTalk()
    {
        var port = FreePort();
        const string key = "0123456789abcdef0123456789abcdef";
        using var mod = new LinkServer(port, key, _ => { });
        mod.ClientAuthenticated += () => mod.Send(new Dictionary<string, object> { ["ev"] = "hello", ["mod"] = "test" });
        mod.Start();

        await using var engine = await LinkClient.ConnectAsync(port, key, CancellationToken.None);
        var hello = await engine.ReceiveAsync(CancellationToken.None);
        Assert.Equal("hello", hello!.Value.GetProperty("ev").GetString());

        await engine.SendAsync(new { op = "msg_all", text = "Redémarrage dans 5 min" });
        var got = await Eventually(() => mod.Drain());
        Assert.Equal("msg_all", got[0].Str("op"));
        Assert.Equal("Redémarrage dans 5 min", got[0].Str("text"));

        mod.Send(new Dictionary<string, object> { ["ev"] = "player_list", ["ids"] = new List<int> { 7, 9 } });
        var list = await engine.ReceiveAsync(CancellationToken.None);
        Assert.Equal(2, list!.Value.GetProperty("ids").GetArrayLength());
    }

    [Fact]
    public async Task BigAnswerDoesNotCutTheLink()
    {
        // A busy server's structure list is several MB: it used to exceed the 1 MB line limit and cut the link
        // at every structure refresh (every 90 s), making teleports and other actions fail.
        var port = FreePort();
        const string key = "0123456789abcdef0123456789abcdef";
        using var mod = new LinkServer(port, key, _ => { });
        mod.Start();
        await using var engine = await LinkClient.ConnectAsync(port, key, CancellationToken.None);
        await Eventually(() => mod.Connected ? "" : null);

        var list = Enumerable.Range(0, 30000).Select(i => (object)new Dictionary<string, object> { ["id"] = i, ["name"] = "Structure " + i + new string('x', 80) }).ToList();
        // Sent from another thread: a 3 MB write blocks until the engine reads it (small socket buffers on Windows).
        var sending = Task.Run(() =>
        {
            mod.Send(new Dictionary<string, object> { ["ev"] = "structures", ["list"] = list });
            mod.Send(new Dictionary<string, object> { ["ev"] = "after" });
        });
        var big = await engine.ReceiveAsync(CancellationToken.None);
        Assert.Equal(30000, big!.Value.GetProperty("list").GetArrayLength());
        var after = await engine.ReceiveAsync(CancellationToken.None);
        Assert.Equal("after", after!.Value.GetProperty("ev").GetString());
        await sending;
        Assert.False(engine.Closed.IsCompleted);
        await engine.DisposeAsync();
        Assert.True(engine.Closed.IsCompleted);
    }

    [Fact]
    public async Task WrongKeyIsRefused()
    {
        var port = FreePort();
        using var mod = new LinkServer(port, "the-right-key-0123456789abcdef", _ => { });
        mod.Start();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            LinkClient.ConnectAsync(port, "a-wrong-key-0123456789abcdef00", CancellationToken.None));
        Assert.False(mod.Connected);
    }

    [Fact]
    public void SignatureIsStable()
    {
        Assert.Equal(LinkProtocol.Sign("k", "n"), LinkProtocol.Sign("k", "n"));
        Assert.NotEqual(LinkProtocol.Sign("k", "n"), LinkProtocol.Sign("k2", "n"));
        Assert.True(LinkProtocol.SameSignature("abc", "abc"));
        Assert.False(LinkProtocol.SameSignature("abc", "abd"));
        Assert.False(LinkProtocol.SameSignature(null!, "abd"));
    }
}

public class ServerDataTests
{
    [Fact]
    public void ChatAndPlayers()
    {
        using var dir = new TempDir();
        var data = new ServerData(Path.Combine(dir.Path, "eam", "server.db"));
        for (var i = 0; i < 5; i++) data.AddChat(DateTime.UtcNow, "Global", 100 + i, "P" + i, "7656119", -1, "msg " + i, "game");
        var last3 = data.RecentChat(3);
        Assert.Equal(["msg 2", "msg 3", "msg 4"], last3.Select(c => c.Text));
        var before = data.RecentChat(10, last3[0].Id);
        Assert.Equal(2, before.Count);

        data.PlayerSeen("76561190000000001", 100, "Adrien");
        data.PlayerSeen("76561190000000001", 100, "Adrien2", 60);
        var p = Assert.Single(data.Players());
        Assert.Equal("Adrien2", p.Name);
        Assert.Equal(60, p.PlaytimeSeconds);
    }

    [Fact]
    public void ModPortAvoidsTelnet()
    {
        Assert.Equal(30005, PortAllocator.ModPortFor(30000, 30004));
        Assert.Equal(30006, PortAllocator.ModPortFor(30000, 30005));
        ServerDefinition[] existing = [new() { Slug = "x", Name = "x", RootDir = "/x", GamePort = 30000, TelnetPort = 30004, TelnetPassword = "p" }];
        // The new server's game ports would take the existing server's mod port (30005).
        Assert.Equal("port_used_by_server", PortAllocator.Check(30005, 30020, existing, _ => false));
        // The new server's mod port (game + 5 = 30000) would take the existing game port.
        Assert.Equal("port_used_by_server", PortAllocator.Check(29995, 29999, existing, _ => false));
        Assert.Null(PortAllocator.Check(30010, 30014, existing, _ => false));
    }
}
