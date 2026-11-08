using Eam.Core.Config;
using Eam.Core.Servers;
using Eam.Core.Servers.Eah;

namespace Eam.Engine.Servers;

public sealed record ImportPreviewRequest(string Path);
public sealed record ImportRequest(string Path, string Mode, long? ServerId, string? Name, EahImportOptions? Options, string? DisplayName = null, string? Folder = null);

/// <summary>
/// "Import a server": the admin gives the folder of a server that used EAH (or any Empyrion server),
/// EAM adds the server (copied or used in place) and brings over all of EAH's data and settings.
/// </summary>
public sealed class ImportService(ServerManager manager, LiveService live, EamPaths paths, ILogger<ImportService> log)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private string WorkDir() => Path.Combine(paths.DataRoot, "temp", "import-" + Guid.NewGuid().ToString("N")[..8]);

    private static string? GameDirOf(string path)
    {
        path = (path ?? "").Trim().Trim('"');
        if (!Directory.Exists(path)) return null;
        var d = new DirectoryInfo(Path.GetFullPath(path));
        for (var i = 0; i < 6 && d is not null; i++, d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "EmpyrionLauncher.exe")) || File.Exists(Path.Combine(d.FullName, "DedicatedServer", "EmpyrionDedicated.exe")))
                return d.FullName;
        return null;
    }

    public object Preview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path.Trim().Trim('"'))) throw new ServerException("not_found", path);
        var config = EahReader.FindConfig(path);
        var gameDir = GameDirOf(path) ?? (config is null ? null : EahReader.FindGameDir(config));
        if (config is null && gameDir is null) throw new ServerException("nothing_to_import");
        EahPreview? eah = null;
        if (config is not null) eah = EahImporter.Preview(config, WorkDir());
        var yamlName = eah?.Server.GetValueOrDefault("dedicated") is { Length: > 0 } y ? Path.GetFileName(y) : null;
        string? srvName = null, port = null;
        if (gameDir is not null)
        {
            var yaml = new[] { yamlName, "dedicated.yaml" }.Where(n => n is not null).Select(n => Path.Combine(gameDir, n!)).FirstOrDefault(File.Exists);
            if (yaml is not null)
            {
                try
                {
                    var dy = DedicatedYaml.Load(yaml);
                    srvName = dy.Get("ServerConfig", "Srv_Name");
                    port = dy.Get("ServerConfig", "Srv_Port");
                    yamlName = Path.GetFileName(yaml);
                }
                catch { /* shown without the name */ }
            }
        }
        long size = 0;
        if (gameDir is not null)
            try { size = new DirectoryInfo(gameDir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { }
        var existing = gameDir is null ? null : manager.Definitions().FirstOrDefault(d => ServerManager.IsUnder(d.GameDir, gameDir) && ServerManager.IsUnder(gameDir, d.GameDir));
        return new
        {
            configDir = config, gameDir, yaml = yamlName, name = srvName, port, size,
            serversFolder = paths.ServersDir, freeBytes = ServerManager.FreeBytes(paths.ServersDir),
            existingServerId = existing?.Id,
            counts = eah?.Counts, eah = eah?.Server, missing = eah?.Missing,
        };
    }

    public async Task<object> ImportAsync(ImportRequest req, string by)
    {
        if (!await Gate.WaitAsync(0)) throw new ServerException("import_running");
        try
        {
            var path = (req.Path ?? "").Trim().Trim('"');
            var config = EahReader.FindConfig(path);
            ServerDefinition def;
            switch (req.Mode)
            {
                case "existing":
                    def = manager.Definition(req.ServerId ?? 0);
                    if (config is null) throw new ServerException("eah_not_found");
                    break;
                case "copy":
                case "inplace":
                    var gameDir = GameDirOf(path) ?? (config is null ? null : EahReader.FindGameDir(config)) ?? throw new ServerException("not_an_empyrion_server");
                    string? yamlName = null;
                    if (config is not null && EahReader.Settings(config).GetValueOrDefault("Dedicated") is { Length: > 0 } y) yamlName = Path.GetFileName(y);
                    def = await manager.AdoptAsync(req.Name, gameDir, yamlName, req.Mode == "copy", by, req.DisplayName, req.Mode == "copy" ? req.Folder : null);
                    break;
                default: throw new ServerException("invalid_mode");
            }
            object? counts = null;
            List<string> notes = [];
            if (config is not null)
            {
                var opts = req.Options ?? new EahImportOptions();
                var data = live.Data(def);
                var report = await Task.Run(() => EahImporter.Import(data, config, WorkDir(), opts));
                counts = report.Counts;
                notes = report.Notes;
                ServerManager.ServerLog(def, $"{by}: EAH data imported from {config}: " + string.Join(", ", report.Counts.Select(kv => $"{kv.Key} {kv.Value}")));
                log.LogInformation("{By} imported EAH data from {Config} into '{Name}'", by, config, def.Name);
            }
            return new { serverId = def.Id, name = def.Name, counts, notes };
        }
        finally { Gate.Release(); }
    }
}
