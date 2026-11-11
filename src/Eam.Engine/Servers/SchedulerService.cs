using System.Collections.Concurrent;
using System.Text.Json;
using Eam.Core.Servers;
using Eam.Engine.Api;

namespace Eam.Engine.Servers;

/// <summary>Parameters of a scheduled task (only the ones its kind uses are read).</summary>
public sealed class TaskParams
{
    public int WarnMinutes { get; set; } = 5;
    public string? Message { get; set; }
    public string? Text { get; set; }
    public string? Mode { get; set; }
    public string? Command { get; set; }
    public List<string> Playfields { get; set; } = [];
    public string? Wipe { get; set; }
    public bool BackupFirst { get; set; }
    public int LogDays { get; set; } = 14;
    /// <summary>Wipe: playfields never wiped by "*" (all) or by a solar system.</summary>
    public List<string> Exclude { get; set; } = [];
    /// <summary>Wipe: every playfield of this solar system (from the galaxy and the structures seen there).</summary>
    public string? SolarSystem { get; set; }
    /// <summary>Old players / inventory copies: older than this many days.</summary>
    public int Days { get; set; } = 90;
    /// <summary>Sequence: steps run one after the other, with a pause after each.</summary>
    public List<TaskStep> Steps { get; set; } = [];
}

public sealed class TaskStep
{
    public string Kind { get; set; } = "message";
    public TaskParams? Params { get; set; }
    public int DelaySeconds { get; set; }
}

public sealed record TaskRequest(string Kind, bool Enabled, JsonElement Schedule, JsonElement? Params);

/// <summary>
/// Step 4: scheduled tasks of each server (restarts, stops, starts, backups, messages, console commands,
/// wipes, cleanups, game updates). Checked every 15 seconds; a task late by more than 10 minutes
/// (EAM was not running) is skipped instead of running at the wrong time.
/// </summary>
public sealed class SchedulerService(ServerManager manager, LiveService live, BackupService backups, WorldService world, LiveHub hub, ILogger<SchedulerService> log) : BackgroundService
{
    public static readonly string[] Kinds = ["restart", "stop", "start", "backup", "message", "command", "wipe", "cleanup", "update",
        "sequence", "backup_structures", "shared_cleanup", "limits", "cleanup_players", "cleanup_inventories"];
    public static readonly string[] WipeKinds = ["deposit", "poi", "terrain", "all"];
    private static readonly TimeSpan MaxLate = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ConcurrentDictionary<long, DateTime> _baseline = new();
    private readonly ConcurrentDictionary<long, Task> _running = new();

    public object List(long id)
    {
        var def = manager.Definition(id);
        return live.Data(def).Tasks().Select(t => Describe(t)).ToList();
    }

    private object Describe(ScheduledTask t)
    {
        DateTime? next = null;
        try { if (t.Enabled) next = Next(t)?.ToUniversalTime(); } catch { }
        return new
        {
            t.Id, t.Kind, t.Enabled, schedule = JsonDocument.Parse(t.Schedule).RootElement, @params = JsonDocument.Parse(t.Params).RootElement,
            t.LastRunUtc, t.LastResult, nextRunUtc = next, running = _running.ContainsKey(t.Id),
        };
    }

    private DateTime? Next(ScheduledTask t)
    {
        var s = Schedule.Parse(t.Schedule);
        var from = t.LastRunUtc?.ToLocalTime() ?? _baseline.GetOrAdd(t.Id, _ => DateTime.Now);
        var n = s.NextAfter(from);
        return n == DateTime.MaxValue ? null : n;
    }

    /// <summary>Next time (local) a task of this kind runs, for the chatbot (CB:NextRestart, CB:NextWipe).</summary>
    public DateTime? NextRun(long id, string kind)
    {
        var def = manager.Definition(id);
        DateTime? best = null;
        foreach (var t in live.Data(def).Tasks().Where(t => t.Enabled && t.Kind == kind))
        {
            DateTime? n;
            try { n = Next(t); } catch { continue; }
            if (n is { } v && (best is null || v < best)) best = v;
        }
        return best;
    }

