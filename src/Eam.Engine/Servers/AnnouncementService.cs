using Eam.Core.Servers;

namespace Eam.Engine.Servers;

/// <summary>
/// Automatic announcements (Announcements tab): every N minutes the next message of the list is sent in game,
/// then the one after, and so on, back to the first one at the end.
/// </summary>
public sealed class AnnouncementService(ServerManager manager, LiveService live, ILogger<AnnouncementService> log) : BackgroundService
{
    private readonly Dictionary<long, (DateTime LastUtc, int Next)> _state = [];

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (await timer.WaitForNextTickAsync(stop))
        {
            foreach (var def in manager.Definitions())
            {
                try { await TickAsync(def); }
                catch (Exception e) { log.LogDebug("Announcement for {Name} failed: {Error}", def.Name, e.Message); }
            }
        }
    }

    private async Task TickAsync(ServerDefinition def)
    {
        if (manager.Runtime(def.Id).State != ServerState.Running) return;
        var s = FeatureStore.Load<CommunitySettings>(live.Data(def), "community");
        var list = s.Announcements.Where(a => a.Text.Trim().Length > 0).ToList();
        if (!s.AnnouncementsEnabled || list.Count == 0) { _state.Remove(def.Id); return; }
        if (s.AnnouncementsOnlyWithPlayers && live.OnlinePlayers(def.Id).Count == 0) return;
        var now = DateTime.UtcNow;
        // First message one interval after switching on (or after an EAM restart), not at once.
        if (!_state.TryGetValue(def.Id, out var st)) { _state[def.Id] = (now, 0); return; }
        if (now - st.LastUtc < TimeSpan.FromMinutes(s.AnnouncementMinutes)) return;
        var i = st.Next % list.Count;
        _state[def.Id] = (now, i + 1);
        await live.SendAsync(def.Id, list[i].Text.Trim(), s.AnnouncementMode, null, "EAM", asServer: true);
        ServerManager.ServerLog(def, $"Announcement {i + 1}/{list.Count} sent");
    }
}
