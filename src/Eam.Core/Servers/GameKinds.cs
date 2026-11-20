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
    /// <summary>Satisfactory (Unreal dedicated server, managed through its HTTPS API on the game port).</summary>
    public const string Satisfactory = "satisfactory";

    public static readonly string[] All = [Empyrion, Vsl, Satisfactory];

    public static string Normalize(string? game) =>
        All.FirstOrDefault(g => string.Equals(g, game?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Empyrion;

    /// <summary>Steam app of the dedicated server (downloaded anonymously with SteamCMD).</summary>
    public static string AppId(string game) => game switch
    {
        Vsl => "5164760",
        Satisfactory => "1690800",
        _ => SteamCmd.EmpyrionDedicatedAppId,
    };

    /// <summary>Names of the game's processes (without .exe).</summary>
    public static string[] ProcessNames(string game) => game switch
    {
        Vsl => ["Vita Somnium Life Server"],
        Satisfactory => ["FactoryServer-Win64-Shipping-Cmd", "FactoryServer-Win64-Shipping", "FactoryServer"],
        _ => ["EmpyrionDedicated", "EmpyrionPlayfieldServer", "EmpyrionLauncher"],
    };

    /// <summary>The process that stands for "the server is running".</summary>
    public static string MainProcess(string game) => game switch
    {
        Vsl => "Vita Somnium Life Server",
        Satisfactory => "FactoryServer-Win64-Shipping-Cmd",
        _ => "EmpyrionDedicated",
    };

    public const string VslExe = "Vita Somnium Life Server.exe";
    public const string SatisfactoryExe = "FactoryServer.exe";
}