    public long Save(long id, long taskId, TaskRequest req, string by)
    {
        var def = manager.Definition(id);
        if (!Kinds.Contains(req.Kind)) throw new ServerException("invalid_task_kind");
        Schedule schedule;
        try { schedule = Schedule.Parse(req.Schedule.GetRawText()); }
        catch (Exception e) when (e is FormatException or JsonException) { throw new ServerException(e is FormatException ? e.Message : "invalid_schedule"); }
        var p = req.Params is { ValueKind: JsonValueKind.Object } pe ? pe.Deserialize<TaskParams>(Json) ?? new TaskParams() : new TaskParams();
        Validate(req.Kind, p);
        var data = live.Data(def);
        if (taskId != 0 && data.Tasks().All(t => t.Id != taskId)) throw new ServerException("task_not_found");
        var newId = data.SaveTask(new ScheduledTask(taskId, req.Kind, req.Enabled, schedule.ToJson(), JsonSerializer.Serialize(p, Json), null, null));
        _baseline[newId] = DateTime.Now;
        ServerManager.ServerLog(def, $"Scheduled task {req.Kind} #{newId} saved by {by}");
        return newId;
    }

    private static void Validate(string kind, TaskParams p)
    {
        p.WarnMinutes = Math.Clamp(p.WarnMinutes, 0, 60);
        if (p.Message?.Length > 120) throw new ServerException("message_too_long");
        switch (kind)
        {
            case "message":
                if (string.IsNullOrWhiteSpace(p.Text) || p.Text.Length > 300) throw new ServerException("message_empty");
                if (p.Mode is not ("chat" or "popup" or "alert")) p.Mode = "chat";
                break;
            case "command":
                if (string.IsNullOrWhiteSpace(p.Command) || p.Command.Length > 500 || p.Command.IndexOfAny(['\r', '\n']) >= 0) throw new ServerException("invalid_command");
                break;
            case "wipe":
                if (!WipeKinds.Contains(p.Wipe)) throw new ServerException("invalid_wipe");
                p.Playfields = p.Playfields.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
                p.Exclude = p.Exclude.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
                p.SolarSystem = string.IsNullOrWhiteSpace(p.SolarSystem) ? null : p.SolarSystem.Trim();
                if (p.Playfields.Count == 0 && p.SolarSystem is null || p.Playfields.Concat(p.Exclude).Any(x => x.Length > 80 || x.IndexOfAny(['\'', '\r', '\n']) >= 0))
                    throw new ServerException("invalid_playfields");
                break;
            case "cleanup_players":
            case "cleanup_inventories":
                p.Days = Math.Clamp(p.Days, 1, 3650);
                break;
            case "sequence":
                if (p.Steps.Count is 0 or > 20 || p.Steps.Any(x => x.Kind is "sequence" || !Kinds.Contains(x.Kind) || x.DelaySeconds is < 0 or > 3600))
                    throw new ServerException("invalid_steps");
                foreach (var step in p.Steps) { step.Params ??= new TaskParams(); Validate(step.Kind, step.Params); }
                break;
            case "cleanup":
                p.LogDays = Math.Clamp(p.LogDays, 1, 365);
                break;
        }
    }

    public void Delete(long id, long taskId, string by)
    {
        var def = manager.Definition(id);
        live.Data(def).DeleteTask(taskId);
        ServerManager.ServerLog(def, $"Scheduled task #{taskId} deleted by {by}");
    }

