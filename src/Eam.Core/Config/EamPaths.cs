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
    /// <summary>
    /// Folder where each new game server gets its own isolated sub-folder. Chosen in the settings;
    /// by default next to EAM's program (e.g. F:\EAM\servers) when EAM keeps its data in ProgramData,
    /// otherwise inside the data folder.
    /// </summary>
    public string ServersDir { get; private set; }

    /// <summary>True when the data folder is the machine-wide one (%ProgramData%), not a portable or forced folder.</summary>
    public bool MachineWide { get; private init; }

    /// <summary>Servers folder used when none is chosen in the settings.</summary>
    public string DefaultServersDir
    {
        get
        {
            if (!MachineWide) return Path.Combine(DataRoot, "servers");
            var exeDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\', '/');
            foreach (var pf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                var dir = Environment.GetFolderPath(pf);
                if (!string.IsNullOrEmpty(dir) && (exeDir + "\\").StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(DataRoot, "servers");
            }
            return Path.Combine(exeDir, "servers");
        }
    }

    /// <summary>Applies the folder chosen in the settings (empty = default).</summary>
    public void UseServersFolder(string? configured)
    {
        ServersDir = string.IsNullOrWhiteSpace(configured) ? DefaultServersDir : Path.GetFullPath(configured.Trim());
    }
    /// <summary>SteamCMD installation owned by EAM (step 2).</summary>
    public string SteamCmdDir => Path.Combine(DataRoot, "steamcmd");

    public EamPaths(string dataRoot)
    {
        DataRoot = Path.GetFullPath(dataRoot);
        ServersDir = Path.Combine(DataRoot, "servers");
    }

    /// <summary>
    /// Resolution order: --data argument, EAM_DATA environment variable, then the "data" folder next to
    /// the program (EAM is portable: everything stays in its own folder). Only when that folder cannot be
    /// written (e.g. EAM placed in Program Files) the machine-wide folder %ProgramData%\EmpyrionAdminManager is used.
    /// Data left in %ProgramData% by an older version is copied next to the program once (never deleted).
    /// </summary>
    public static EamPaths Resolve(string? overrideRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return new EamPaths(overrideRoot);

        var env = Environment.GetEnvironmentVariable("EAM_DATA");
        if (!string.IsNullOrWhiteSpace(env))
            return new EamPaths(env);

        var local = Path.Combine(AppContext.BaseDirectory, "data");
        var machine = MachineWideRoot();
        if (CanWrite(local))
        {
            MigrateFrom(machine, local);
            return new EamPaths(local);
        }
        return new EamPaths(machine) { MachineWide = true };
    }

    private static string MachineWideRoot()
    {
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrEmpty(common))
            common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(common, AppInfo.ServiceName);
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// One-time move to the portable layout: settings, accounts, keys, certificates, logs and SteamCMD are copied
    /// from the old %ProgramData% folder when this folder has no database yet. Game servers already there stay
    /// where they are (their path is kept), and nothing is deleted from the old folder.
    /// </summary>
    internal static void MigrateFrom(string oldRoot, string newRoot)
    {
        try
        {
            if (string.Equals(Path.GetFullPath(oldRoot).TrimEnd('\\', '/'), Path.GetFullPath(newRoot).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(Path.Combine(oldRoot, "eam.db")) || File.Exists(Path.Combine(newRoot, "eam.db"))) return;
            Directory.CreateDirectory(newRoot);
            // The desktop window and the engine may start at the same time: only one copies.
            using var lockFile = new FileStream(Path.Combine(newRoot, ".migrate.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(Path.Combine(newRoot, "eam.db"))) return;
            foreach (var file in Directory.EnumerateFiles(oldRoot))
                File.Copy(file, Path.Combine(newRoot, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.EnumerateDirectories(oldRoot))
            {
                var name = Path.GetFileName(dir);
                if (name is "servers" or "temp") continue;
                CopyDir(dir, Path.Combine(newRoot, name));
            }
            File.WriteAllText(Path.Combine(newRoot, "migrated-from.txt"),
                $"Copied from {oldRoot} on {DateTime.Now:yyyy-MM-dd HH:mm}. That folder is no longer used and can be deleted once everything works." + Environment.NewLine);
        }
        catch (IOException) { /* another process holds the lock and is copying */ }
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.EnumerateDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    public void EnsureCreated()
    {
        foreach (var dir in new[] { DataRoot, LogsDir, KeysDir, CertificatesDir })
            Directory.CreateDirectory(dir);
    }
}
