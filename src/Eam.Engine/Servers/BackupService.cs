using System.Collections.Concurrent;
using Eam.Core.Servers;
using Eam.Engine.Api;

namespace Eam.Engine.Servers;

public sealed record BackupSettings(int Keep, string? CopyDir, int CopyKeep);

/// <summary>
/// Step 4: backups of each server (create, list, restore, delete, prune, copy to another folder
/// such as a Google Drive or OneDrive synchronised folder).
/// </summary>
public sealed class BackupService(ServerManager manager, LiveService live, LiveHub hub, ILogger<BackupService> log)
{
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _gates = new();

    private SemaphoreSlim Gate(long id) => _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

    public bool IsRunning(long id) => Gate(id).CurrentCount == 0;

    public BackupSettings Settings(long id)
    {
        var data = live.Data(manager.Definition(id));
        return new BackupSettings(
            int.TryParse(data.Setting("backup_keep"), out var k) ? k : 20,
            data.Setting("backup_copy_dir"),
            int.TryParse(data.Setting("backup_copy_keep"), out var ck) ? ck : 10);
    }

    public void SaveSettings(long id, BackupSettings s)
    {
        if (s.Keep is < 1 or > 500 || s.CopyKeep is < 1 or > 500) throw new ServerException("invalid_keep");
        var dir = string.IsNullOrWhiteSpace(s.CopyDir) ? null : s.CopyDir.Trim();
        if (dir is not null)
        {
            if (!Path.IsPathFullyQualified(dir)) throw new ServerException("copy_dir_not_absolute");
            try { Directory.CreateDirectory(dir); }
            catch (Exception e) { throw new ServerException("copy_dir_unusable", e.Message); }
        }
        var data = live.Data(manager.Definition(id));
        data.SetSetting("backup_keep", s.Keep.ToString());
        data.SetSetting("backup_copy_keep", s.CopyKeep.ToString());
        data.SetSetting("backup_copy_dir", dir);
    }

    public IReadOnlyList<BackupInfo> List(long id) => BackupArchive.List(manager.Definition(id));

    public string GameName(ServerDefinition def)
    {
        try { return File.Exists(def.YamlFile) ? DedicatedYaml.Load(def.YamlFile).Get("GameConfig", "GameName") ?? "DediGame" : "DediGame"; }
        catch { return "DediGame"; }
    }

    /// <summary>Creates a backup now. Works while the server runs (files are read without locking them).</summary>
    public async Task<BackupInfo> CreateAsync(long id, string kind, string by, string? note = null)
    {
        var def = manager.Definition(id);
        if (!def.Installed) throw new ServerException("not_installed");
        var gate = Gate(id);
        if (!await gate.WaitAsync(0)) throw new ServerException("backup_running");
        try { return await CreateLockedAsync(def, kind, by, note); }
        finally { gate.Release(); }
    }

    private async Task<BackupInfo> CreateLockedAsync(ServerDefinition def, string kind, string by, string? note)
    {
        var data = live.Data(def);
        var lastPush = DateTime.MinValue;
        var progress = new Progress<double>(p =>
        {
            if ((DateTime.UtcNow - lastPush).TotalMilliseconds < 800) return;
            lastPush = DateTime.UtcNow;
            _ = hub.BroadcastAsync(new { type = "backup_progress", id = def.Id, pct = Math.Round(p, 1) }, CancellationToken.None);
        });
        ServerManager.ServerLog(def, $"Backup ({kind}) started by {by}");
        var started = DateTime.UtcNow;
        BackupInfo info;
        try
        {
            info = await Task.Run(() => BackupArchive.Create(def, GameName(def), kind, by, note, data.SnapshotTo, progress));
        }
        catch (Exception e)
        {
            ServerManager.ServerLog(def, "Backup failed: " + e.Message);
            log.LogError(e, "Backup of '{Name}' failed", def.Name);
            await hub.BroadcastAsync(new { type = "backup_progress", id = def.Id, pct = (double?)null, error = e.Message }, CancellationToken.None);
            throw new ServerException("backup_failed", e.Message);
        }
        ServerManager.ServerLog(def, $"Backup {info.Name} done ({BackupArchive.FormatSize(info.Size)}, {(DateTime.UtcNow - started).TotalSeconds:0} s)");

        var settings = Settings(def.Id);
        var pruned = BackupArchive.Prune(BackupArchive.BackupsDir(def), settings.Keep);
        if (pruned > 0) ServerManager.ServerLog(def, $"{pruned} old automatic backup(s) removed (keeping {settings.Keep})");
        if (settings.CopyDir is { } copyDir) await CopyOutAsync(def, info, copyDir, settings.CopyKeep);

        await hub.BroadcastAsync(new { type = "backup_progress", id = def.Id, pct = (double?)null, done = info.Name }, CancellationToken.None);
        return info;
    }

