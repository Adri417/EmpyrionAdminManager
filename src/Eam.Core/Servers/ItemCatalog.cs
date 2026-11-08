using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Eam.Core.Servers;

public sealed record CatalogItem(int Id, string Name, string Label, bool IsBlock);

/// <summary>
/// Item and block names of the scenario a server runs (ItemsConfig.ecf / BlocksConfig.ecf),
/// translated with the scenario's Localization.csv. Falls back to the game's own files.
/// </summary>
public static partial class ItemCatalog
{
    [GeneratedRegex(@"^\{\s*\+?(Item|Block)\s+(?:Id:\s*(\d+)\s*,\s*)?Name:\s*(?:""([^""]+)""|([^\s,}]+))", RegexOptions.Multiline)]
    private static partial Regex Entry();

    [GeneratedRegex(@"\[[^\]]{0,12}\]")]
    private static partial Regex Markup();

    /// <summary>
    /// In inventories, items are numbered after the block ids: inventory id = ItemsConfig Id + 8192
    /// (O2 bottle 80 -> 8272). Blocks keep their own id.
    /// </summary>
    public const int ItemIdOffset = 8192;

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, IReadOnlyList<CatalogItem> Items)> Cache = new();

    /// <summary>
    /// The game's own configuration first, then the scenario's, which overrides it (same name or id).
    /// Blocks written without an id get the one the save gave them (Saves\Games\&lt;game&gt;\blocksmap.dat).
    /// </summary>
    public static IReadOnlyList<CatalogItem> Load(ServerDefinition def, string? scenario, string language)
    {
        var baseConfig = Path.Combine(def.GameDir, "Content", "Configuration");
        var configDirs = new List<string> { baseConfig };
        var locFiles = new List<string> { Path.Combine(def.GameDir, "Content", "Extras", "Localization.csv") };
        if (!string.IsNullOrWhiteSpace(scenario))
        {
            var sc = Path.Combine(ScenarioLibrary.ScenariosDir(def), scenario);
            configDirs.Add(Path.Combine(sc, "Content", "Configuration"));
            locFiles.Add(Path.Combine(sc, "Extras", "Localization.csv"));
            locFiles.Add(Path.Combine(sc, "Content", "Extras", "Localization.csv"));
        }
        var files = new List<string>();
        foreach (var d in configDirs)
            foreach (var n in new[] { "BlocksConfig.ecf", "ItemsConfig.ecf" })
                if (File.Exists(Path.Combine(d, n))) files.Add(Path.Combine(d, n));
        if (files.Count == 0) return [];
        var locs = locFiles.Where(File.Exists).ToList();
        var blocksMap = BlocksMapFile(def);
        var stampFiles = files.Concat(locs).Concat(blocksMap is null ? [] : [blocksMap]).ToList();
        var stamp = stampFiles.Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
        var key = string.Join("|", stampFiles) + "|" + language;
        if (Cache.TryGetValue(key, out var c) && c.Stamp == stamp) return c.Items;

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var loc in locs)
            foreach (var kv in ReadLocalization(loc, language)) names[kv.Key] = kv.Value; // scenario last: wins
        var mapped = blocksMap is null ? new Dictionary<string, int>() : ReadBlocksMap(blocksMap);
        var byName = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            foreach (Match m in Entry().Matches(File.ReadAllText(f)))
            {
                var isBlock = m.Groups[1].Value == "Block";
                var name = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
                int id;
                if (m.Groups[2].Success) id = int.Parse(m.Groups[2].Value) + (isBlock ? 0 : ItemIdOffset);
                else if (isBlock && mapped.TryGetValue(name, out var mid)) id = mid;
                else if (byName.TryGetValue(name, out var known)) id = known.Id;
                else continue;
                if (id == 0) continue;
                var label = names.TryGetValue(name, out var l) ? l : name;
                byName[name] = new CatalogItem(id, name, label, isBlock);
            }
        }
        var items = new Dictionary<int, CatalogItem>();
        foreach (var it in byName.Values) items[it.Id] = it;
        var list = items.Values.OrderBy(i => i.Label, StringComparer.OrdinalIgnoreCase).ToList();
        Cache[key] = (stamp, list);
        return list;
    }

    /// <summary>blocksmap.dat of the save the server plays (null when unknown).</summary>
    public static string? BlocksMapFile(ServerDefinition def)
    {
        try
        {
            if (!File.Exists(def.YamlFile)) return null;
            var y = DedicatedYaml.Load(def.YamlFile);
            var game = y.Get("GameConfig", "GameName");
            if (string.IsNullOrWhiteSpace(game)) return null;
            var saves = y.Get("GameConfig", "SaveDirectory") ?? y.Get("ServerConfig", "SaveDirectory");
            if (string.IsNullOrWhiteSpace(saves)) saves = Path.Combine(def.GameDir, "Saves");
            else if (!Path.IsPathFullyQualified(saves)) saves = Path.Combine(def.GameDir, saves);
            var f = Path.Combine(saves, "Games", game, "blocksmap.dat");
            return File.Exists(f) ? f : null;
        }
        catch { return null; }
    }

    /// <summary>blocksmap.dat: 9-byte header, then [name length][ASCII name][uint16 id] repeated.</summary>
    public static Dictionary<string, int> ReadBlocksMap(string file)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var b = File.ReadAllBytes(file);
            var i = 9;
            while (i < b.Length)
            {
                int len = b[i++];
                if (len == 0 || i + len + 2 > b.Length) break;
                var name = Encoding.ASCII.GetString(b, i, len);
                i += len;
                map[name] = b[i] | (b[i + 1] << 8);
                i += 2;
            }
        }
        catch { }
        return map;
    }

    private static Dictionary<string, string> ReadLocalization(string file, string language)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StreamReader(file, Encoding.UTF8);
        var header = ParseCsvLine(reader.ReadLine() ?? "");
        var col = header.FindIndex(h => language == "fr" ? h.StartsWith("Fran", StringComparison.OrdinalIgnoreCase)
            : language == "de" ? h.StartsWith("Deutsch", StringComparison.OrdinalIgnoreCase)
            : language == "es" ? h.StartsWith("Spanish", StringComparison.OrdinalIgnoreCase) : h == "English");
        if (col < 0) col = 1;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var cells = ParseCsvLine(line);
            if (cells.Count < 2 || cells[0].Length == 0) continue;
            var text = cells.Count > col && cells[col].Length > 0 ? cells[col] : cells[1];
            text = Markup().Replace(text, "").Trim();
            if (text.Length > 0) result.TryAdd(cells[0], text);
        }
        return result;
    }

    public static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else sb.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        cells.Add(sb.ToString());
        return cells;
    }
}
