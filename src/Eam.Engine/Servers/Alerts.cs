using Eam.Core.Servers;
using Eam.Engine.Api;

namespace Eam.Engine.Servers;

/// <summary>
/// One place for everything an admin should notice: written to the server's journal, pushed to the open windows
/// (with a sound for severity 2) and sent to the Discord admin channel when one is set.
/// </summary>
public sealed class Alerts(LiveService live, LiveHub hub, ILogger<Alerts> log)
{
    /// <summary>Set by the Discord service.</summary>
    public Func<ServerDefinition, string, Task>? ToDiscordAdmin { get; set; }

    public async Task RaiseAsync(ServerDefinition def, string category, int severity, string text, string? steamId = null, string? name = null, string? playfield = null)
    {
        try
        {
            var entry = live.Data(def).AddJournal(category, severity, text, steamId, name, playfield);
            await hub.BroadcastAsync(new { type = "alert", id = def.Id, server = def.Name, entry }, CancellationToken.None);
            if (severity >= 1 && ToDiscordAdmin is { } discord)
                await discord(def, $"[{def.Name}] {category}: {(name is null ? "" : name + " — ")}{text}");
        }
        catch (Exception e) { log.LogWarning("Alert not recorded: {Err}", e.Message); }
    }
}
