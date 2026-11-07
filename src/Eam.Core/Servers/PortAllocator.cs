using System.Net.NetworkInformation;

namespace Eam.Core.Servers;

/// <summary>
/// Hands out ports so that two servers (or a server and another program) never share one.
/// Default layout per server: game ports P..P+3, telnet P+4. EAM's game mod will use P+5 (step 3).
/// </summary>
public static class PortAllocator
{
    public const int GamePortCount = 4;
    public const int BlockSize = 10;
    public const int DefaultFirstPort = 30000;

    public sealed record Suggestion(int GamePort, int TelnetPort);

    public static Suggestion Suggest(IEnumerable<ServerDefinition> existing, Func<int, bool>? isPortBusy = null)
    {
        isPortBusy ??= IsPortInUse;
        var taken = existing.SelectMany(s => s.AllPorts).ToHashSet();
        for (var p = DefaultFirstPort; p < 65000; p += BlockSize)
        {
            var block = Enumerable.Range(p, 6).ToArray();
            if (block.Any(taken.Contains) || block.Any(isPortBusy)) continue;
            return new Suggestion(p, p + 4);
        }
        throw new InvalidOperationException("no_free_ports");
    }

    /// <summary>Returns an error code, or null when the ports can be used by the given server.</summary>
    public static string? Check(int gamePort, int telnetPort, IEnumerable<ServerDefinition> others, Func<int, bool>? isPortBusy = null)
    {
        isPortBusy ??= IsPortInUse;
        if (gamePort is < 1024 or > 65530 || telnetPort is < 1024 or > 65535) return "port_out_of_range";
        var game = Enumerable.Range(gamePort, GamePortCount).Append(telnetPort).ToArray();
        if (game.Distinct().Count() != game.Length) return "port_overlap";
        var mine = game.Append(ModPortFor(gamePort, telnetPort)).ToArray();
        var taken = others.SelectMany(s => s.AllPorts).ToHashSet();
        if (mine.Any(taken.Contains)) return "port_used_by_server";
        if (mine.Any(isPortBusy)) return "port_used_by_program";
        return null;
    }

    public static int ModPortFor(int gamePort, int telnetPort) => telnetPort == gamePort + 5 ? gamePort + 6 : gamePort + 5;

    public static bool IsPortInUse(int port)
    {
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            return props.GetActiveTcpListeners().Any(e => e.Port == port)
                   || props.GetActiveUdpListeners().Any(e => e.Port == port);
        }
        catch { return false; }
    }
}
