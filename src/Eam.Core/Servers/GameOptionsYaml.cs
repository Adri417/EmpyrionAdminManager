using System.Text;
using System.Text.RegularExpressions;

namespace Eam.Core.Servers;

/// <summary>
/// The game options of a save (Saves\Games\&lt;GameName&gt;\gameoptions.yaml), copied from the scenario when the
/// save is created. The file holds several blocks ("- ValidFor: [ MP, Survival ]"…); a dedicated server
/// uses the MP block of its mode. Edited line by line so comments stay in place.
/// </summary>
public sealed partial class GameOptionsYaml
{
    private readonly List<string> _lines;

    [GeneratedRegex(@"^(\s*)(#\s*)?([A-Za-z_][A-Za-z0-9_]*):(.*)$")]
    private static partial Regex KeyLine();

    [GeneratedRegex(@"^\s*-\s*ValidFor:\s*\[(.*)\]")]
    private static partial Regex ValidForLine();

    private GameOptionsYaml(IEnumerable<string> lines) => _lines = lines.ToList();

    public static GameOptionsYaml Parse(string text) => new(text.Replace("\r\n", "\n").Split('\n'));
    public static GameOptionsYaml Load(string file) => Parse(File.ReadAllText(file));

    public override string ToString() => string.Join("\r\n", _lines).TrimEnd() + "\r\n";

    public void Save(string file)
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, ToString(), new UTF8Encoding(false));
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>"Survival" or "Creative" block for multiplayer: [start, end) line range, or null.</summary>
    private (int Start, int End)? Block(string mode)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var m = ValidForLine().Match(_lines[i]);
            if (!m.Success) continue;
            var tags = m.Groups[1].Value.Split(',').Select(x => x.Trim()).ToList();
            if (!tags.Contains("MP", StringComparer.OrdinalIgnoreCase) || !tags.Contains(mode, StringComparer.OrdinalIgnoreCase)) continue;
            var end = i + 1;
            while (end < _lines.Count && !ValidForLine().IsMatch(_lines[end]) && !(_lines[end].Length > 0 && !char.IsWhiteSpace(_lines[end][0]) && _lines[end][0] != '#'))
                end++;
            return (i + 1, end);
        }
        return null;
    }

    /// <summary>Options of the multiplayer block of <paramref name="mode"/> (active or commented out).</summary>
    public IReadOnlyList<YamlEntry> Entries(string mode) => Block(mode) is { } b ? Read(b.Start, b.End, mode) : [];

    /// <summary>Every option found in any block (used with gameoptions_example.yaml as the list of known options).</summary>
    public IReadOnlyList<YamlEntry> AllEntries()
    {
        var all = Read(0, _lines.Count, "*");
        return all.GroupBy(e => e.Key).Select(g => g.OrderByDescending(e => e.Help?.Length ?? 0).First()).ToList();
    }

    private List<YamlEntry> Read(int start, int end, string section)
    {
        var list = new List<YamlEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var help = new List<string>();
        for (var i = start; i < end; i++)
        {
            var line = _lines[i];
            if (line.Trim().Length == 0 || ValidForLine().IsMatch(line)) { help.Clear(); continue; }
            var m = KeyLine().Match(line);
            if (!m.Success || m.Groups[3].Value is "Options" or "ValidFor")
            {
                var c = line.Trim().TrimStart('#').Trim();
                if (c.Length > 0 && !c.StartsWith("!!")) help.Add(c);
                continue;
            }
            var (value, comment) = Split(m.Groups[4].Value);
            var commented = m.Groups[2].Success;
            if (commented && value.Length == 0) { help.Add(line.Trim().TrimStart('#').Trim()); continue; }
            if (seen.Add(m.Groups[3].Value))
            {
                var h = string.Join(" ", help.Append(comment ?? "").Where(x => x.Length > 0 && x != "default value"));
                list.Add(new YamlEntry(section, m.Groups[3].Value, value, !commented, h.Length > 0 ? h : null));
            }
            help.Clear();
        }
        return list;
    }

    private static (string, string?) Split(string raw)
    {
        var v = raw.Trim();
        string? comment = null;
        if (!v.StartsWith('"'))
        {
            var hash = v.StartsWith('#') ? 0 : v.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0) { comment = v[hash..].TrimStart(' ', '#').Trim(); v = v[..hash].Trim(); }
        }
        else
        {
            var end = v.IndexOf('"', 1);
            if (end > 0 && end + 1 < v.Length) { comment = v[(end + 1)..].TrimStart(' ', '#').Trim(); v = v[..(end + 1)]; }
        }
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1];
        return (v, string.IsNullOrEmpty(comment) ? null : comment);
    }

    private int Find(int start, int end, string key, bool includeCommented)
    {
        var commented = -1;
        for (var i = start; i < end; i++)
        {
            var m = KeyLine().Match(_lines[i]);
            if (!m.Success || m.Groups[3].Value != key) continue;
            if (!m.Groups[2].Success) return i;
            if (includeCommented && commented < 0 && Split(m.Groups[4].Value).Item1.Length > 0) commented = i;
        }
        return commented;
    }

    public GameOptionsYaml Set(string mode, string key, string value)
    {
        if (!Regex.IsMatch(key, "^[A-Za-z_][A-Za-z0-9_]*$")) throw new FormatException("invalid_key");
        value = (value ?? "").Trim();
        if (value.IndexOfAny(['\r', '\n']) >= 0) throw new FormatException("invalid_value");
        var rendered = value.Length > 0 && (value.IndexOfAny([':', '#', '"', '[', ']', '{', '}', ',', '&', '*', '!', '|', '>', '%', '@', '`']) >= 0 || value != value.Trim())
            ? "\"" + value.Replace("\"", "'") + "\"" : value;
        var b = Block(mode);
        if (b is null)
        {
            if (!_lines.Any(l => l.TrimEnd() == "Options:")) _lines.Add("Options:");
            _lines.Add($"  - ValidFor: [ MP, {mode} ]");
            b = (_lines.Count, _lines.Count);
        }
        var (start, end) = b.Value;
        var i = Find(start, end, key, includeCommented: true);
        if (i >= 0)
        {
            var m = KeyLine().Match(_lines[i]);
            var indent = m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : "    ";
            var comment = Split(m.Groups[4].Value).Item2;
            if (comment == "default value") comment = null; // no longer true once changed
            var text = $"{indent}{key}: {rendered}";
            if (comment is not null) text = text.PadRight(Math.Max(text.Length + 1, 38)) + "# " + comment;
            _lines[i] = text;
        }
        else
        {
            // Insert after the last non-empty line of the block.
            var at = end;
            while (at > start && _lines[at - 1].Trim().Length == 0) at--;
            _lines.Insert(at, $"    {key}: {rendered}");
        }
        return this;
    }

    public GameOptionsYaml Disable(string mode, string key)
    {
        if (Block(mode) is not { } b) return this;
        var i = Find(b.Start, b.End, key, includeCommented: false);
        if (i >= 0)
        {
            var line = _lines[i];
            var indent = line[..(line.Length - line.TrimStart().Length)];
            _lines[i] = indent + "# " + line.TrimStart();
        }
        return this;
    }
}
