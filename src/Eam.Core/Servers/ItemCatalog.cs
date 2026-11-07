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
    [GeneratedRegex(@"^\{\s*\+?(Item|Block)\s+Id:\s*(\d+)\s*,\s*Name:\s*([^\s,]+)", RegexOptions.Multiline)]
    private static partial Regex Entry();

    [GeneratedRegex(@"\[[^\]]{0,12}\]")]
    private static partial Regex Markup();

    public const int ItemIdOffset = 4096;

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, IReadOnlyList<CatalogItem> Items)> Cache = new();

    public static IReadOnlyList<CatalogItem> Load(ServerDefinition def, string? scenario, string language)
    {
        var configDirs = new List<string>();
        var locFiles = new List<string>();
        if (!string.IsNullOrWhiteSpace(scenario))
        {
            var s = Path.Combine(ScenarioLibrary.ScenariosDir(def), scenario);
            configDirs.Add(Path.Combine(s, "Content", "Configuration"));
            locFiles.Add(Path.Combine(s, "Extras", "Localization.csv"));
        }
        configDirs.Add(Path.Combine(def.GameDir, "Content", "Configuration"));
        locFiles.Add(Path.Combine(def.GameDir, "Content", "Extras", "Localization.csv"));

        var config = configDirs.FirstOrDefault(d => File.Exists(Path.Combine(d, "ItemsConfig.ecf")));
        if (config is null) return [];
        var loc = locFiles.FirstOrDefault(File.Exists);
        var files = new[] { Path.Combine(config, "ItemsConfig.ecf"), Path.Combine(config, "BlocksConfig.ecf"), loc }.OfType<string>().Where(File.Exists).ToList();
        var stamp = files.Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
        var key = string.Join("|", files) + "|" + language;
        if (Cache.TryGetValue(key, out var c) && c.Stamp == stamp) return c.Items;

        var names = loc is null ? new Dictionary<string, string>() : ReadLocalization(loc, language);
        var items = new Dictionary<int, CatalogItem>();
        foreach (var f in files.Where(f => f.EndsWith(".ecf", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (Match m in Entry().Matches(File.ReadAllText(f)))
            {
                var isBlock = m.Groups[1].Value == "Block";
                // In inventories, items are numbered after the 4096 block ids (IronIngot 224 -> 4320).
                var id = int.Parse(m.Groups[2].Value) + (isBlock ? 0 : ItemIdOffset);
                var name = m.Groups[3].Value;
                if (id == 0) continue;
                var label = names.TryGetValue(name, out var l) ? l : name;
                items[id] = new CatalogItem(id, name, label, isBlock);
            }
        }
        var list = items.Values.OrderBy(i => i.Label, StringComparer.OrdinalIgnoreCase).ToList();
        Cache[key] = (stamp, list);
        return list;
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
