using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Eam.Core.Config;
using Eam.Core.Servers;
using Eam.Engine.Api;

namespace Eam.Engine.Servers;

public sealed class ServerException(string code, string? detail = null) : Exception(detail ?? code)
{
    public string Code { get; } = code;
    public string? Detail { get; } = detail;
}

public sealed record CreateServerRequest(
    string Name, int? GamePort, int? TelnetPort, int MaxPlayers, string? Password, string? Description,
    string Branch, bool Public, bool AutoStart, bool AutoRestart);

public sealed record ServerSettingsRequest(
    int GamePort, int TelnetPort, int MaxPlayers, string? Password, string? Description,
    string Branch, bool Public, bool AutoStart, bool AutoRestart);

/// <summary>
/// Installs, starts, stops and watches every game server. Each server has its own lock,
/// so two heavy operations (update, start, stop, delete…) never run on it at the same time.
/// </summary>
public sealed class ServerManager : BackgroundService
{
    private readonly ServerStore _store;
    private readonly EamPaths _paths;
    private readonly SteamCmd _steam;
    private readonly LiveHub _hub;
    private readonly ILogger<ServerManager> _log;
    private readonly ConcurrentDictionary<long, ServerRuntime> _runtime = new();
    private static readonly SemaphoreSlim CreateGate = new(1, 1);

    public const int MaxCrashesBeforeSuspend = 3;
    public static readonly TimeSpan CrashWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(15);

    public ServerManager(ServerStore store, EamPaths paths, LiveHub hub, ILogger<ServerManager> log, IHttpClientFactory httpFactory)
    {
        _store = store;
        _paths = paths;
        _hub = hub;
        _log = log;
        _steam = new SteamCmd(paths.SteamCmdDir, httpFactory.CreateClient("steam"));
    }

    // ------------------------------------------------------------------ queries

    public ServerRuntime Runtime(long id) => _runtime.GetOrAdd(id, _ => new ServerRuntime());

    public IReadOnlyList<object> Describe() => _store.List().Select(Describe).ToList();

    public object Describe(ServerDefinition s)
    {
        var rt = Runtime(s.Id);
        DedicatedYaml? yaml = null;
        try { yaml = File.Exists(s.YamlFile) ? DedicatedYaml.Load(s.YamlFile) : ReadPending(s)?.ToYaml(); } catch { }
        string? yamlValue(string key) => yaml?.Get("ServerConfig", key);
        return new
        {
            id = s.Id, slug = s.Slug, name = s.Name, displayName = s.DisplayName, listName = s.ListName, rootDir = s.RootDir, gamePort = s.GamePort, telnetPort = s.TelnetPort, modPort = s.ModPort,
            branch = s.Branch, autoStart = s.AutoStart, autoRestart = s.AutoRestart, installed = s.Installed, buildId = s.BuildId,
            latestBuild = _steam.LatestBuilds.GetValueOrDefault(s.Branch),
            gameUpdateAvailable = s.Installed && s.BuildId is { Length: > 0 } b0 && _steam.LatestBuilds.TryGetValue(s.Branch, out var lb) && lb != b0
                && long.TryParse(lb, out var lbn) && long.TryParse(b0, out var b0n) && lbn > b0n,
            createdUtc = s.CreatedUtc,
            maxPlayers = int.TryParse(yamlValue("Srv_MaxPlayers"), out var mp) ? mp : (int?)null,
            description = yamlValue("Srv_Description"),
            hasPassword = !string.IsNullOrEmpty(yamlValue("Srv_Password")),
            isPublic = !string.Equals(yamlValue("Srv_Public"), "false", StringComparison.OrdinalIgnoreCase),
            state = rt.State, operation = rt.Operation, progress = rt.Progress, progressText = rt.ProgressText,
            lastError = rt.LastError, processId = rt.ProcessId, startedUtc = rt.StartedUtc,
            autoRestartSuspended = rt.AutoRestartSuspended, recentCrashes = rt.RecentCrashes.Count,
        };
    }

    public object SuggestPorts()
    {
        var s = PortAllocator.Suggest(_store.List());
        return new { gamePort = s.GamePort, telnetPort = s.TelnetPort };
    }

    private ServerDefinition Get(long id) => _store.Find(id) ?? throw new ServerException("not_found");

    public ServerDefinition Definition(long id) => Get(id);

    public IReadOnlyList<ServerDefinition> Definitions() => _store.List();

    /// <summary>True while a server install, update, copy or backup holds its lock (used before a program update).</summary>
    public bool AnyBusy() => _store.List().Any(s => Runtime(s.Id).Operation is not null);

    /// <summary>Runs an operation with the server's lock (nothing else runs on it meanwhile).</summary>
    public Task RunExclusiveAsync(long id, string operation, bool requireStopped, Func<ServerDefinition, ServerRuntime, Task> action) =>
        WithLock(id, operation, async (def, rt) =>
        {
            if (requireStopped) RequireStopped(rt, allowNotInstalled: false);
            await action(def, rt);
        });

    /// <summary>Progress of a long job shown on the server (backup, download…), without taking the lock.</summary>
    public async Task ReportProgressAsync(long id, double? percent, string? text)
    {
        var rt = Runtime(id);
        rt.Progress = percent;
        rt.ProgressText = text;
        await Push(id);
    }

    /// <summary>
    /// Restart that runs <paramref name="whileStopped"/> between the stop and the start (backup, update…).
    /// The server is started again even if that step fails.
    /// </summary>
    public Task RestartWithAsync(long id, int delayMinutes, string? message, string by, string operation,
        Func<ServerDefinition, ServerRuntime, Task>? whileStopped, bool startAfter = true) => WithLock(id, operation, async (def, rt) =>
    {
        var wasUp = rt.State is ServerState.Running or ServerState.Starting;
        if (wasUp) await StopInternalAsync(def, rt, delayMinutes, message, by);
        Exception? failure = null;
        if (whileStopped is not null)
        {
            try { await whileStopped(def, rt); }
            catch (Exception e) { failure = e; ServerLog(def, $"{operation}: step while stopped failed: {e.Message}"); }
        }
        if (startAfter && wasUp) await StartInternalAsync(Get(id), rt, by);
        if (failure is not null) throw failure is ServerException ? failure : new ServerException("step_failed", failure.Message);
    });

