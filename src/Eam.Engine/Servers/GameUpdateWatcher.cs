namespace Eam.Engine.Servers;

/// <summary>
/// Asks Steam every 3 hours whether a newer build of the dedicated server exists, so the interface can
/// show "game update available" on each server. Nothing is downloaded: updating stays the admin's choice
/// (or a scheduled "update" task).
/// </summary>
public sealed class GameUpdateWatcher(ServerManager manager, ILogger<GameUpdateWatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (manager.Definitions().Any(d => d.Installed))
                {
                    var builds = await manager.Steam.CheckLatestBuildsAsync(stop);
                    log.LogInformation("Latest dedicated server builds on Steam: {Builds}", string.Join(", ", builds.Select(b => $"{b.Key} {b.Value}")));
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogInformation("Game update check failed: {Error}", e.Message); }
            try { await Task.Delay(TimeSpan.FromHours(3), stop); } catch (OperationCanceledException) { return; }
        }
    }
}