    /// <summary>Runs a task now (button "Run now").</summary>
    public void RunNow(long id, long taskId, string by)
    {
        var def = manager.Definition(id);
        var t = live.Data(def).Tasks().FirstOrDefault(x => x.Id == taskId) ?? throw new ServerException("task_not_found");
        if (_running.ContainsKey(t.Id)) throw new ServerException("task_running");
        Launch(def, t, by);
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stop))
        {
            foreach (var def in manager.Definitions())
            {
                try
                {
                    foreach (var t in live.Data(def).Tasks().Where(t => t.Enabled))
                    {
                        if (_running.ContainsKey(t.Id)) continue;
                        DateTime? due;
                        try { due = Next(t); } catch { continue; }
                        if (due is null || due > DateTime.Now) continue;
                        if (DateTime.Now - due > MaxLate)
                        {
                            live.Data(def).TaskRan(t.Id, DateTime.UtcNow, "skipped_late");
                            continue;
                        }
                        Launch(def, t, "scheduler");
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Scheduler check of '{Name}' failed", def.Name); }
            }
        }
    }

    private void Launch(ServerDefinition def, ScheduledTask t, string by)
    {
        // Marked as run right away: a long task (restart with warnings) is not started twice.
        live.Data(def).TaskRan(t.Id, DateTime.UtcNow, "running");
        _running[t.Id] = Task.Run(async () =>
        {
            string result;
            try
            {
                result = await RunAsync(def, t, by);
            }
            catch (ServerException e) { result = "error:" + e.Code + (e.Detail is null ? "" : " " + e.Detail); }
            catch (Exception e) { result = "error:" + e.Message; log.LogError(e, "Task {Kind} of '{Name}' failed", t.Kind, def.Name); }
            try { live.Data(def).TaskRan(t.Id, DateTime.UtcNow, result.Length > 300 ? result[..300] : result); } catch { }
            ServerManager.ServerLog(def, $"Scheduled task {t.Kind} #{t.Id} ({by}): {result}");
            _running.TryRemove(t.Id, out _);
            await hub.BroadcastAsync(new { type = "tasks", id = def.Id }, CancellationToken.None);
        });
        _ = hub.BroadcastAsync(new { type = "tasks", id = def.Id }, CancellationToken.None);
    }

    private async Task<string> RunAsync(ServerDefinition def, ScheduledTask t, string by)
    {
        var p = JsonSerializer.Deserialize<TaskParams>(t.Params, Json) ?? new TaskParams();
        var rt = manager.Runtime(def.Id);
        var running = rt.State == ServerState.Running;
        var who = by == "scheduler" ? "scheduler" : by + " (task)";
        switch (t.Kind)
        {
            case "restart":
                if (!running) return "skipped_not_running";
                await manager.RestartWithAsync(def.Id, p.WarnMinutes, p.Message, who, "restart",
                    p.BackupFirst ? async (d, _) => await backups.CreateAsync(d.Id, "scheduled", who, "restart") : null);
                return "ok";
            case "stop":
                if (!running) return "skipped_not_running";
                await manager.StopAsync(def.Id, p.WarnMinutes, p.Message, who);
                return "ok";
            case "start":
                if (rt.State is ServerState.Running or ServerState.Starting) return "skipped_running";
                await manager.StartAsync(def.Id, who);
                return "ok";
            case "backup":
                var b = await backups.CreateAsync(def.Id, "scheduled", who);
                return "ok " + b.Name;
            case "message":
                if (!running) return "skipped_not_running";
                await live.SendAsync(def.Id, p.Text ?? "", p.Mode ?? "chat", null, who, asServer: true);
                return "ok";
            case "command":
                if (!running) return "skipped_not_running";
                var output = await manager.ConsoleAsync(def.Id, p.Command ?? "", who);
                return "ok " + output.Replace("\r", " ").Replace("\n", " ").Trim();
            case "wipe":
            {
                if (!running) return "skipped_not_running";
                var targets = WipeTargets(def, p);
                if (targets.Count == 0) return "skipped_no_playfield";
                if (p.BackupFirst) await backups.CreateAsync(def.Id, "before-wipe", who, string.Join(", ", targets));
                var done = new List<string>();
                foreach (var pf in targets)
                {
                    await manager.ConsoleAsync(def.Id, $"wipe '{pf}' {p.Wipe}", who);
                    done.Add(pf);
                }
                return $"ok {p.Wipe}: {string.Join(", ", done)}";
            }
            case "cleanup":
                return "ok " + Cleanup(def, p.LogDays);
            case "update":
                await manager.RestartWithAsync(def.Id, running ? p.WarnMinutes : 0, p.Message ?? "Server update", who, "update", async (d, r) =>
                {
                    if (p.BackupFirst) await backups.CreateAsync(d.Id, "before-update", who);
                    await manager.UpdateFilesAsync(d, r, who);
                });
                return "ok";
            case "sequence":
            {
                var results = new List<string>();
                foreach (var step in p.Steps)
                {
                    var r = await RunAsync(def, new ScheduledTask(t.Id, step.Kind, true, t.Schedule, JsonSerializer.Serialize(step.Params ?? new TaskParams(), Json), null, null), by);
                    results.Add($"{step.Kind}: {r}");
                    if (r.StartsWith("error", StringComparison.Ordinal)) break;
                    if (step.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(step.DelaySeconds));
                }
                return "ok " + string.Join(" | ", results);
            }
            case "backup_structures":
                var bs = await backups.CreateAsync(def.Id, "structures", who);
                return "ok " + bs.Name;
            case "shared_cleanup":
                if (!running) return "skipped_not_running";
                await world.RefreshIfEmptyAsync(def.Id);
                return "ok " + JsonSerializer.Serialize(world.SharedClean(def.Id, who), Json);
            case "limits":
                if (!running) return "skipped_not_running";
                await world.RefreshIfEmptyAsync(def.Id);
                return "ok " + JsonSerializer.Serialize(world.Violations(def.Id), Json).Length + " bytes of report (see Structures > Rules)";
            case "cleanup_players":
                return "ok " + live.Data(def).ForgetInactivePlayers(p.Days) + " rows";
            case "cleanup_inventories":
                return "ok " + live.Data(def).PruneInventorySnapshots(p.Days) + " copies";
            default:
                return "error:unknown_kind";
        }
    }

    /// <summary>Playfields of a wipe task: the list ("*" = every known playfield), a solar system, minus the exclusions.</summary>
    private List<string> WipeTargets(ServerDefinition def, TaskParams p)
    {
        var data = live.Data(def);
        var all = data.Structures().Select(x => x.Playfield).Where(x => !string.IsNullOrEmpty(x));
        var list = new List<string>();
        foreach (var pf in p.Playfields)
        {
            if (pf == "*") list.AddRange(all);
            else list.Add(pf);
        }
        if (p.SolarSystem is { } ss)
        {
            list.AddRange(data.Structures().Where(x => string.Equals(x.SolarSystem, ss, StringComparison.OrdinalIgnoreCase)).Select(x => x.Playfield));
            try
            {
                var galaxy = GalaxyReader.Read(GalaxyReader.GlobalDb(def, WorldService.SaveName(def)));
                var sys = galaxy.FirstOrDefault(g => string.Equals(g.Name, ss, StringComparison.OrdinalIgnoreCase));
                if (sys is not null) list.AddRange(sys.Playfields.Select(x => x.Name));
            }
            catch { /* galaxy not readable: structures only */ }
        }
        return list.Where(x => !p.Exclude.Any(e => FeatureStore.MatchesPlayfield(e, x)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Removes game log folders and EAM logs older than <paramref name="days"/> days.</summary>
    private static string Cleanup(ServerDefinition def, int days)
    {
        var limit = DateTime.UtcNow.AddDays(-days);
        var removed = 0;
        var logs = Path.Combine(def.GameDir, "Logs");
        if (Directory.Exists(logs))
            foreach (var d in Directory.GetDirectories(logs))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(d) < limit) { Directory.Delete(d, true); removed++; }
                }
                catch { }
            }
        foreach (var f in Directory.Exists(def.LogsDir) ? Directory.GetFiles(def.LogsDir, "*.log.*") : [])
        {
            try { if (File.GetLastWriteTimeUtc(f) < limit) { File.Delete(f); removed++; } } catch { }
        }
        return $"{removed} removed";
    }
}
