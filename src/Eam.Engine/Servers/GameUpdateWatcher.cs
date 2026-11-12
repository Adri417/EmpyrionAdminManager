using Eam.Core.Servers;

namespace Eam.Engine.Servers;

/// <summary>
/// Asks Steam every 10 minutes whether a newer build of the dedicated server exists (the interface shows
/// "game update available"). Per server (Gameplay tab): "notify" only writes it in the server journal,
/// "auto" updates the server by itself: 5 minute countdown with red alerts, kick at 1 minute, backup,
/// SteamCMD update, restart. A build that failed is tried again at the next checks, 3 times at most (no update loop).
/// </summary>
public sealed class GameUpdateWatcher(ServerManager manager, LiveService live, BackupService backups, ILogger<GameUpdateWatcher> log) : BackgroundService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, string> _handled = new();
    private readonly Dictionary<(long, string), int> _tries = [];

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try { await CheckAsync(stop); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogInformation("Game update check failed: {Error}", e.Message); }
            try { await Task.Delay(TimeSpan.FromMinutes(10), stop); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task CheckAsync(CancellationToken stop)
    {
        var servers = manager.Definitions().Where(d => d.Installed).ToList();
        if (servers.Count == 0) return;
        // SteamCMD may be missing (server copied from EAH, data moved): without it Steam cannot be asked.
        if (!manager.Steam.IsInstalled) await manager.Steam.EnsureInstalledAsync(null, stop);
        var builds = await manager.Steam.CheckLatestBuildsAsync(stop);
        log.LogInformation("Latest dedicated server builds on Steam: {Builds}", string.Join(", ", builds.Select(b => $"{b.Key} {b.Value}")));
        if (builds.Count == 0) return;
        foreach (var def in servers)
        {
            if (!builds.TryGetValue(def.Branch, out var latest) || manager.InstalledBuild(def) is not { Length: > 0 } installed || installed == latest) continue;
            if (_handled.TryGetValue(def.Id, out var done) && done == latest) continue;
            _handled[def.Id] = latest;
            var mode = FeatureStore.Load<GameplaySettings>(live.Data(def), "gameplay");
            if (mode.GameUpdateMode == "off") continue;
            ServerManager.ServerLog(def, $"Game update available on Steam: build {installed} -> {latest} ({def.Branch})");
            if (mode.GameUpdateMode != "auto") continue;
            var rt = manager.Runtime(def.Id);
            if (rt.Operation is not null || rt.State is not (ServerState.Running or ServerState.Stopped)) { _handled.TryRemove(def.Id, out _); continue; } // busy: next hour
            var running = rt.State == ServerState.Running;
            var tries = _tries.TryGetValue((def.Id, latest), out var n) ? n : 0;
            if (tries >= 3) continue; // failed 3 times: the admin is told in the journal, no loop
            _tries[(def.Id, latest)] = tries + 1;
            ServerManager.ServerLog(def, $"Automatic game update to build {latest} (attempt {tries + 1}/3)");
            _ = Task.Run(async () =>
            {
                try
                {
                    await manager.RestartWithAsync(def.Id, running ? 5 : 0, "Mise a jour du jeu / Game update", "EAM (auto update)", "update", async (d, r) =>
                    {
                        if (mode.GameUpdateBackupFirst) await backups.CreateAsync(d.Id, "before-update", "EAM (auto update)");
                        await manager.UpdateFilesAsync(d, r, "EAM (auto update)");
                    });
                }
                catch (Exception e)
                {
                    ServerManager.ServerLog(def, "Automatic game update failed: " + e.Message);
                    _handled.TryRemove(def.Id, out _); // tried again at the next check
                }
            }, CancellationToken.None);
        }
    }
}