    /// <summary>Copies the backup to the admin's folder (e.g. Google Drive / OneDrive synchronised folder).</summary>
    private static async Task CopyOutAsync(ServerDefinition def, BackupInfo info, string copyDir, int keep)
    {
        try
        {
            var dir = Path.Combine(copyDir, def.Slug);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, Path.GetFileName(info.File));
            await Task.Run(() =>
            {
                File.Copy(info.File, target + ".tmp", overwrite: true);
                File.Move(target + ".tmp", target, overwrite: true);
            });
            BackupArchive.Prune(dir, keep);
            // Manual backups are not pruned in the server folder, but the copy folder must not grow forever.
            var all = Directory.GetFiles(dir, "*" + BackupArchive.Extension).OrderByDescending(File.GetLastWriteTimeUtc).Skip(keep * 2);
            foreach (var f in all) File.Delete(f);
            ServerManager.ServerLog(def, $"Backup copied to {dir}");
        }
        catch (Exception e) { ServerManager.ServerLog(def, $"Backup copy to {copyDir} failed: {e.Message}"); }
    }

    public void Delete(long id, string name, string by)
    {
        var def = manager.Definition(id);
        var b = BackupArchive.Find(def, name) ?? throw new ServerException("backup_not_found");
        File.Delete(b.File);
        ServerManager.ServerLog(def, $"Backup {name} deleted by {by}");
    }

    public string FileOf(long id, string name) =>
        (BackupArchive.Find(manager.Definition(id), name) ?? throw new ServerException("backup_not_found")).File;

    /// <summary>Restores a backup (server must be stopped). A safety backup of the current state is made first.</summary>
    public Task RestoreAsync(long id, string name, bool restoreConfig, bool restoreEamData, string by) =>
        manager.RunExclusiveAsync(id, "restore", requireStopped: true, async (def, rt) =>
        {
            var backup = BackupArchive.Find(def, name) ?? throw new ServerException("backup_not_found");
            var gate = Gate(id);
            if (!await gate.WaitAsync(0)) throw new ServerException("backup_running");
            try
            {
                rt.ProgressText = "backup_before_restore";
                await manager.PushAsync(id);
                await CreateLockedAsync(def, "before-restore", by, $"before restoring {name}");
                rt.ProgressText = "restoring";
                await manager.PushAsync(id);
                var dbFile = restoreEamData ? def.DatabaseFile : null;
                var game = await Task.Run(() => BackupArchive.Restore(def, backup, restoreConfig, dbFile));
                if (restoreEamData) live.ForgetData(def.DatabaseFile);
                if (restoreConfig && File.Exists(def.YamlFile))
                {
                    // Ports, name and telnet password stay those of this server, whatever the backup contains.
                    var y = DedicatedYaml.Load(def.YamlFile);
                    ServerManager.ApplyManagedKeys(y, def);
                    y.Save(def.YamlFile);
                }
                if (game is not null && File.Exists(def.YamlFile) && !restoreConfig)
                {
                    var y = DedicatedYaml.Load(def.YamlFile);
                    if (y.Get("GameConfig", "GameName") != game) y.Set("GameConfig", "GameName", game).Save(def.YamlFile);
                }
                ServerManager.ServerLog(def, $"Backup {name} restored by {by} (save '{game ?? "?"}')");
            }
            finally
            {
                rt.ProgressText = null;
                gate.Release();
            }
        });
}
