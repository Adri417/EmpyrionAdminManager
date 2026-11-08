namespace Eam.Core.Servers;

/// <summary>
/// Item pictures of a server. Each server has its own (RE1 and RE2 do not share ids nor pictures):
/// 1. eam\icons\ (pictures the admin adds: &lt;inventory id&gt;.png or &lt;item name&gt;.png),
/// 2. the scenario's icons: Content\Scenarios\&lt;scenario&gt;\SharedData\Content\Bundles\ItemIcons\&lt;name&gt;.png,
/// 3. the game's: Content\Bundles\ItemIcons\&lt;name&gt;.png,
/// 4. the pictures of an EAH copied with the server (EmpyrionAdminHelper\Items\&lt;name&gt;.png).
/// </summary>
public static class ItemIcons
{
    public static string Folder(ServerDefinition def) => Path.Combine(def.EamDir, "icons");

    public static IEnumerable<string> Folders(ServerDefinition def, string? scenario)
    {
        yield return Folder(def);
        if (!string.IsNullOrWhiteSpace(scenario))
            yield return Path.Combine(ScenarioLibrary.ScenariosDir(def), scenario, "SharedData", "Content", "Bundles", "ItemIcons");
        yield return Path.Combine(def.GameDir, "Content", "Bundles", "ItemIcons");
        foreach (var eah in new[] { Path.Combine(def.GameDir, "DedicatedServer", "EmpyrionAdminHelper", "Items"), Path.Combine(def.GameDir, "EmpyrionAdminHelper", "Items") })
            yield return eah;
    }

    public static string? Find(ServerDefinition def, string? scenario, int itemId, string? name)
    {
        foreach (var dir in Folders(def, scenario))
        {
            if (!Directory.Exists(dir)) continue;
            var byId = Path.Combine(dir, itemId + ".png");
            if (File.Exists(byId)) return byId;
            if (name is null) continue;
            var byName = Path.Combine(dir, name + ".png");
            if (File.Exists(byName)) return byName;
            // Linux and other case-sensitive file systems.
            if (OperatingSystem.IsWindows()) continue;
            var any = Directory.EnumerateFiles(dir, "*.png").FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.OrdinalIgnoreCase));
            if (any is not null) return any;
        }
        return null;
    }
}
