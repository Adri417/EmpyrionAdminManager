using System.IO.Compression;
using System.Text;
using Eam.Core.Servers;

namespace Eam.Engine.Servers;

/// <summary>
/// Step 9: mod manager. Mods live in game\Content\Mods\&lt;name&gt; (loaded by the game); disabled mods are kept in
/// eam\mods-disabled, removed ones in eam\mods-removed. Installing, enabling, disabling and removing need the server stopped.
/// EAM's own mod is never listed here.
/// </summary>
public sealed class ModService(ServerManager manager)
{
    private static readonly string[] ConfigExtensions = [".yaml", ".yml", ".json", ".xml", ".txt", ".ini", ".cfg", ".config", ".csv"];
    private const long MaxConfigSize = 512 * 1024;

    private static string ActiveDir(ServerDefinition def) => Path.Combine(def.GameDir, "Content", "Mods");
    private static string DisabledDir(ServerDefinition def) => Path.Combine(def.EamDir, "mods-disabled");

    private static string SafeName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 60 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".." || name.Equals("EAM", StringComparison.OrdinalIgnoreCase))
            throw new ServerException("invalid_name");
        return name;
    }

    private void RequireStopped(long id)
    {
        if (manager.Runtime(id).State is not (ServerState.Stopped or ServerState.Crashed or ServerState.NotInstalled))
            throw new ServerException("server_must_be_stopped");
    }

    public object List(long id)
    {
        var def = manager.Definition(id);
        object Describe(string dir, bool enabled)
        {
            var info = new DirectoryInfo(dir);
            var files = info.EnumerateFiles("*", SearchOption.AllDirectories).ToList();
            var yaml = files.FirstOrDefault(f => f.Name.EndsWith("_Info.yaml", StringComparison.OrdinalIgnoreCase));
            string? version = null, author = null, description = null;
            if (yaml is not null)
                foreach (var line in File.ReadLines(yaml.FullName).Take(40))
                {
                    var i = line.IndexOf(':');
                    if (i <= 0) continue;
                    var k = line[..i].Trim();
                    var v = line[(i + 1)..].Trim().Trim('"');
                    if (k.Equals("Version", StringComparison.OrdinalIgnoreCase)) version = v;
                    else if (k.Equals("Author", StringComparison.OrdinalIgnoreCase)) author = v;
                    else if (k.Equals("Description", StringComparison.OrdinalIgnoreCase)) description = v;
                }
            return new
            {
                name = info.Name, enabled, version, author, description, size = files.Sum(f => f.Length),
                modifiedUtc = files.Count > 0 ? files.Max(f => f.LastWriteTimeUtc) : info.LastWriteTimeUtc,
                hasDll = files.Any(f => f.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)),
                configs = files.Where(f => ConfigExtensions.Contains(f.Extension.ToLowerInvariant()) && f.Length <= MaxConfigSize)
                    .Select(f => Path.GetRelativePath(dir, f.FullName).Replace('\\', '/')).Take(50).ToList(),
            };
        }
        var active = Directory.Exists(ActiveDir(def)) ? Directory.EnumerateDirectories(ActiveDir(def)).Where(d => !Path.GetFileName(d).Equals("EAM", StringComparison.OrdinalIgnoreCase)).Select(d => Describe(d, true)) : [];
        var disabled = Directory.Exists(DisabledDir(def)) ? Directory.EnumerateDirectories(DisabledDir(def)).Select(d => Describe(d, false)) : [];
        return new { mods = active.Concat(disabled).ToList(), stopped = manager.Runtime(id).State is ServerState.Stopped or ServerState.Crashed or ServerState.NotInstalled };
    }

    /// <summary>Installs a mod from a .zip (one folder per mod) or a single .dll.</summary>
    public async Task<string> InstallAsync(long id, IFormFile file, string by)
    {
        var def = manager.Definition(id);
        RequireStopped(id);
        if (file.Length is 0 or > 200 * 1024 * 1024) throw new ServerException("invalid_file");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var staging = Path.Combine(def.EamDir, ".mod-upload-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        try
        {
            string name;
            if (ext == ".dll")
            {
                name = SafeName(Path.GetFileNameWithoutExtension(file.FileName));
                var dir = Path.Combine(staging, name);
                Directory.CreateDirectory(dir);
                await using var fs = File.Create(Path.Combine(dir, name + ".dll"));
                await file.CopyToAsync(fs);
            }
            else if (ext == ".zip")
            {
                var zipFile = Path.Combine(staging, "upload.zip");
                await using (var fs = File.Create(zipFile)) await file.CopyToAsync(fs);
                var extract = Path.Combine(staging, "x");
                using (var zip = ZipFile.OpenRead(zipFile))
                {
                    var root = Path.GetFullPath(extract) + Path.DirectorySeparatorChar;
                    foreach (var e in zip.Entries)
                    {
                        var target = Path.GetFullPath(Path.Combine(extract, e.FullName));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ServerException("invalid_file"); // zip-slip
                        if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        e.ExtractToFile(target, overwrite: true);
                    }
                }
                // A zip with one folder at its root is that mod; otherwise the zip's name is the mod's name.
                var top = Directory.EnumerateFileSystemEntries(extract).ToList();
                if (top.Count == 1 && Directory.Exists(top[0])) name = SafeName(Path.GetFileName(top[0]));
                else
                {
                    name = SafeName(Path.GetFileNameWithoutExtension(file.FileName));
                    var wrapped = Path.Combine(staging, "w", name);
                    Directory.CreateDirectory(Path.GetDirectoryName(wrapped)!);
                    Directory.Move(extract, wrapped);
                    extract = Path.GetDirectoryName(wrapped)!;
                }
                var src = Path.Combine(extract, name);
                var dir = Path.Combine(staging, name);
                Directory.Move(src, dir);
            }
            else throw new ServerException("invalid_file");

            Directory.CreateDirectory(ActiveDir(def));
            var dest = Path.Combine(ActiveDir(def), name);
            if (Directory.Exists(dest)) MoveAside(def, dest, "replaced");
            var disabled = Path.Combine(DisabledDir(def), name);
            if (Directory.Exists(disabled)) MoveAside(def, disabled, "replaced");
            Directory.Move(Path.Combine(staging, name), dest);
            ServerManager.ServerLog(def, $"{by}: mod {name} installed from {file.FileName}");
            return name;
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    private static void MoveAside(ServerDefinition def, string dir, string why)
    {
        var target = Path.Combine(def.EamDir, "mods-removed", $"{DateTime.Now:yyyyMMdd-HHmmss}-{why}", Path.GetFileName(dir));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.Move(dir, target);
    }

    public void SetEnabled(long id, string name, bool enabled, string by)
    {
        var def = manager.Definition(id);
        RequireStopped(id);
        name = SafeName(name);
        var from = Path.Combine(enabled ? DisabledDir(def) : ActiveDir(def), name);
        var to = Path.Combine(enabled ? ActiveDir(def) : DisabledDir(def), name);
        if (!Directory.Exists(from)) throw new ServerException("not_found");
        if (Directory.Exists(to)) throw new ServerException("name_taken");
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Directory.Move(from, to);
        ServerManager.ServerLog(def, $"{by}: mod {name} {(enabled ? "enabled" : "disabled")}");
    }

    public void Remove(long id, string name, string by)
    {
        var def = manager.Definition(id);
        RequireStopped(id);
        name = SafeName(name);
        var dir = new[] { Path.Combine(ActiveDir(def), name), Path.Combine(DisabledDir(def), name) }.FirstOrDefault(Directory.Exists)
                  ?? throw new ServerException("not_found");
        MoveAside(def, dir, "removed");
        ServerManager.ServerLog(def, $"{by}: mod {name} removed (kept in eam\\mods-removed)");
    }

    private string ConfigPath(ServerDefinition def, string name, string relative)
    {
        name = SafeName(name);
        var dir = new[] { Path.Combine(ActiveDir(def), name), Path.Combine(DisabledDir(def), name) }.FirstOrDefault(Directory.Exists)
                  ?? throw new ServerException("not_found");
        var full = Path.GetFullPath(Path.Combine(dir, relative ?? ""));
        if (!full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ServerException("invalid_name");
        if (!ConfigExtensions.Contains(Path.GetExtension(full).ToLowerInvariant())) throw new ServerException("invalid_file");
        return full;
    }

    public object ReadConfig(long id, string name, string file)
    {
        var path = ConfigPath(manager.Definition(id), name, file);
        if (!File.Exists(path)) throw new ServerException("not_found");
        if (new FileInfo(path).Length > MaxConfigSize) throw new ServerException("invalid_file");
        return new { text = File.ReadAllText(path) };
    }

    public void SaveConfig(long id, string name, string file, string text, string by)
    {
        var def = manager.Definition(id);
        var path = ConfigPath(def, name, file);
        if (!File.Exists(path)) throw new ServerException("not_found");
        if ((text ?? "").Length > MaxConfigSize) throw new ServerException("invalid_file");
        File.Copy(path, path + ".eam-backup", overwrite: true);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text ?? "", new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
        ServerManager.ServerLog(def, $"{by}: mod {name} config {file} saved (previous copy: {Path.GetFileName(path)}.eam-backup)");
    }
}
