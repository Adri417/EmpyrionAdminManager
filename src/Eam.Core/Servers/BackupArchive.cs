using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Eam.Core.Servers;

public sealed record BackupInfo(string File, string Name, DateTime CreatedUtc, string Kind, long Size, string? GameName, string? By, string? Note);

public sealed record BackupManifest(string App, int Format, DateTime CreatedUtc, string Kind, string Server, string? GameName,
    string? By, string? Note, List<string> Skipped);

/// <summary>
/// Backups of one server: zip files in servers\&lt;name&gt;\backups. Each zip holds the save game
/// (Saves\Games\&lt;GameName&gt;), the player blueprints, adminconfig.yaml, EAM's dedicated-eam.yaml
/// and EAM's database for this server, plus a small manifest.
/// A backup is first written as .tmp and renamed at the end: an interrupted backup never looks complete.
/// </summary>
public static class BackupArchive
{
    public const string ManifestName = "eam-backup.json";
    public const string Extension = ".zip";
    /// <summary>Kinds that are pruned automatically ("manual" backups stay until deleted).</summary>
    public static readonly string[] AutomaticKinds = ["scheduled", "before-restore", "before-update", "before-wipe"];

    public static string BackupsDir(ServerDefinition def) => Path.Combine(def.RootDir, "backups");

    public static string SavesDir(ServerDefinition def) => Path.Combine(def.GameDir, "Saves");

