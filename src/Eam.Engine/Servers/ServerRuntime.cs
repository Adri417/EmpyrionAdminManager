using System.Text.Json.Serialization;

namespace Eam.Engine.Servers;

[JsonConverter(typeof(JsonStringEnumConverter<ServerState>))]
public enum ServerState { NotInstalled, Installing, Updating, Stopped, Starting, Running, Stopping, Crashed }

/// <summary>Live state of one server, kept in memory by the engine.</summary>
public sealed class ServerRuntime
{
    public ServerState State { get; set; } = ServerState.Stopped;
    /// <summary>What the admin wants: true after Start, false after Stop. Drives crash detection.</summary>
    public bool WantRunning { get; set; }
    public string? Operation { get; set; }
    public double? Progress { get; set; }
    public string? ProgressText { get; set; }
    public string? LastError { get; set; }
    public int? ProcessId { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? StartRequestedUtc { get; set; }
    public List<DateTime> RecentCrashes { get; } = [];
    public bool AutoRestartSuspended { get; set; }
    public SemaphoreSlim Lock { get; } = new(1, 1);
    public CancellationTokenSource? StopCountdown { get; set; }
    /// <summary>Last stop or forced stop asked by an admin: processes still closing are not a restart nor a crash.</summary>
    public DateTime? ManualStopUtc { get; set; }
}
