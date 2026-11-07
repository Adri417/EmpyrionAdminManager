using System.Text.RegularExpressions;
using Eam.Core.Config;
using Eam.Core.Servers;

namespace Eam.Engine.Servers;

public sealed record OptionChange(string Section, string Key, string? Value, bool Active);

/// <summary>
/// Every option of a server: dedicated-eam.yaml (server options, save name, scenario, mode, seed…),
/// the save's gameoptions.yaml (game options) and the scenarios installed in the server.
/// </summary>
public sealed partial class GameConfigService(ServerManager manager, EamPaths paths, ILogger<GameConfigService> log)
{
    /// <summary>Options EAM sets itself from the server's settings: never edited by hand.</summary>
    public static readonly string[] ManagedKeys = ["Srv_Name", "Srv_Port", "Tel_Enabled", "Tel_Port", "Tel_Pwd"];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")]
    private static partial Regex KeyName();

    private static string ExampleOptionsFile(ServerDefinition def) =>
        Path.Combine(ScenarioLibrary.ScenariosDir(def), "Default Random", "gameoptions_example.yaml");

    public static string SaveDir(ServerDefinition def, string gameName) => Path.Combine(def.GameDir, "Saves", "Games", gameName);

    // ------------------------------------------------------------------ read

    public object Get(long id)
    {
        var def = manager.Definition(id);
        if (!def.Installed || !File.Exists(def.YamlFile)) throw new ServerException("not_installed");
        var yaml = DedicatedYaml.Load(def.YamlFile);
        var entries = yaml.Entries().ToList();
        // Options the game's template knows but EAM's file does not mention yet.
        if (File.Exists(def.ShippedYamlFile))
        {
            foreach (var e in DedicatedYaml.Load(def.ShippedYamlFile).Entries())
                if (!entries.Any(x => x.Section == e.Section && x.Key == e.Key)) entries.Add(e with { Active = false });
        }
        var gameName = yaml.Get("GameConfig", "GameName") ?? "DediGame";
        var mode = yaml.Get("GameConfig", "Mode") ?? "Survival";
        var scenario = yaml.Get("GameConfig", "CustomScenario");
        var savesDir = Path.Combine(def.GameDir, "Saves", "Games");
        var saves = Directory.Exists(savesDir)
            ? Directory.GetDirectories(savesDir).Where(d => !Path.GetFileName(d).Contains(".eam-"))
                .Select(d => new { name = Path.GetFileName(d), modifiedUtc = Directory.GetLastWriteTimeUtc(d) })
                .OrderByDescending(x => x.modifiedUtc).ToList()
            : [];
        var scenarios = ScenarioLibrary.List(def);

        return new
        {
            gameName, mode, scenario, seed = yaml.Get("GameConfig", "Seed"),
            options = entries.Select(e => new
            {
                section = e.Section, key = e.Key, value = e.Value, active = e.Active, help = e.Help,
                managed = e.Section == "ServerConfig" && ManagedKeys.Contains(e.Key),
            }),
            raw = yaml.ToString(),
            saves, scenarios,
            known = ScenarioLibrary.Known.Select(k => new
            {
                k.Key, k.Name, k.WorkshopId, k.Label,
                installed = scenarios.FirstOrDefault(s => s.WorkshopId == k.WorkshopId || s.Name == k.Name)?.Name,
            }),
            gameOptions = GameOptions(def, gameName, mode, scenario),
        };
    }

    private object GameOptions(ServerDefinition def, string gameName, string mode, string? scenario)
    {
        var file = Path.Combine(SaveDir(def, gameName), "gameoptions.yaml");
        var saveExists = File.Exists(file);
        var source = saveExists ? file : scenario is { Length: > 0 } ? Path.Combine(ScenarioLibrary.ScenariosDir(def), scenario, "gameoptions.yaml") : null;
        var entries = source is not null && File.Exists(source) ? GameOptionsYaml.Load(source).Entries(mode).ToList() : [];
        var example = ExampleOptionsFile(def);
        if (File.Exists(example))
        {
            var known = GameOptionsYaml.Load(example).AllEntries();
            foreach (var k in known)
            {
                var i = entries.FindIndex(e => e.Key == k.Key);
                if (i < 0) entries.Add(k with { Active = false });
                else if (string.IsNullOrEmpty(entries[i].Help) && k.Help is not null) entries[i] = entries[i] with { Help = k.Help };
            }
        }
        return new
        {
            saveExists, file = saveExists ? file : null, mode,
            entries = entries.Select(e => new { key = e.Key, value = e.Value, active = e.Active, help = e.Help }),
        };
    }

