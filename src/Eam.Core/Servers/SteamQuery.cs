using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Eam.Core.Servers;

/// <summary>
/// Steam server query (A2S) on the local query port: who is online, without asking the game anything else.
/// Returns null when the server does not answer.
/// </summary>
public static class SteamQuery
{
    public sealed record Player(string Name, int Score, double Seconds);
    public sealed record Info(string Name, string Map, int Players, int MaxPlayers);

    public static async Task<Info?> InfoAsync(int port, CancellationToken ct = default)
    {
        var req = new List<byte> { 0xFF, 0xFF, 0xFF, 0xFF, 0x54 };
        req.AddRange(Encoding.ASCII.GetBytes("Source Engine Query\0"));
        var data = await AskAsync(port, [.. req], 0x49, ct);
        if (data is null) return null;
        try
        {
            var i = 6; // header (4) + type (1) + protocol (1)
            var name = Str(data, ref i); var map = Str(data, ref i); Str(data, ref i); Str(data, ref i);
            i += 2; // app id
            return new Info(name, map, data[i], data[i + 1]);
        }
        catch (IndexOutOfRangeException) { return null; }
    }

    public static async Task<List<Player>?> PlayersAsync(int port, CancellationToken ct = default)
    {
        var data = await AskAsync(port, [0xFF, 0xFF, 0xFF, 0xFF, 0x55, 0xFF, 0xFF, 0xFF, 0xFF], 0x44, ct);
        if (data is null) return null;
        var list = new List<Player>();
        try
        {
            var i = 5;
            int count = data[i++];
            for (var n = 0; n < count && i < data.Length; n++)
            {
                i++; // index
                var name = Str(data, ref i);
                var score = BitConverter.ToInt32(data, i); i += 4;
                var secs = BitConverter.ToSingle(data, i); i += 4;
                if (name.Length > 0) list.Add(new Player(name, score, secs));
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException) { }
        return list;
    }

    /// <summary>Sends the request, answers a challenge (0x41) once, and returns the reply of the expected type.</summary>
    private static async Task<byte[]?> AskAsync(int port, byte[] request, byte expected, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var ep = new IPEndPoint(IPAddress.Loopback, port);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var req = request;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await udp.SendAsync(req, ep, cts.Token);
                var res = await udp.ReceiveAsync(cts.Token);
                var d = res.Buffer;
                if (d.Length < 5) return null;
                if (d[4] == 0x41 && d.Length >= 9)
                {
                    // challenge: repeat the request with it (A2S_INFO appends it, A2S_PLAYER replaces the -1)
                    var challenge = d[5..9];
                    req = request[4] == 0x54 ? [.. request, .. challenge] : [.. request[..5], .. challenge];
                    continue;
                }
                return d[4] == expected ? d : null;
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        return null;
    }

    private static string Str(byte[] d, ref int i)
    {
        var start = i;
        while (d[i] != 0) i++;
        var s = Encoding.UTF8.GetString(d, start, i - start);
        i++;
        return s;
    }
}
