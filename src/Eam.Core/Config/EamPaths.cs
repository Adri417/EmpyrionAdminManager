namespace Eam.Core.Config;

/// <summary>
/// Locates every folder used by EAM. Everything lives under one data root so an
/// installation can be moved, backed up or removed as a whole.
/// </summary>
public sealed class EamPaths
{
    public string DataRoot { get; }
    public string ConfigFile => Path.Combine(DataRoot, "eam.json");
    public string DatabaseFile => Path.Combine(DataRoot, "eam.db");
    public string LogsDir => Path.Combine(DataRoot, "logs");
    public string KeysDir => Path.Combine(DataRoot, "keys");
    public string CertificatesDir => Path.Combine(DataRoot, "certificates");
    /// <summary>Root folder where each game server gets its own isolated sub-folder (step 2).</summary>
    public string ServersDir => Path.Combine(DataRoot, "servers");
    /// <summary>SteamCMD installation owned by EAM (step 2).</summary>
    public string SteamCmdDir => Path.Combine(DataRoot, "steamcmd");

    public EamPaths(string dataRoot)
    {
        DataRoot = Path.GetFullPath(dataRoot);
    }

    /// <summary>
    /// Resolution order: EAM_DATA environment variable, then a "data" folder next to the
    /// executable when a file named "portable.txt" exists there, then the machine-wide
    /// folder (%ProgramData%\EmpyrionAdminManager on Windows).
    /// </summary>
    public static EamPaths Resolve(string? overrideRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return new EamPaths(overrideRoot);

        var env = Environment.GetEnvironmentVariable("EAM_DATA");
        if (!string.IsNullOrWhiteSpace(env))
            return new EamPaths(env);

        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
            return new EamPaths(Path.Combine(exeDir, "data"));

        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrEmpty(common))
            common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return new EamPaths(Path.Combine(common, AppInfo.ServiceName));
    }

    public void EnsureCreated()
    {
        foreach (var dir in new[] { DataRoot, LogsDir, KeysDir, CertificatesDir, ServersDir })
            Directory.CreateDirectory(dir);
    }
}