    // ------------------------------------------------------------------ server options (dedicated-eam.yaml)

    public async Task<bool> SaveOptionsAsync(long id, IReadOnlyList<OptionChange> changes, string by)
    {
        var running = false;
        await manager.RunExclusiveAsync(id, "server-options", requireStopped: false, async (def, rt) =>
        {
            if (!File.Exists(def.YamlFile)) throw new ServerException("not_installed");
            var y = DedicatedYaml.Load(def.YamlFile);
            foreach (var c in changes)
            {
                if (c.Section is not ("ServerConfig" or "GameConfig")) throw new ServerException("invalid_section", c.Section);
                if (!KeyName().IsMatch(c.Key ?? "")) throw new ServerException("invalid_key", c.Key);
                if (c.Section == "ServerConfig" && ManagedKeys.Contains(c.Key)) continue;
                var v = c.Value?.Trim() ?? "";
                if (v.Length > 500 || v.IndexOfAny(['\r', '\n']) >= 0) throw new ServerException("invalid_value", c.Key);
                if (c.Key == "GameName") ValidateGameName(v);
                if (c.Key == "CustomScenario" && v.Length > 0) ScenarioLibrary.ValidateName(v);
                if (c.Active) y.Set(c.Section, c.Key, v);
                else y.Disable(c.Section, c.Key);
            }
            ServerManager.ApplyManagedKeys(y, def);
            Backup(def.YamlFile);
            y.Save(def.YamlFile);
            running = rt.State is ServerState.Running or ServerState.Starting;
            ServerManager.ServerLog(def, $"Server options changed by {by}: {string.Join(", ", changes.Select(c => c.Key))}");
            await Task.CompletedTask;
        });
        return running;
    }

