using Eam.Core.Data;
using Eam.Core.Servers;

namespace Eam.Engine.Servers;

/// <summary>
/// Step 9: maintenance tools. Compact the databases, "fresh start" for a new season, delete the save and its cache
/// (new game), find damaged player files, convert item numbers and names.
/// Everything that touches the save needs the server stopped and makes a backup first.
/// </summary>
public sealed class MaintenanceService(ServerManager manager, LiveService live, BackupService backups, Database mainDb, Alerts alerts)
{
    public object Compact(long id)
    {
        var def = manager.Definition(id);
        var size = live.Data(def).Compact();
        using (var c = mainDb.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); VACUUM;";
            cmd.ExecuteNonQuery();
        }
        return new { serverDb = size };
    }

    private static string SaveName(ServerDefinition def) => WorldService.SaveName(def);

    public object Info(long id)
    {
        var def = manager.Definition(id);
        var game = SaveName(def);
        var saveDir = Path.Combine(def.GameDir, "Saves", "Games", game);
        var cacheDir = Path.Combine(def.GameDir, "Saves", "Cache", game);
        long Size(string d) => Directory.Exists(d) ? new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        return new
        {
            game, saveExists = Directory.Exists(saveDir), saveSize = Size(saveDir), cacheSize = Size(cacheDir),
            dbSize = File.Exists(def.DatabaseFile) ? new FileInfo(def.DatabaseFile).Length : 0,
            stopped = manager.Runtime(id).State is ServerState.Stopped or ServerState.Crashed or ServerState.NotInstalled,
        };
    }

    /// <summary>Clears EAM's history of the old game (players, structures, journal…). Settings and tasks stay.</summary>
    public async Task FreshStartAsync(long id, bool keepChat, bool deleteSave, string by)
    {
        var def = manager.Definition(id);
        await manager.RunExclusiveAsync(id, "fresh-start", requireStopped: true, async (d, rt) =>
        {
            await backups.CreateAsync(id, "before-wipe", by, "fresh start");
            if (deleteSave)
            {
                var game = SaveName(d);
                foreach (var dir in new[] { Path.Combine(d.GameDir, "Saves", "Games", game), Path.Combine(d.GameDir, "Saves", "Cache", game) })
                {
                    if (!Directory.Exists(dir)) continue;
                    if (!ServerManager.IsUnder(dir, d.GameDir)) throw new ServerException("unsafe_delete");
                    Directory.Delete(dir, true);
                }
            }
            live.Data(d).FreshStart(keepChat);
        });
        await alerts.RaiseAsync(def, "maintenance", 1, $"{by}: fresh start{(deleteSave ? " with a new game" : "")} (backup made first)");
    }

    /// <summary>Player files that are empty or tiny: usually damaged by a crash.</summary>
    public object DamagedPlayers(long id)
    {
        var def = manager.Definition(id);
        var dir = Path.Combine(def.GameDir, "Saves", "Games", SaveName(def), "Players");
        if (!Directory.Exists(dir)) return Array.Empty<object>();
        var names = live.Data(def).Players().ToDictionary(p => p.SteamId, p => p.Name);
        return new DirectoryInfo(dir).EnumerateFiles("*.ply").Where(f => f.Length < 64)
            .Select(f => new { file = f.Name, size = f.Length, modifiedUtc = f.LastWriteTimeUtc, name = names.GetValueOrDefault(Path.GetFileNameWithoutExtension(f.Name)) })
            .ToList();
    }

    public object MoveDamagedPlayer(long id, string file, string by)
    {
        var def = manager.Definition(id);
        if (manager.Runtime(id).State is not (ServerState.Stopped or ServerState.Crashed)) throw new ServerException("server_must_be_stopped");
        if (file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !file.EndsWith(".ply", StringComparison.OrdinalIgnoreCase)) throw new ServerException("invalid_name");
        var src = Path.Combine(def.GameDir, "Saves", "Games", SaveName(def), "Players", file);
        if (!File.Exists(src)) throw new ServerException("not_found");
        var target = Path.Combine(def.EamDir, "players-removed", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(target);
        File.Move(src, Path.Combine(target, file));
        ServerManager.ServerLog(def, $"{by}: damaged player file {file} moved to {target}");
        return new { target };
    }

    /// <summary>Item number ↔ name, for the scenario of the server.</summary>
    public object Items(long id, string query, string lang)
    {
        var def = manager.Definition(id);
        string? scenario = null;
        try { scenario = File.Exists(def.YamlFile) ? DedicatedYaml.Load(def.YamlFile).Get("GameConfig", "CustomScenario") : null; } catch { }
        var items = ItemCatalog.Load(def, scenario, lang);
        query = (query ?? "").Trim();
        var byNumber = int.TryParse(query, out var n);
        return items.Where(i => query.Length == 0 || (byNumber ? i.Id == n || i.Id - ItemCatalog.ItemIdOffset == n : i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || i.Label.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Take(200)
            .Select(i => new { i.Id, configId = i.IsBlock ? i.Id : i.Id - ItemCatalog.ItemIdOffset, i.Name, i.Label, i.IsBlock })
            .ToList();
    }
}
