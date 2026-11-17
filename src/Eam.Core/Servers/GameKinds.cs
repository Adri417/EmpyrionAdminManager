namespace Eam.Core.Servers;

/// <summary>
/// The games EAM can run. Everything common (accounts, scheduler, SteamCMD updates, service...) is shared;
/// what differs per game (Steam app, executable, configuration, clean stop) is described here and in the
/// game-specific code paths.
/// </summary>
public static class GameKinds
{
    public const string Empyrion = "empyrion";
    /// <summary>Vita Somnium Life (Unity dedicated server, configuration in server_config.json, data in MySQL).</summary>
    public const string Vsl = "vsl";

    public static readonly string[] All = [Empyrion, Vsl];

    public static string Normalize(string? game) =>
        All.FirstOrDefault(g => string.Equals(g, game?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Empyrion;

    /// <summary>Steam app of the dedicated server (downloaded anonymously with SteamCMD).</summary>
    public static string AppId(string game) => game == Vsl ? "5164760" : SteamCmd.EmpyrionDedicatedAppId;

    /// <summary>Names of the game's processes (without .exe).</summary>
    public static string[] ProcessNames(string game) => game == Vsl
        ? ["Vita Somnium Life Server"]
        : ["EmpyrionDedicated", "EmpyrionPlayfieldServer", "EmpyrionLauncher"];

    /// <summary>The process that stands for "the server is running".</summary>
    public static string MainProcess(string game) => game == Vsl ? "Vita Somnium Life Server" : "EmpyrionDedicated";

    public const string VslExe = "Vita Somnium Life Server.exe";
}