    /// <summary>SteamCMD update of the game files, used by the scheduler inside <see cref="RestartWithAsync"/>.</summary>
    public async Task UpdateFilesAsync(ServerDefinition def, ServerRuntime rt, string by)
    {
        rt.State = ServerState.Updating;
        await Push(def.Id);
        try
        {
            var build = await _steam.InstallOrUpdateAsync(def.GameDir, def.Branch, false, null, line => ServerLog(def, "[steamcmd] " + line), CancellationToken.None);
            _store.Update(def with { BuildId = build ?? def.BuildId });
            ServerLog(def, $"Game files updated by {by} (build {build ?? "?"})");
        }
        finally { rt.State = ServerState.Stopped; }
    }

    public SteamCmd Steam => _steam;

    // ------------------------------------------------------------------ create / rename / settings / delete

    public async Task<ServerDefinition> CreateAsync(CreateServerRequest req, string by)
    {
        var name = CleanName(req.Name);
        ValidateCommon(req.MaxPlayers, req.Branch, req.Description, req.Password);
        await CreateGate.WaitAsync();
        try
        {
            var all = _store.List();
            if (all.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ServerException("name_taken");
            var slug = UniqueSlug(Slug.From(name), all);
            var root = Path.Combine(ServersParent(null), slug);
            EnsureFolderFree(root, all);

            var suggestion = PortAllocator.Suggest(all);
            var gamePort = req.GamePort ?? suggestion.GamePort;
            var telnetPort = req.TelnetPort ?? (req.GamePort is { } gp ? gp + 4 : suggestion.TelnetPort);
            if (PortAllocator.Check(gamePort, telnetPort, all) is { } portError) throw new ServerException(portError);

            Directory.CreateDirectory(root);
            var def = _store.Add(new ServerDefinition
            {
                Slug = slug, Name = name, RootDir = root, GamePort = gamePort, TelnetPort = telnetPort,
                TelnetPassword = NewSecret(), Branch = NormalizeBranch(req.Branch),
                AutoStart = req.AutoStart, AutoRestart = req.AutoRestart, ModKey = NewModKey(),
            });
            Directory.CreateDirectory(def.GameDir);
            Directory.CreateDirectory(def.LogsDir);
            // Settings that need the game files are written after the installation.
            WritePending(def, new PendingSettings(req.MaxPlayers, req.Password, req.Description, req.Public));
            ServerLog(def, $"Server created by {by}: '{name}', ports {gamePort}-{gamePort + 3}, telnet {telnetPort}");
            _log.LogInformation("{By} created server '{Name}' in {Root}", by, name, root);
            var rt = Runtime(def.Id);
            rt.State = ServerState.NotInstalled;
            _ = InstallAsync(def.Id, validate: false, by);
            return def;
        }
        finally { CreateGate.Release(); }
    }

    /// <summary>
    /// Adds an Empyrion server that already exists on this machine (for example one run with EAH).
    /// copy = true copies the whole folder into EAM's servers folder (the original is not touched);
    /// copy = false uses the folder in place. Ports, telnet password and options come from its dedicated yaml.
    /// </summary>
    public async Task<ServerDefinition> AdoptAsync(string? name, string gameDir, string? yamlName, bool copy, string by, string? displayName = null, string? parentFolder = null)
    {
        gameDir = Path.GetFullPath((gameDir ?? "").Trim().Trim('"'));
        if (!Directory.Exists(gameDir)) throw new ServerException("not_found", gameDir);
        if (!File.Exists(Path.Combine(gameDir, "EmpyrionLauncher.exe")) && !File.Exists(Path.Combine(gameDir, "DedicatedServer", "EmpyrionDedicated.exe")))
            throw new ServerException("not_an_empyrion_server", gameDir);
        var yamlFile = new[] { yamlName, "dedicated-eam.yaml", "dedicated.yaml" }
            .Where(n => !string.IsNullOrWhiteSpace(n) && n!.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
            .Select(n => Path.Combine(gameDir, n!)).FirstOrDefault(File.Exists);
        var yaml = yamlFile is null ? DedicatedYaml.Default() : DedicatedYaml.Load(yamlFile);
        var listName = yaml.Get("ServerConfig", "Srv_Name");
        var cleanName = CleanName(string.IsNullOrWhiteSpace(name) ? listName ?? "Imported server" : name);
        // The name in the game list stays the one of the imported server unless another one is given.
        var cleanDisplay = CleanDisplayName(string.IsNullOrWhiteSpace(displayName) ? listName : displayName, cleanName);
        if (copy)
        {
            // Copying a save while the game writes it would give a broken copy: the old server must be stopped first.
            var running = FindGameProcesses(gameDir);
            var busy = running.Count > 0;
            foreach (var p in running) p.Dispose();
            if (busy) throw new ServerException("import_source_running");
        }
        await CreateGate.WaitAsync();
        try
        {
            var all = _store.List();
            if (all.Any(s => string.Equals(s.Name, cleanName, StringComparison.OrdinalIgnoreCase))) throw new ServerException("name_taken");
            foreach (var o in all)
                if (IsUnder(gameDir, o.GameDir) || IsUnder(o.GameDir, gameDir) || IsUnder(gameDir, o.RootDir)) throw new ServerException("folder_used_by_server");
            var gamePort = int.TryParse(yaml.Get("ServerConfig", "Srv_Port"), out var gp) ? gp : PortAllocator.Suggest(all).GamePort;
            var telnetPort = int.TryParse(yaml.Get("ServerConfig", "Tel_Port"), out var tp) ? tp : gamePort + 4;
            if (PortAllocator.Check(gamePort, telnetPort, all) is { } portError) throw new ServerException(portError);
            var telnetPwd = yaml.Get("ServerConfig", "Tel_Pwd") is { Length: >= 4 } pwd ? pwd : NewSecret();
            var slug = UniqueSlug(Slug.From(cleanName), all);
            var root = Path.Combine(ServersParent(parentFolder), slug);
            EnsureFolderFree(root, all);
            Directory.CreateDirectory(root);
            var def = _store.Add(new ServerDefinition
            {
                Slug = slug, Name = cleanName, DisplayName = cleanDisplay, RootDir = root, GamePort = gamePort, TelnetPort = telnetPort, TelnetPassword = telnetPwd,
                Branch = "public", AutoStart = false, AutoRestart = true, ModKey = NewModKey(), Installed = !copy,
                GameDirOverride = copy ? null : gameDir,
            });
            Directory.CreateDirectory(def.LogsDir);
            ServerLog(def, $"Server imported by {by} from {gameDir} ({(copy ? "copied" : "used in place")}), yaml {yamlFile ?? "default"}");
            var rt = Runtime(def.Id);
            if (!copy)
            {
                ApplyManagedKeys(yaml, def);
                yaml.Save(def.YamlFile);
                rt.State = ServerState.Stopped;
                await BroadcastListAsync();
                return def;
            }
            long needed = 0;
            try { needed = new DirectoryInfo(gameDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { }
            if (FreeBytes(root) is { } free && free < needed + 2L * 1024 * 1024 * 1024)
            {
                _store.Remove(def.Id);
                try { Directory.Delete(root, true); } catch { }
                throw new ServerException("not_enough_space", $"{needed / 1_073_741_824.0:0.0} GB / {free / 1_073_741_824.0:0.0} GB");
            }
            rt.State = ServerState.Installing;
            rt.Progress = 0;
            rt.ProgressText = "copy";
            await BroadcastListAsync();
            _ = Task.Run(async () =>
            {
                try
                {
                    var skipped = await Task.Run(() => CopyTree(gameDir, def.GameDir, rt));
                    // Paths of the old server written in its yaml (save folder...) now point into the copy:
                    // the copy must never play on the original's save.
                    foreach (var key in new[] { "SaveDirectory" })
                        if (yaml.Get("GameConfig", key) is { Length: > 0 } v0 && RebasePath(v0, gameDir, def.GameDir) is { } n0) yaml.Set("GameConfig", key, n0);
                        else if (yaml.Get("ServerConfig", key) is { Length: > 0 } v1 && RebasePath(v1, gameDir, def.GameDir) is { } n1) yaml.Set("ServerConfig", key, n1);
                    // A copied EAH plugin would run without EAH and get in EAM's way: put it aside (not deleted).
                    var mods = Path.Combine(def.GameDir, "Content", "Mods");
                    if (Directory.Exists(mods))
                        foreach (var mod in Directory.GetDirectories(mods))
                            if (IsEahMod(mod))
                            {
                                var aside = Path.Combine(def.EamDir, "mods-set-aside", Path.GetFileName(mod));
                                Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
                                Eam.Core.SafeFs.MoveDirectory(mod, aside);
                                ServerLog(def, $"EAH plugin {Path.GetFileName(mod)} set aside in {aside} (EAM replaces EAH)");
                            }
                    ApplyManagedKeys(yaml, def);
                    yaml.Save(def.YamlFile);
                    _store.Update(def with { Installed = true });
                    rt.State = ServerState.Stopped;
                    ServerLog(def, "Copy finished");
                    if (skipped.Count > 0)
                    {
                        ServerLog(def, "Not copied (Windows refused to read them): " + string.Join(", ", skipped));
                        rt.LastError = $"copy_skipped: {string.Join(", ", skipped.Take(5))}{(skipped.Count > 5 ? $" (+{skipped.Count - 5})" : "")}";
                    }
                }
                catch (Exception e)
                {
                    rt.State = ServerState.NotInstalled;
                    rt.LastError = e is ServerException se ? $"{se.Code}: {se.Detail}" : "copy_failed: " + e.Message;
                    ServerLog(def, "Copy failed: " + rt.LastError);
                }
                finally
                {
                    rt.Progress = null;
                    rt.ProgressText = null;
                    await BroadcastListAsync();
                }
            });
            return def;
        }
        finally { CreateGate.Release(); }
    }

    /// <summary>Copies a whole server folder. Files Windows does not let EAM read are left out and returned.</summary>
    private List<string> CopyTree(string from, string to, ServerRuntime rt)
    {
        var skipped = new List<string>();
        var files = new DirectoryInfo(from).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        var total = Math.Max(1, files.Sum(f => f.Length));
        long done = 0;
        var lastPush = DateTime.UtcNow;
        foreach (var f in files)
        {
            var rel = Path.GetRelativePath(from, f.FullName);
            var target = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try { f.CopyTo(target, overwrite: true); }
            catch (UnauthorizedAccessException) { skipped.Add(rel); }
            catch (IOException e) { throw new ServerException("copy_failed", $"{rel}: {e.Message}"); }
            done += f.Length;
            rt.Progress = Math.Round(done * 100.0 / total, 1);
            if (DateTime.UtcNow - lastPush > TimeSpan.FromSeconds(1)) { lastPush = DateTime.UtcNow; _ = BroadcastListAsync(); }
        }
        return skipped;
    }

    public async Task RenameAsync(long id, string newName, string by, string? displayName = null)
    {
        var name = CleanName(newName);
        var display = CleanDisplayName(displayName, name);
        await WithLock(id, "rename", async (def, rt) =>
        {
            RequireStopped(rt, allowNotInstalled: true);
            var all = _store.List().Where(s => s.Id != id).ToList();
            if (all.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ServerException("name_taken");
            var slug = Slug.From(name);
            var updated = def with { Name = name, DisplayName = display };
            if (!string.Equals(slug, def.Slug, StringComparison.OrdinalIgnoreCase))
            {
                slug = UniqueSlug(slug, all);
                var newRoot = Path.Combine(Path.GetDirectoryName(def.RootDir.TrimEnd('\\', '/'))!, slug);
                EnsureFolderFree(newRoot, all);
                if (CreatedByEam(def))
                {
                    Eam.Core.SafeFs.MoveDirectory(def.RootDir, newRoot);
                    updated = updated with { Slug = slug, RootDir = newRoot };
                }
            }
            if (File.Exists(updated.YamlFile)) DedicatedYaml.Load(updated.YamlFile).Set("ServerConfig", "Srv_Name", updated.ListName).Save(updated.YamlFile);
            _store.Update(updated);
            ServerLog(updated, $"Renamed by {by}: '{def.Name}' -> '{name}' (folder {updated.RootDir}, name in the game list '{updated.ListName}')");
            await Task.CompletedTask;
        });
    }

    public async Task<bool> UpdateSettingsAsync(long id, ServerSettingsRequest req, string by)
    {
        ValidateCommon(req.MaxPlayers, req.Branch, req.Description, req.Password);
        var appliesOnRestart = false;
        await WithLock(id, "settings", async (def, rt) =>
        {
            var portsChanged = req.GamePort != def.GamePort || req.TelnetPort != def.TelnetPort;
            if (portsChanged)
            {
                RequireStopped(rt, allowNotInstalled: true);
                var others = _store.List().Where(s => s.Id != id);
                if (PortAllocator.Check(req.GamePort, req.TelnetPort, others) is { } e) throw new ServerException(e);
            }
            var updated = def with
            {
                GamePort = req.GamePort, TelnetPort = req.TelnetPort, Branch = NormalizeBranch(req.Branch),
                AutoStart = req.AutoStart, AutoRestart = req.AutoRestart,
            };
            _store.Update(updated);
            if (File.Exists(updated.YamlFile))
            {
                var y = DedicatedYaml.Load(updated.YamlFile);
                ApplyManagedKeys(y, updated);
                y.Set("ServerConfig", "Srv_MaxPlayers", req.MaxPlayers.ToString());
                y.Set("ServerConfig", "Srv_Description", req.Description?.Trim());
                y.Set("ServerConfig", "Srv_Public", req.Public ? "true" : "false");
                if (req.Password is not null) y.Set("ServerConfig", "Srv_Password", req.Password.Trim());
                y.Save(updated.YamlFile);
            }
            else
            {
                // Not installed yet: keep the values until the installation creates the configuration.
                var old = ReadPending(updated);
                WritePending(updated, new PendingSettings(req.MaxPlayers, req.Password ?? old?.Password, req.Description, req.Public));
            }
            if (!req.AutoRestart) rt.AutoRestartSuspended = false;
            appliesOnRestart = rt.State is ServerState.Running or ServerState.Starting;
            ServerLog(updated, $"Settings changed by {by}");
            await Task.CompletedTask;
        });
        return appliesOnRestart;
    }

    public async Task DeleteAsync(long id, bool deleteFiles, string confirmName, string by)
    {
        await WithLock(id, "delete", async (def, rt) =>
        {
            RequireStopped(rt, allowNotInstalled: true);
            if (!string.Equals(confirmName?.Trim(), def.Name, StringComparison.Ordinal)) throw new ServerException("confirm_name_mismatch");
            if (FindGameProcesses(def).Count > 0) throw new ServerException("server_running");
            _store.Remove(id);
            if (deleteFiles)
            {
                // Safety: only ever delete a folder EAM created inside its own servers folder.
                if (!CreatedByEam(def)) throw new ServerException("unsafe_delete");
                await Task.Run(() => Directory.Delete(def.RootDir, recursive: true));
            }
            _log.LogWarning("{By} removed server '{Name}' (files {Files})", by, def.Name, deleteFiles ? "deleted" : "kept");
        });
        _runtime.TryRemove(id, out _);
        await BroadcastListAsync();
    }

    // ------------------------------------------------------------------ install / update

    public Task InstallAsync(long id, bool validate, string by) => WithLock(id, validate ? "validate" : "install", async (def, rt) =>
    {
        RequireStopped(rt, allowNotInstalled: true);
        var firstInstall = !def.Installed;
        rt.State = firstInstall ? ServerState.Installing : ServerState.Updating;
        rt.LastError = null;
        rt.Progress = 0;
        rt.ProgressText = null;
        await Push(def.Id);
        ServerLog(def, $"{(firstInstall ? "Install" : validate ? "Verify" : "Update")} started by {by} (branch {def.Branch})");
        var lastPush = DateTime.MinValue;
        var progress = new Progress<SteamProgress>(p =>
        {
            rt.Progress = p.Percent;
            rt.ProgressText = p.Stage;
            if ((DateTime.UtcNow - lastPush).TotalMilliseconds > 700) { lastPush = DateTime.UtcNow; _ = Push(def.Id); }
        });
        try
        {
            var build = await _steam.InstallOrUpdateAsync(def.GameDir, def.Branch, validate || firstInstall, progress, line => ServerLog(def, "[steamcmd] " + line), CancellationToken.None);
            var updated = def with { Installed = true, BuildId = build ?? def.BuildId };
            PrepareYaml(updated);
            _store.Update(updated);
            rt.State = ServerState.Stopped;
            ServerLog(updated, $"Game files ready (build {updated.BuildId ?? "?"})");
        }
        catch (Exception e)
        {
            rt.State = def.Installed ? ServerState.Stopped : ServerState.NotInstalled;
            rt.LastError = e is PlatformNotSupportedException ? "steamcmd_windows_only" : e.Message;
            ServerLog(def, "Install/update failed: " + e.Message);
            _log.LogError(e, "Install/update of '{Name}' failed", def.Name);
        }
        finally
        {
            rt.Progress = null;
            rt.ProgressText = null;
        }
    });

    /// <summary>Creates EAM's own dedicated-eam.yaml from the game's template and applies EAM-managed keys.</summary>
    private void PrepareYaml(ServerDefinition def)
    {
        DedicatedYaml y;
        var created = !File.Exists(def.YamlFile);
        if (!created) y = DedicatedYaml.Load(def.YamlFile);
        else if (File.Exists(def.ShippedYamlFile)) y = DedicatedYaml.Load(def.ShippedYamlFile);
        else y = DedicatedYaml.Default();

        ApplyManagedKeys(y, def);
        var pending = ReadPending(def);
        if (created && pending is not null) pending.ApplyTo(y);
        y.Save(def.YamlFile);
        File.Delete(PendingFile(def));
    }

    private sealed record PendingSettings(int MaxPlayers, string? Password, string? Description, bool Public)
    {
        public void ApplyTo(DedicatedYaml y)
        {
            y.Set("ServerConfig", "Srv_MaxPlayers", MaxPlayers.ToString());
            y.Set("ServerConfig", "Srv_Password", Password?.Trim());
            y.Set("ServerConfig", "Srv_Description", Description?.Trim());
            y.Set("ServerConfig", "Srv_Public", Public ? "true" : "false");
        }

        public DedicatedYaml ToYaml()
        {
            var y = DedicatedYaml.Parse("ServerConfig:\n");
            ApplyTo(y);
            return y;
        }
    }

    private static string PendingFile(ServerDefinition def) => Path.Combine(def.EamDir, "pending-settings.json");

    private static PendingSettings? ReadPending(ServerDefinition def)
    {
        try
        {
            var f = PendingFile(def);
            return File.Exists(f) ? System.Text.Json.JsonSerializer.Deserialize<PendingSettings>(File.ReadAllText(f)) : null;
        }
        catch { return null; }
    }

    private static void WritePending(ServerDefinition def, PendingSettings p)
    {
        var f = PendingFile(def);
        File.WriteAllText(f + ".tmp", System.Text.Json.JsonSerializer.Serialize(p));
        File.Move(f + ".tmp", f, overwrite: true);
    }

    public static void ApplyManagedKeys(DedicatedYaml y, ServerDefinition def)
    {
        y.Set("ServerConfig", "Srv_Name", def.ListName);
        y.Set("ServerConfig", "Srv_Port", def.GamePort.ToString());
        y.Set("ServerConfig", "Tel_Enabled", "true");
        y.Set("ServerConfig", "Tel_Port", def.TelnetPort.ToString());
        y.Set("ServerConfig", "Tel_Pwd", def.TelnetPassword);
    }

    // ------------------------------------------------------------------ start / stop / kill

    public Task StartAsync(long id, string by) => WithLock(id, "start", async (def, rt) =>
    {
        await StartInternalAsync(def, rt, by);
    });

    private async Task StartInternalAsync(ServerDefinition def, ServerRuntime rt, string by)
    {
        if (!def.Installed) throw new ServerException("not_installed");
        if (rt.State is ServerState.Running or ServerState.Starting) throw new ServerException("already_running");
        if (FindGameProcesses(def).Count > 0) throw new ServerException("already_running");
        if (!File.Exists(def.YamlFile)) PrepareYaml(def);
        def = InstallMod(def);
        var busy = def.AllPorts.Where(PortAllocator.IsPortInUse).ToList();
        if (busy.Count > 0) throw new ServerException("port_used_by_program", string.Join(", ", busy));

        var launcher = Path.Combine(def.GameDir, "EmpyrionLauncher.exe");
        if (!File.Exists(launcher)) throw new ServerException("game_files_missing", launcher);

        var psi = new ProcessStartInfo(launcher)
        {
            WorkingDirectory = def.GameDir,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-startDedi");
        psi.ArgumentList.Add("-dedicated");
        psi.ArgumentList.Add(Path.GetFileName(def.YamlFile));
        Process.Start(psi)?.Dispose();

        rt.WantRunning = true;
        rt.State = ServerState.Starting;
        rt.StartRequestedUtc = DateTime.UtcNow;
        rt.LastError = null;
        ServerLog(def, $"Start requested by {by}");
        _log.LogInformation("{By} started server '{Name}'", by, def.Name);
        await Task.CompletedTask;
    }

    /// <summary>Clean stop: warns players, then saveandexit, waits, and only kills as a last resort.</summary>
    public Task StopAsync(long id, int delayMinutes, string? message, string by) =>
        WithLock(id, "stop", (def, rt) => StopInternalAsync(def, rt, delayMinutes, message, by));

    public Task RestartAsync(long id, int delayMinutes, string? message, string by) => WithLock(id, "restart", async (def, rt) =>
    {
        await StopInternalAsync(def, rt, delayMinutes, message, by);
        await StartInternalAsync(def, rt, by);
    });

    private async Task StopInternalAsync(ServerDefinition def, ServerRuntime rt, int delayMinutes, string? message, string by)
    {
        delayMinutes = Math.Clamp(delayMinutes, 0, 60);
        rt.WantRunning = false;
        rt.ManualStopUtc = DateTime.UtcNow;
        if (FindGameProcesses(def).Count == 0) { rt.State = ServerState.Stopped; return; }

        rt.State = ServerState.Stopping;
        ServerLog(def, $"Stop requested by {by} (in {delayMinutes} min)");
        await Push(def.Id);

        using var cts = new CancellationTokenSource();
        rt.StopCountdown = cts;
        try
        {
            var text = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
            for (var left = delayMinutes; left > 0; left--)
            {
                // Like EAH: a message every minute of the last 5 (and at the start, 10 and 15 when longer),
                // then at 1 minute every player is kicked so the save happens with nobody in game.
                if (left == 1)
                {
                    await TrySay(def, "Deconnexion des joueurs, redemarrage dans 1 min / Players are being disconnected, restart in 1 min");
                    await KickEveryone(def);
                }
                else if (left == delayMinutes || left <= 5 || left is 10 or 15)
                    await TrySay(def, text is null
                        ? $"Redemarrage du serveur dans {left} min, deconnectez-vous ! / Server restart in {left} min, please log out!"
                        : $"{text} - {left} min - deconnectez-vous / please log out");
                rt.ProgressText = $"{left} min";
                await Push(def.Id);
                await Task.Delay(TimeSpan.FromMinutes(1), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            rt.State = ServerState.Running;
            rt.WantRunning = true;
            rt.ProgressText = null;
            ServerLog(def, "Stop cancelled");
            throw new ServerException("stop_cancelled");
        }
        finally { rt.StopCountdown = null; }

        // Anyone who came back during the last minute.
        if (delayMinutes > 0) await KickEveryone(def);
        rt.ProgressText = "saveandexit";
        await Push(def.Id);
        try
        {
            await TelnetClient.SendAsync(def.TelnetPort, def.TelnetPassword, "saveandexit 0");
            ServerLog(def, "saveandexit sent");
        }
        catch (Exception e) { ServerLog(def, "saveandexit could not be sent: " + e.Message); }

        var deadline = DateTime.UtcNow + StopTimeout;
        while (DateTime.UtcNow < deadline && FindGameProcesses(def).Count > 0)
            await Task.Delay(2000);

        var left2 = FindGameProcesses(def);
        if (left2.Count > 0)
        {
            ServerLog(def, $"Server did not exit after {StopTimeout.TotalMinutes} min: forcing {left2.Count} process(es) to stop");
            _log.LogWarning("Server '{Name}' had to be force-stopped", def.Name);
            KillAll(left2);
        }
        rt.State = ServerState.Stopped;
        rt.ProcessId = null;
        rt.StartedUtc = null;
        rt.ProgressText = null;
        ServerLog(def, "Server stopped");
    }

    public bool CancelStop(long id)
    {
        if (Runtime(id).StopCountdown is { } c) { c.Cancel(); return true; }
        return false;
    }

    /// <summary>Immediate kill without saving. For a frozen server only.</summary>
    public async Task KillAsync(long id, string by)
    {
        var def = Get(id);
        var rt = Runtime(id);
        rt.WantRunning = false;
        rt.ManualStopUtc = DateTime.UtcNow;
        rt.StopCountdown?.Cancel();
        var procs = FindGameProcesses(def);
        KillAll(procs);
        rt.State = def.Installed ? ServerState.Stopped : ServerState.NotInstalled;
        rt.ProcessId = null;
        ServerLog(def, $"Killed by {by} ({procs.Count} process(es))");
        _log.LogWarning("{By} killed server '{Name}'", by, def.Name);
        await Push(id);
    }

    public void ResumeAutoRestart(long id)
    {
        var rt = Runtime(id);
        rt.AutoRestartSuspended = false;
        rt.RecentCrashes.Clear();
    }

    // ------------------------------------------------------------------ console & logs

    public async Task<string> ConsoleAsync(long id, string command, string by)
    {
        var def = Get(id);
        var rt = Runtime(id);
        if (rt.State != ServerState.Running) throw new ServerException("not_running");
        command = (command ?? "").Trim();
        if (command.Length is 0 or > 500 || command.Contains('\n') || command.Contains('\r')) throw new ServerException("invalid_command");
        ServerLog(def, $"Console ({by}): {command}");
        try { return await TelnetClient.SendAsync(def.TelnetPort, def.TelnetPassword, command); }
        catch (Exception e) { throw new ServerException("telnet_failed", e.Message); }
    }

    public IReadOnlyList<string> GameLogTail(long id, int lines)
    {
        var def = Get(id);
        var logs = Path.Combine(def.GameDir, "Logs");
        if (!Directory.Exists(logs)) return [];
        var newest = Directory.EnumerateFiles(logs, "Dedicated_*.log", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        return newest is null ? [] : Tail(newest.FullName, lines);
    }

    public IReadOnlyList<string> EamLogTail(long id, int lines)
    {
        var def = Get(id);
        var file = Path.Combine(def.LogsDir, "server.log");
        return File.Exists(file) ? Tail(file, lines) : [];
    }

    private static IReadOnlyList<string> Tail(string file, int lines)
    {
        lines = Math.Clamp(lines, 1, 2000);
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, fs.Length - 512 * 1024);
        fs.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(fs);
        var all = reader.ReadToEnd().Replace("\r", "").Split('\n');
        return all.Skip(start > 0 ? 1 : 0).Where(l => l.Length > 0).TakeLast(lines).ToList();
    }

    // ------------------------------------------------------------------ monitoring loop

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // Re-attach to servers already running (engine restart) and start the auto-start ones.
        foreach (var def in _store.List())
        {
            var rt = Runtime(def.Id);
            var running = FindGameProcesses(def).Count > 0;
            rt.State = !def.Installed ? ServerState.NotInstalled : running ? ServerState.Starting : ServerState.Stopped;
            rt.WantRunning = running;
            if (running) rt.StartRequestedUtc = DateTime.UtcNow;
            if (!running && def.Installed && def.AutoStart)
            {
                try { await StartAsync(def.Id, "auto-start"); }
                catch (Exception e) { rt.LastError = (e as ServerException)?.Code ?? e.Message; _log.LogWarning("Auto-start of '{Name}' failed: {Err}", def.Name, e.Message); }
            }
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stop))
        {
            foreach (var def in _store.List())
            {
                try { await CheckAsync(def, stop); }
                catch (Exception e) when (e is not OperationCanceledException) { _log.LogWarning(e, "Watch of '{Name}' failed", def.Name); }
            }
        }
    }

    private async Task CheckAsync(ServerDefinition def, CancellationToken ct)
    {
        var rt = Runtime(def.Id);
        if (rt.Lock.CurrentCount == 0) return; // an operation is running: it owns the state
        var before = (rt.State, rt.ProcessId);
        var procs = FindGameProcesses(def);
        var main = procs.FirstOrDefault(p => SafeName(p) == "EmpyrionDedicated");

        if (procs.Count > 0)
        {
            rt.ProcessId = main?.Id;
            if (rt.State is ServerState.Stopped or ServerState.Crashed)
            {
                // Processes still closing right after an admin stop: finish the job, never treat it as a start.
                if (rt.ManualStopUtc is { } ms && DateTime.UtcNow - ms < TimeSpan.FromMinutes(2))
                {
                    KillAll(procs);
                    return;
                }
                rt.State = ServerState.Starting; rt.WantRunning = true; rt.StartRequestedUtc ??= DateTime.UtcNow;
            }
            if (rt.State == ServerState.Starting && await TelnetClient.IsReachableAsync(def.TelnetPort, ct))
            {
                rt.State = ServerState.Running;
                rt.StartedUtc = DateTime.UtcNow;
                ServerLog(def, "Server is running (telnet reachable)");
            }
            else if (rt.State == ServerState.Starting && rt.StartRequestedUtc is { } t && DateTime.UtcNow - t > StartTimeout)
            {
                rt.LastError = "start_timeout";
            }
        }
        else if (rt.State is ServerState.Running or ServerState.Starting && rt.WantRunning)
        {
            // Launcher still starting the dedicated process: give it a moment.
            if (rt.State == ServerState.Starting && rt.StartRequestedUtc is { } t0 && DateTime.UtcNow - t0 < TimeSpan.FromSeconds(60)) return;
            OnCrash(def, rt);
        }
        else if (rt.State is ServerState.Running or ServerState.Starting)
        {
            rt.State = ServerState.Stopped;
        }

        foreach (var p in procs) p.Dispose();
        if (before != (rt.State, rt.ProcessId)) await Push(def.Id);

        if (rt.State == ServerState.Crashed && def.AutoRestart && !rt.AutoRestartSuspended && rt.WantRunning)
        {
            ServerLog(def, "Automatic restart after crash");
            try { await StartAsync(def.Id, "auto-restart"); }
            catch (Exception e) { rt.LastError = (e as ServerException)?.Code ?? e.Message; }
        }
    }

    private void OnCrash(ServerDefinition def, ServerRuntime rt)
    {
        rt.State = ServerState.Crashed;
        rt.ProcessId = null;
        rt.StartedUtc = null;
        var now = DateTime.UtcNow;
        rt.RecentCrashes.RemoveAll(t => now - t > CrashWindow);
        rt.RecentCrashes.Add(now);
        ServerLog(def, $"CRASH detected ({rt.RecentCrashes.Count} in the last {CrashWindow.TotalMinutes} min)");
        _log.LogError("Server '{Name}' crashed", def.Name);
        if (rt.RecentCrashes.Count >= MaxCrashesBeforeSuspend)
        {
            rt.AutoRestartSuspended = true;
            rt.WantRunning = false;
            rt.LastError = "crash_loop";
            ServerLog(def, "Crash loop: automatic restarts suspended until an admin intervenes");
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task WithLock(long id, string operation, Func<ServerDefinition, ServerRuntime, Task> action)
    {
        var def = Get(id);
        var rt = Runtime(id);
        if (!await rt.Lock.WaitAsync(0)) throw new ServerException("server_busy", rt.Operation);
        rt.Operation = operation;
        try
        {
            await Push(id);
            await action(def, rt);
        }
        catch (ServerException e)
        {
            // Errors of interactive actions go back to the caller; only background operations keep them on the server.
            if (e.Code != "stop_cancelled" && operation is not ("rename" or "settings" or "delete" or "gameoptions" or "scenario" or "restore" or "server-options"))
                rt.LastError = e.Code;
            throw;
        }
        finally
        {
            rt.Operation = null;
            rt.Lock.Release();
            await Push(id);
        }
    }

    private static void RequireStopped(ServerRuntime rt, bool allowNotInstalled = false)
    {
        if (rt.State is ServerState.Stopped or ServerState.Crashed) return;
        if (allowNotInstalled && rt.State == ServerState.NotInstalled) return;
        if (rt.State == ServerState.NotInstalled) throw new ServerException("not_installed");
        throw new ServerException("server_must_be_stopped");
    }

    /// <summary>EAH's own mods: its plugin (EAH*.dll, folder EPM) and its game mod (folder EPF, "EAH Mod" in its info file).</summary>
    internal static bool IsEahMod(string mod)
    {
        try
        {
            if (Path.GetFileName(mod) is "EPM" or "EPF") return true;
            if (Directory.EnumerateFiles(mod, "EAH*.dll", SearchOption.AllDirectories).Any()) return true;
            return Directory.EnumerateFiles(mod, "*.yaml", SearchOption.TopDirectoryOnly)
                .Any(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"(?im)^\s*Name\s*:\s*.*\bEAH\b"));
        }
        catch { return false; }
    }

    /// <summary>A path inside <paramref name="from"/> moved to the same place inside <paramref name="to"/> (null when it is elsewhere).</summary>
    internal static string? RebasePath(string value, string from, string to)
    {
        var v = value.Trim().Trim('"', '\'');
        if (!Path.IsPathFullyQualified(v) || !IsUnder(v, from)) return null;
        return Path.Combine(to, Path.GetRelativePath(from, v));
    }

    /// <summary>Free space on the drive of a folder (null when unknown).</summary>
    public static long? FreeBytes(string folder)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder))!).AvailableFreeSpace; } catch { return null; }
    }

    /// <summary>Folder that receives a new server: the one given (full path), or the servers folder of the settings.</summary>
    private string ServersParent(string? parentFolder)
    {
        var dir = string.IsNullOrWhiteSpace(parentFolder) ? _paths.ServersDir : parentFolder.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(dir)) throw new ServerException("servers_folder_unusable", dir);
        dir = Path.GetFullPath(dir);
        try { Directory.CreateDirectory(dir); }
        catch (Exception e) { throw new ServerException("servers_folder_unusable", $"{dir}: {e.Message}"); }
        return dir;
    }

    /// <summary>
    /// Safety for deleting or moving a server folder: only a folder EAM made itself, i.e. named after the
    /// server and holding EAM's own "eam" sub-folder, and never the game folder of a server used in place.
    /// </summary>
    private static bool CreatedByEam(ServerDefinition def)
    {
        var root = Path.GetFullPath(def.RootDir).TrimEnd('\\', '/');
        if (!string.Equals(Path.GetFileName(root), def.Slug, StringComparison.OrdinalIgnoreCase)) return false;
        if (!Directory.Exists(Path.Combine(root, "eam"))) return false;
        if (def.GameDirOverride is { } g && (IsUnder(g, root) || IsUnder(root, g))) return false;
        return Path.GetPathRoot(root) != root;
    }

    private void EnsureFolderFree(string root, IEnumerable<ServerDefinition> others)
    {
        var full = Path.GetFullPath(root);
        foreach (var o in others)
            if (IsUnder(full, o.RootDir) || IsUnder(o.RootDir, full)) throw new ServerException("folder_used_by_server");
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()) throw new ServerException("folder_not_empty", full);
    }

