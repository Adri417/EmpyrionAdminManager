using System.Net.Sockets;
using System.Text;

namespace Eam.Core.Servers;

/// <summary>
/// Minimal client for the Empyrion dedicated server telnet console (local only).
/// One connection per command keeps it simple and robust against server restarts.
/// </summary>
public static class TelnetClient
{
    public static async Task<string> SendAsync(int port, string password, string command, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (command.Contains('\n') || command.Contains('\r')) throw new ArgumentException("single_line_only");
        timeout ??= TimeSpan.FromSeconds(8);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout.Value);

        using var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync("127.0.0.1", port, cts.Token);
        await using var stream = tcp.GetStream();

        var greeting = await ReadUntilQuietAsync(stream, TimeSpan.FromMilliseconds(400), cts.Token);
        if (!string.IsNullOrEmpty(password) && greeting.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            await WriteLineAsync(stream, password, cts.Token);
            var answer = await ReadUntilQuietAsync(stream, TimeSpan.FromMilliseconds(400), cts.Token);
            if (answer.Contains("wrong", StringComparison.OrdinalIgnoreCase) || answer.Contains("denied", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("telnet_password_rejected");
        }

        await WriteLineAsync(stream, command, cts.Token);
        var output = await ReadUntilQuietAsync(stream, TimeSpan.FromMilliseconds(700), cts.Token);
        return Clean(output);
    }

    public static async Task<bool> IsReachableAsync(int port, CancellationToken ct = default)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync("127.0.0.1", port, cts.Token);
            return true;
        }
        catch { return false; }
    }

    private static Task WriteLineAsync(NetworkStream s, string text, CancellationToken ct) =>
        s.WriteAsync(Encoding.UTF8.GetBytes(text + "\r\n"), ct).AsTask();

    private static async Task<string> ReadUntilQuietAsync(NetworkStream s, TimeSpan quiet, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[8192];
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(quiet);
            int n;
            try { n = await s.ReadAsync(buf, idle.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
            if (n == 0) break;
            sb.Append(Encoding.UTF8.GetString(StripNegotiation(buf.AsSpan(0, n))));
            if (sb.Length > 1_000_000) break;
        }
        return sb.ToString();
    }

    /// <summary>Removes telnet negotiation sequences (IAC ...) from raw bytes.</summary>
    public static byte[] StripNegotiation(ReadOnlySpan<byte> data)
    {
        var o = new List<byte>(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] != 0xFF) { o.Add(data[i]); continue; }
            if (i + 1 >= data.Length) break;
            var cmd = data[i + 1];
            if (cmd == 0xFF) { o.Add(0xFF); i++; continue; }          // escaped 255
            if (cmd == 0xFA)                                          // sub-negotiation until IAC SE
            {
                var j = i + 2;
                while (j + 1 < data.Length && !(data[j] == 0xFF && data[j + 1] == 0xF0)) j++;
                i = j + 1; continue;
            }
            i += cmd is >= 0xFB and <= 0xFE ? 2 : 1;                   // WILL/WONT/DO/DONT opt, or 2-byte command
        }
        return o.ToArray();
    }

    /// <summary>Removes control characters and normalises line endings.</summary>
    public static string Clean(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c == '\r') continue;
            if (c < ' ' && c != '\n' && c != '\t') continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
