using System.Text.Json;
using System.Text.RegularExpressions;

namespace Eam.Core.Servers;

public sealed record ScenarioInfo(string Name, string? Description, bool Stock, string? WorkshopId, DateTime? InstalledUtc, long? SizeBytes);

public sealed record KnownScenario(string Key, string Name, string WorkshopId, string Label);

/// <summary>Scenarios installed in a server (game\Content\Scenarios) and their installation from the Workshop or a folder.</summary>
public static partial class ScenarioLibrary
{
    /// <summary>Well-known community scenarios EAM can install from the Steam Workshop in one click.</summary>
    public static readonly KnownScenario[] Known =
    [
        new("re2", "Reforged Eden 2", "3143225812", "Reforged Eden 2"),
        new("re1", "Reforged Eden", "2550354956", "Reforged Eden 1"),
    ];

    public const string SourceFile = ".eam-source.json";

    private sealed record Source(string? WorkshopId, string? From, DateTime InstalledUtc);

    public static string ScenariosDir(ServerDefinition def) => Path.Combine(def.GameDir, "Content", "Scenarios");

    [GeneratedRegex(@"\[[^\]]{0,12}\]")]
    private static partial Regex Markup();

    public static IReadOnlyList<ScenarioInfo> List(ServerDefinition def)
    {
        var dir = ScenariosDir(def);
        if (!Directory.Exists(dir)) return [];
        var list = new List<ScenarioInfo>();
        foreach (var d in Directory.GetDirectories(dir))
        {
            var name = Path.GetFileName(d);
            if (name.StartsWith('.') || name.StartsWith("_Test", StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(Path.Combine(d, ".hide.txt"))) continue;
            string? desc = null;
            try
            {
                var f = Path.Combine(d, "description.txt");
                if (File.Exists(f)) desc = Markup().Replace(File.ReadAllText(f), "").Trim();
                if (desc?.Length > 300) desc = desc[..300] + "…";
            }
            catch { }
            Source? src = null;
            try
            {
                var s = Path.Combine(d, SourceFile);
                if (File.Exists(s)) src = JsonSerializer.Deserialize<Source>(File.ReadAllText(s));
            }
            catch { }
            list.Add(new ScenarioInfo(name, desc, src is null && IsStockName(name), src?.WorkshopId, src?.InstalledUtc, null));
        }
        return list.OrderBy(s => s.Stock).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsStockName(string n) =>
        n.StartsWith("Default ", StringComparison.OrdinalIgnoreCase) || n is "Tutorial" or "Guided Tutorial" or "Rise of the Dark Faction"
            or "Invader vs Defender - Conflict of Cygnus" or "Shadows of Starlight" or "Stranded at Nemesis (Survival Challenge)";

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Trim() != name || name.StartsWith('.')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
            throw new ArgumentException("invalid_scenario_name");
    }

    /// <summary>The folder that really is the scenario (contains gameoptions.yaml or Playfields), possibly one level down.</summary>
    public static string? FindRoot(string dir)
    {
        static bool IsScenario(string d) => File.Exists(Path.Combine(d, "gameoptions.yaml")) || Directory.Exists(Path.Combine(d, "Playfields"));
        if (!Directory.Exists(dir)) return null;
        if (IsScenario(dir)) return dir;
        var subs = Directory.GetDirectories(dir).Where(IsScenario).ToList();
        return subs.Count == 1 ? subs[0] : null;
    }

    /// <summary>Copies a scenario into the server. The old version is only removed once the new one is complete.</summary>
    public static void Install(ServerDefinition def, string sourceDir, string name, string? workshopId, IProgress<double>? progress = null)
    {
        ValidateName(name);
        var root = FindRoot(sourceDir) ?? throw new DirectoryNotFoundException("scenario_not_found");
        var dir = ScenariosDir(def);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, name);
        var tmp = Path.Combine(dir, $".eam-new-{Guid.NewGuid():N}");
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        var done = 0;
        try
        {
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(root, f);
                var dest = Path.Combine(tmp, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, overwrite: true);
                if (++done % 50 == 0) progress?.Report(100.0 * done / files.Length);
            }
            File.WriteAllText(Path.Combine(tmp, SourceFile), JsonSerializer.Serialize(new Source(workshopId, workshopId is null ? root : null, DateTime.UtcNow)));
            string? old = null;
            if (Directory.Exists(target))
            {
                old = Path.Combine(dir, $".eam-old-{Guid.NewGuid():N}");
                Directory.Move(target, old);
            }
            Directory.Move(tmp, target);
            if (old is not null) try { Directory.Delete(old, true); } catch { }
        }
        finally
        {
            if (Directory.Exists(tmp)) try { Directory.Delete(tmp, true); } catch { }
        }
        progress?.Report(100);
    }

    public static void Delete(ServerDefinition def, string name)
    {
        ValidateName(name);
        var target = Path.Combine(ScenariosDir(def), name);
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException("scenario_not_found");
        Directory.Delete(target, true);
    }

    /// <summary>
    /// Workshop item already downloaded by the Steam client of this machine (subscribed scenario), if any.
    /// Used when SteamCMD cannot download it anonymously.
    /// </summary>
    public static string? FindInSteamClient(string itemId)
    {
        foreach (var lib in SteamLibraries())
        {
            var d = SteamCmd.WorkshopItemDir(lib, itemId);
            if (Directory.Exists(d)) return d;
        }
        return null;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPath();

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (k?.GetValue("SteamPath") is string p) roots.Add(p.Replace('/', '\\'));
            }
            catch { }
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                if (k?.GetValue("InstallPath") is string p) roots.Add(p);
            }
            catch { }
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        }
        var all = new List<string>();
        foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            all.Add(r);
            try
            {
                var vdf = Path.Combine(r, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf))) all.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch { }
        }
        return all.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