    /// <summary>Creates a backup. <paramref name="snapshotDb"/> writes a consistent copy of EAM's database to the given path.</summary>
    public static BackupInfo Create(ServerDefinition def, string? gameName, string kind, string? by, string? note,
        Action<string>? snapshotDb, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = BackupsDir(def);
        Directory.CreateDirectory(dir);
        var now = DateTime.UtcNow;
        var name = $"{now.ToLocalTime():yyyyMMdd-HHmmss}_{kind}";
        var file = Path.Combine(dir, name + Extension);
        for (var i = 2; File.Exists(file); i++) file = Path.Combine(dir, $"{name}-{i}{Extension}");
        var tmp = file + ".tmp";
        var skipped = new List<string>();

        var saves = SavesDir(def);
        var sources = new List<(string Path, string Entry)>();
        if (!string.IsNullOrEmpty(gameName))
        {
            var game = Path.Combine(saves, "Games", gameName);
            if (Directory.Exists(game)) AddTree(sources, game, $"Saves/Games/{gameName}");
        }
        var bps = Path.Combine(saves, "Blueprints");
        if (Directory.Exists(bps)) AddTree(sources, bps, "Saves/Blueprints");
        var admin = Path.Combine(saves, "adminconfig.yaml");
        if (File.Exists(admin)) sources.Add((admin, "Saves/adminconfig.yaml"));
        if (File.Exists(def.YamlFile)) sources.Add((def.YamlFile, "dedicated-eam.yaml"));

        var total = Math.Max(1, sources.Sum(s => SafeLength(s.Path)));
        long done = 0;
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                foreach (var (path, entry) in sources)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        // The game may be writing: read with full sharing, never lock its files.
                        using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        var e = zip.CreateEntry(entry, CompressionLevel.Fastest);
                        e.LastWriteTime = File.GetLastWriteTime(path);
                        using var dst = e.Open();
                        src.CopyTo(dst);
                    }
                    catch (IOException) { skipped.Add(entry); }
                    catch (UnauthorizedAccessException) { skipped.Add(entry); }
                    done += SafeLength(path);
                    progress?.Report(100.0 * done / total);
                }

                if (snapshotDb is not null)
                {
                    var dbCopy = Path.Combine(dir, $".db-{Guid.NewGuid():N}.tmp");
                    try
                    {
                        snapshotDb(dbCopy);
                        zip.CreateEntryFromFile(dbCopy, "eam/server.db", CompressionLevel.Fastest);
                    }
                    catch (Exception) { skipped.Add("eam/server.db"); }
                    finally { TryDelete(dbCopy); }
                }

                var manifest = new BackupManifest("Empyrion Admin Manager", 1, now, kind, def.Name, gameName, by, note, skipped);
                using var m = zip.CreateEntry(ManifestName).Open();
                JsonSerializer.Serialize(m, manifest);
            }
            File.Move(tmp, file);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
        return Read(file) ?? throw new IOException("backup_unreadable");
    }

    public static IReadOnlyList<BackupInfo> List(ServerDefinition def)
    {
        var dir = BackupsDir(def);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*" + Extension).Select(Read).OfType<BackupInfo>()
            .OrderByDescending(b => b.CreatedUtc).ToList();
    }

    public static BackupInfo? Read(string file)
    {
        try
        {
            var fi = new FileInfo(file);
            using var zip = ZipFile.OpenRead(file);
            var m = zip.GetEntry(ManifestName);
            BackupManifest? manifest = null;
            if (m is not null) { using var s = m.Open(); manifest = JsonSerializer.Deserialize<BackupManifest>(s); }
            return new BackupInfo(file, Path.GetFileNameWithoutExtension(file), manifest?.CreatedUtc ?? fi.LastWriteTimeUtc,
                manifest?.Kind ?? "manual", fi.Length, manifest?.GameName, manifest?.By, manifest?.Note);
        }
        catch { return null; }
    }

    /// <summary>Finds a backup by name inside this server's backup folder only (no path from the outside is trusted).</summary>
    public static BackupInfo? Find(ServerDefinition def, string name) =>
        List(def).FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Restores a backup into a STOPPED server. The current save is moved aside first and only
    /// deleted once the restored one is fully in place. Returns the restored game name.
    /// </summary>
    public static string? Restore(ServerDefinition def, BackupInfo backup, bool restoreConfig, string? restoreDbTo)
    {
        var saves = SavesDir(def);
        Directory.CreateDirectory(saves);
        var work = Path.Combine(saves, $".eam-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);
        var aside = new List<(string Original, string Moved)>();
        try
        {
            using (var zip = ZipFile.OpenRead(backup.File))
            {
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.EndsWith('/')) continue;
                    var target = Path.GetFullPath(Path.Combine(work, e.FullName));
                    if (!target.StartsWith(Path.GetFullPath(work) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("backup_invalid_path"); // zip-slip protection
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, overwrite: true);
                }
            }

            string? gameName = null;
            var games = Path.Combine(work, "Saves", "Games");
            if (Directory.Exists(games))
            {
                foreach (var g in Directory.GetDirectories(games))
                {
                    gameName = Path.GetFileName(g);
                    var dest = Path.Combine(saves, "Games", gameName);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    MoveAside(dest, aside);
                    Directory.Move(g, dest);
                }
            }
            var bps = Path.Combine(work, "Saves", "Blueprints");
            if (Directory.Exists(bps))
            {
                var dest = Path.Combine(saves, "Blueprints");
                MoveAside(dest, aside);
                Directory.Move(bps, dest);
            }
            var admin = Path.Combine(work, "Saves", "adminconfig.yaml");
            if (File.Exists(admin)) File.Copy(admin, Path.Combine(saves, "adminconfig.yaml"), overwrite: true);
            var yaml = Path.Combine(work, "dedicated-eam.yaml");
            if (restoreConfig && File.Exists(yaml)) File.Copy(yaml, def.YamlFile, overwrite: true);
            var db = Path.Combine(work, "eam", "server.db");
            if (restoreDbTo is not null && File.Exists(db))
            {
                TryDelete(restoreDbTo + "-wal");
                TryDelete(restoreDbTo + "-shm");
                File.Copy(db, restoreDbTo, overwrite: true);
            }

            foreach (var (_, moved) in aside) TryDeleteTree(moved);
            return gameName;
        }
        catch
        {
            // Put everything back as it was.
            foreach (var (original, moved) in aside)
            {
                try
                {
                    if (Directory.Exists(original)) Directory.Delete(original, true);
                    Directory.Move(moved, original);
                }
                catch { }
            }
            throw;
        }
        finally { TryDeleteTree(work); }
    }

    /// <summary>Keeps the <paramref name="keep"/> most recent automatic backups; manual ones are never pruned.</summary>
    public static int Prune(string dir, int keep)
    {
        if (!Directory.Exists(dir) || keep < 1) return 0;
        var auto = Directory.EnumerateFiles(dir, "*" + Extension).Select(Read).OfType<BackupInfo>()
            .Where(b => AutomaticKinds.Contains(b.Kind)).OrderByDescending(b => b.CreatedUtc).Skip(keep).ToList();
        foreach (var b in auto) TryDelete(b.File);
        // Leftovers of interrupted backups.
        foreach (var t in Directory.EnumerateFiles(dir, "*.tmp"))
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(t) > TimeSpan.FromHours(12)) TryDelete(t);
        return auto.Count;
    }

    private static void MoveAside(string path, List<(string, string)> aside)
    {
        if (!Directory.Exists(path)) return;
        var moved = $"{path}.eam-old-{DateTime.UtcNow:yyyyMMddHHmmss}";
        Directory.Move(path, moved);
        aside.Add((path, moved));
    }

    private static void AddTree(List<(string, string)> list, string dir, string prefix)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            list.Add((f, prefix + "/" + Path.GetRelativePath(dir, f).Replace('\\', '/')));
    }

    private static long SafeLength(string f) { try { return new FileInfo(f).Length; } catch { return 0; } }

    private static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

    private static void TryDeleteTree(string d) { try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } }

    public static string FormatSize(long bytes) => (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
}