    public async Task<bool> SaveRawAsync(long id, string text, string by)
    {
        var running = false;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200_000) throw new ServerException("invalid_yaml");
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (!lines.Any(l => l.TrimEnd() == "ServerConfig:") || !lines.Any(l => l.TrimEnd() == "GameConfig:")) throw new ServerException("invalid_yaml", "ServerConfig / GameConfig");
        if (lines.Any(l => l.Contains('\t'))) throw new ServerException("invalid_yaml", "tab");
        await manager.RunExclusiveAsync(id, "server-options", requireStopped: false, async (def, rt) =>
        {
            var y = DedicatedYaml.Parse(text);
            ServerManager.ApplyManagedKeys(y, def);
            Backup(def.YamlFile);
            y.Save(def.YamlFile);
            running = rt.State is ServerState.Running or ServerState.Starting;
            ServerManager.ServerLog(def, $"dedicated-eam.yaml edited by {by}");
            await Task.CompletedTask;
        });
        return running;
    }

    private static void ValidateGameName(string v)
    {
        if (v.Length is < 1 or > 60 || v.Trim() != v || v.StartsWith('.') || v.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || v.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|', '#']) >= 0)
            throw new ServerException("invalid_game_name");
    }

    private static void Backup(string file)
    {
        try { if (File.Exists(file)) File.Copy(file, file + ".bak", overwrite: true); } catch { }
    }

    // ------------------------------------------------------------------ game options (save's gameoptions.yaml)

    public Task SaveGameOptionsAsync(long id, IReadOnlyList<OptionChange> changes, string by) =>
        manager.RunExclusiveAsync(id, "gameoptions", requireStopped: true, async (def, rt) =>
        {
            var y = DedicatedYaml.Load(def.YamlFile);
            var gameName = y.Get("GameConfig", "GameName") ?? "DediGame";
            var mode = y.Get("GameConfig", "Mode") ?? "Survival";
            var file = Path.Combine(SaveDir(def, gameName), "gameoptions.yaml");
            if (!File.Exists(file)) throw new ServerException("save_not_created");
            var g = GameOptionsYaml.Load(file);
            foreach (var c in changes)
            {
                if (!KeyName().IsMatch(c.Key ?? "")) throw new ServerException("invalid_key", c.Key);
                var v = c.Value?.Trim() ?? "";
                if (v.Length > 300 || v.IndexOfAny(['\r', '\n']) >= 0) throw new ServerException("invalid_value", c.Key);
                if (c.Active) g.Set(mode, c.Key, v); else g.Disable(mode, c.Key);
            }
            Backup(file);
            g.Save(file);
            ServerManager.ServerLog(def, $"Game options of '{gameName}' changed by {by}: {string.Join(", ", changes.Select(c => c.Key))}");
            await Task.CompletedTask;
        });

    // ------------------------------------------------------------------ scenarios

    private string WorkshopDir => Path.Combine(paths.DataRoot, "workshop");

    /// <summary>
    /// Downloads a Workshop scenario (always the latest version) and installs it in the server.
    /// Falls back on the copy downloaded by the Steam client of this machine if SteamCMD cannot get it.
    /// </summary>
    public async Task InstallWorkshopAsync(long id, string workshopId, string folderName, bool use, string by)
    {
        ScenarioLibrary.ValidateName(folderName);
        var def = manager.Definition(id);
        var rt = manager.Runtime(id);
        if (rt.Lock.CurrentCount == 0) throw new ServerException("server_busy", rt.Operation);
        string source;
        try
        {
            await manager.ReportProgressAsync(id, 0, "download_workshop");
            var lastPush = DateTime.MinValue;
            var progress = new Progress<SteamProgress>(p =>
            {
                if ((DateTime.UtcNow - lastPush).TotalMilliseconds < 800) return;
                lastPush = DateTime.UtcNow;
                _ = manager.ReportProgressAsync(id, p.Percent, "download_workshop");
            });
            try
            {
                source = await manager.Steam.DownloadWorkshopItemAsync(WorkshopDir, workshopId, progress, line => ServerManager.ServerLog(def, "[steamcmd] " + line), CancellationToken.None);
                ServerManager.ServerLog(def, $"Workshop item {workshopId} downloaded by SteamCMD");
            }
            catch (Exception e) when (e is not ServerException)
            {
                var local = ScenarioLibrary.FindInSteamClient(workshopId);
                if (local is null) throw new ServerException("workshop_download_failed", e.Message);
                ServerManager.ServerLog(def, $"SteamCMD could not download {workshopId} ({e.Message}); using the copy of the Steam client: {local}");
                source = local;
            }
        }
        finally { await manager.ReportProgressAsync(id, null, null); }

        await InstallFromAsync(id, source, folderName, workshopId, use, by);
    }

    /// <summary>Installs a scenario from a folder of this machine (or a downloaded Workshop folder).</summary>
    public Task InstallFromAsync(long id, string sourceDir, string folderName, string? workshopId, bool use, string by)
    {
        ScenarioLibrary.ValidateName(folderName);
        if (!Directory.Exists(sourceDir)) throw new ServerException("folder_not_found", sourceDir);
        if (ScenarioLibrary.FindRoot(sourceDir) is null) throw new ServerException("scenario_not_found", sourceDir);
        return manager.RunExclusiveAsync(id, "scenario", requireStopped: true, async (def, rt) =>
        {
            var lastPush = DateTime.MinValue;
            var progress = new Progress<double>(p =>
            {
                rt.Progress = p;
                rt.ProgressText = "copy_scenario";
                if ((DateTime.UtcNow - lastPush).TotalMilliseconds > 800) { lastPush = DateTime.UtcNow; _ = manager.PushAsync(id); }
            });
            try
            {
                await Task.Run(() => ScenarioLibrary.Install(def, sourceDir, folderName, workshopId, progress));
            }
            catch (Exception e) when (e is not ServerException)
            {
                log.LogError(e, "Scenario install failed");
                throw new ServerException("scenario_install_failed", e.Message);
            }
            finally { rt.Progress = null; rt.ProgressText = null; }
            if (use && File.Exists(def.YamlFile))
            {
                var y = DedicatedYaml.Load(def.YamlFile);
                Backup(def.YamlFile);
                y.Set("GameConfig", "CustomScenario", folderName).Save(def.YamlFile);
            }
            ServerManager.ServerLog(def, $"Scenario '{folderName}' installed by {by}{(workshopId is null ? $" from {sourceDir}" : $" (Workshop {workshopId})")}{(use ? ", now used by the server" : "")}");
        });
    }

    public Task DeleteScenarioAsync(long id, string name, string by) =>
        manager.RunExclusiveAsync(id, "scenario", requireStopped: true, async (def, rt) =>
        {
            var current = File.Exists(def.YamlFile) ? DedicatedYaml.Load(def.YamlFile).Get("GameConfig", "CustomScenario") : null;
            if (string.Equals(current, name, StringComparison.OrdinalIgnoreCase)) throw new ServerException("scenario_in_use");
            try { ScenarioLibrary.Delete(def, name); }
            catch (Exception e) when (e is not ServerException) { throw new ServerException("scenario_delete_failed", e.Message); }
            ServerManager.ServerLog(def, $"Scenario '{name}' deleted by {by}");
            await Task.CompletedTask;
        });
}