    public static bool IsUnder(string path, string parent)
    {
        var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var r = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(r, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string UniqueSlug(string slug, IEnumerable<ServerDefinition> all)
    {
        var taken = all.Select(s => s.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = slug;
        for (var i = 2; taken.Contains(candidate); i++) candidate = $"{slug}-{i}";
        return candidate;
    }

    /// <summary>Name in the game's server list: null when empty or equal to the EAM name.</summary>
    private static string? CleanDisplayName(string? display, string name)
    {
        display = (display ?? "").Trim();
        if (display.Length == 0 || display == name) return null;
        if (display.Length > 100 || display.Any(char.IsControl)) throw new ServerException("invalid_display_name");
        return display;
    }

    private static string CleanName(string? name)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 2 or > 60) throw new ServerException("invalid_name");
        if (name.Any(char.IsControl)) throw new ServerException("invalid_name");
        return name;
    }

    private static void ValidateCommon(int maxPlayers, string branch, string? description, string? password)
    {
        if (maxPlayers is < 1 or > 200) throw new ServerException("invalid_max_players");
        if (branch is not ("public" or "experimental")) throw new ServerException("invalid_branch");
        if (description?.Length > 500) throw new ServerException("description_too_long");
        if (password?.Length > 64) throw new ServerException("password_too_long");
    }

    /// <summary>
    /// Copies the EAM mod shipped with EAM into Content\Mods\EAM of this server, with its own port and key.
    /// Done at every start, so the mod is always the version that matches the engine.
    /// </summary>
    private ServerDefinition InstallMod(ServerDefinition def)
    {
        if (def.ModKey.Length < 32) def = _store.Update(def with { ModKey = NewModKey() });
        var source = Path.Combine(AppContext.BaseDirectory, "mod", "EAM");
        if (!File.Exists(Path.Combine(source, "EAM.dll")))
        {
            ServerLog(def, "EAM mod files not found next to EAM: live view disabled for this start");
            return def;
        }
        try
        {
            Directory.CreateDirectory(def.ModDir);
            // Remove files of older EAM versions (e.g. EamMod.dll in 0.3.0): only EAM's files live in this folder.
            var shipped = Directory.GetFiles(source).Select(Path.GetFileName).Append("eam-link.cfg").ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var old in Directory.GetFiles(def.ModDir))
                if (!shipped.Contains(Path.GetFileName(old))) File.Delete(old);
            foreach (var file in Directory.GetFiles(source))
            {
                var target = Path.Combine(def.ModDir, Path.GetFileName(file));
                if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(file)))
                    File.Copy(file, target, overwrite: true);
            }
            var cfg = Path.Combine(def.ModDir, "eam-link.cfg");
            var content = $"# Written by Empyrion Admin Manager. Local link only (127.0.0.1).\r\nport={def.ModPort}\r\nkey={def.ModKey}\r\n";
            File.WriteAllText(cfg + ".tmp", content);
            File.Move(cfg + ".tmp", cfg, overwrite: true);
        }
        catch (Exception e)
        {
            ServerLog(def, "EAM mod could not be installed: " + e.Message);
            _log.LogWarning("EAM mod could not be installed in '{Name}': {Err}", def.Name, e.Message);
        }
        return def;
    }

    private static string NewModKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string NormalizeBranch(string b) => b == "experimental" ? "experimental" : "public";

    private static string NewSecret()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return new string(RandomNumberGenerator.GetItems<char>(chars, 24));
    }

    private static string SafeName(Process p) { try { return p.ProcessName; } catch { return ""; } }

    /// <summary>Every Empyrion process (dedicated, launcher, playfields) running from THIS server's folder only.</summary>
    public static List<Process> FindGameProcesses(ServerDefinition def) => FindGameProcesses(def.GameDir);

    /// <summary>Every Empyrion process running from this game folder.</summary>
    public static List<Process> FindGameProcesses(string gameDir)
    {
        var result = new List<Process>();
        foreach (var name in new[] { "EmpyrionDedicated", "EmpyrionPlayfieldServer", "EmpyrionLauncher" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                string? file = null;
                try { file = p.MainModule?.FileName; } catch { }
                if (file is not null && IsUnder(file, gameDir)) result.Add(p);
                else p.Dispose();
            }
        }
        return result;
    }

    private static void KillAll(IEnumerable<Process> procs)
    {
        foreach (var p in procs)
        {
            try { p.Kill(entireProcessTree: true); p.WaitForExit(10_000); } catch { }
            p.Dispose();
        }
    }

    /// <summary>Kicks every online player (set by the player service: the manager does not know who is online).</summary>
    public Func<long, string, Task<int>>? KickAllPlayers { get; set; }

    private async Task KickEveryone(ServerDefinition def)
    {
        if (KickAllPlayers is null) return;
        try { await KickAllPlayers(def.Id, "Redemarrage du serveur / Server restart"); }
        catch (Exception e) { ServerLog(def, "Kick before stop failed: " + e.Message); }
    }

    private async Task TrySay(ServerDefinition def, string text)
    {
        try { await TelnetClient.SendAsync(def.TelnetPort, def.TelnetPassword, $"say '{text.Replace("'", "")}'"); }
        catch (Exception e) { ServerLog(def, "Announcement failed: " + e.Message); }
    }

    private static readonly object LogLock = new();

    public static void ServerLog(ServerDefinition def, string line)
    {
        try
        {
            Directory.CreateDirectory(def.LogsDir);
            lock (LogLock)
                File.AppendAllText(Path.Combine(def.LogsDir, "server.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }

    public Task PushAsync(long id) => Push(id);

    private async Task Push(long id)
    {
        var def = _store.Find(id);
        if (def is null) return;
        try { await _hub.BroadcastAsync(new { type = "server", data = Describe(def) }, CancellationToken.None); } catch { }
    }

    private Task BroadcastListAsync() => _hub.BroadcastAsync(new { type = "servers" }, CancellationToken.None);
}
