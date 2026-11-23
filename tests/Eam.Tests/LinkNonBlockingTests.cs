using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Eam.Link;
using Xunit;

namespace Eam.Tests;

public class LinkNonBlockingTests
{
    /// <summary>The game thread must never wait for EAM: Send only queues, even when EAM stops reading.</summary>
    [Fact]
    public async Task Send_never_blocks_when_the_engine_stops_reading()
    {
        var port = 46000 + Random.Shared.Next(1000);
        var logs = new List<string>();
        using var server = new LinkServer(port, "0123456789abcdef0123", l => { lock (logs) logs.Add(l); });
        server.Start();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", port);
        var stream = tcp.GetStream();
        var reader = new StreamReader(stream, new UTF8Encoding(false));
        var greeting = (await reader.ReadLineAsync())!.Split(' ');
        await stream.WriteAsync(Encoding.UTF8.GetBytes("AUTH " + LinkProtocol.Sign("0123456789abcdef0123", greeting[2]) + "\n"));
        Assert.Equal("OK", await reader.ReadLineAsync());
        for (var i = 0; i < 50 && !server.Connected; i++) await Task.Delay(20);
        Assert.True(server.Connected);

        // Never read again: big messages fill the socket buffers.
        var big = new string('x', 20_000);
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < LinkServer.MaxOutbox + 5000; i++)
            server.Send(new Dictionary<string, object> { ["ev"] = "test", ["data"] = big });
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 3000, $"Send blocked for {sw.ElapsedMilliseconds} ms");
        // EAM not reading: the link is reset instead of growing without end.
        for (var i = 0; i < 100 && server.Connected; i++) await Task.Delay(50);
        Assert.False(server.Connected);
    }
}
