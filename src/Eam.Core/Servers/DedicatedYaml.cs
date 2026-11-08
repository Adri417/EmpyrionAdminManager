using System.Text;
using System.Text.RegularExpressions;

namespace Eam.Core.Servers;

/// <summary>
/// Reads and edits dedicated.yaml line by line, so comments and the admin's own
/// settings are kept exactly as they are. Only the keys EAM changes are touched.
/// </summary>
public sealed record YamlEntry(string Section, string Key, string Value, bool Active, string? Help);

public sealed partial class DedicatedYaml
{
    [GeneratedRegex(@"^(\s*)(#\s*)?([A-Za-z_][A-Za-z0-9_]*):(.*)$")]
    private static partial Regex KeyLine();

    private readonly List<string> _lines;

    private DedicatedYaml(IEnumerable<string> lines) => _lines = lines.ToList();

    public static DedicatedYaml Parse(string text) => new(text.Replace("\r\n", "\n").Split('\n'));

    public static DedicatedYaml Load(string file) => Parse(File.ReadAllText(file));

    /// <summary>Minimal configuration used when the game's own template is not available.</summary>
    public static DedicatedYaml Default() => Parse("""
        # Created by Empyrion Admin Manager. Steam updates do not touch this file.
        ServerConfig:
            Srv_Port: 30000
            Srv_Name: My Empyrion Server
            Srv_Password:
            Srv_MaxPlayers: 8
            Srv_ReservePlayfields: 1
            Srv_Description:
            Srv_Public: true
            Srv_StopPeriod: 2
            Tel_Enabled: true
            Tel_Port: 30004
            Tel_Pwd:
            EACActive: true
        GameConfig:
            GameName: DediGame
            Mode: Survival
            Seed: 1011345
            CustomScenario: Default Random
        """);

    public string? Get(string section, string key)
    {
        var i = FindKey(section, key, includeCommented: false);
        if (i < 0) return null;
        var line = _lines[i];
        var v = line[(line.IndexOf(':') + 1)..].Trim();
        var hash = v.StartsWith('"') ? -1 : v.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0) v = v[..hash].Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return v;
    }

    /// <summary>
    /// Every option of every top-level section, active or commented out (the game's template lists most
    /// options as comments). The comment lines just above an option and its end-of-line comment become its help.
    /// </summary>
    public IReadOnlyList<YamlEntry> Entries()
    {
        var list = new List<YamlEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? section = null;
        var help = new List<string>();
        foreach (var line in _lines)
        {
            if (line.Trim().Length == 0) { help.Clear(); continue; }
            if (!char.IsWhiteSpace(line[0]) && line[0] != '#')
            {
                section = line.TrimEnd().EndsWith(':') ? line.TrimEnd()[..^1] : null;
                help.Clear();
                continue;
            }
            if (section is null) continue;
            var m = KeyLine().Match(line);
            var commented = m.Success && m.Groups[2].Success;
            if (!m.Success || m.Groups[1].Value.Length == 0 && !commented)
            {
                var c = line.Trim().TrimStart('#').Trim();
                if (c.Length > 0) help.Add(c);
                continue;
            }
            var (value, comment) = SplitValue(m.Groups[4].Value);
            if (commented && value.Length == 0)
            {
                // "# Notes:" in a comment block: text, not an option.
                help.Add(line.Trim().TrimStart('#').Trim());
                continue;
            }
            var id = section + "/" + m.Groups[3].Value;
            if (seen.Add(id))
            {
                var h = string.Join(" ", help.Append(comment ?? "").Where(x => x.Length > 0));
                list.Add(new YamlEntry(section, m.Groups[3].Value, value, !commented, h.Length > 0 ? h : null));
            }
            help.Clear();
        }
        return list;
    }

    private static (string Value, string? Comment) SplitValue(string raw)
    {
        var v = raw.Trim();
        string? comment = null;
        if (!v.StartsWith('"'))
        {
            var hash = v.IndexOf(" #", StringComparison.Ordinal);
            if (v.StartsWith('#')) hash = 0;
            if (hash >= 0) { comment = v[hash..].TrimStart(' ', '#').Trim(); v = v[..hash].Trim(); }
        }
        else
        {
            var end = v.IndexOf('"', 1);
            while (end > 0 && v[end - 1] == '\\') end = v.IndexOf('"', end + 1);
            if (end > 0 && end + 1 < v.Length)
            {
                comment = v[(end + 1)..].TrimStart(' ', '#').Trim();
                v = v[..(end + 1)];
            }
        }
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') v = v[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return (v, string.IsNullOrEmpty(comment) ? null : comment);
    }

    /// <summary>Turns an active option back into a comment (the game then uses its default).</summary>
    public DedicatedYaml Disable(string section, string key)
    {
        var i = FindKey(section, key, includeCommented: false);
        if (i >= 0) _lines[i] = Indent(_lines[i]) + "# " + _lines[i].TrimStart();
        return this;
    }

    public DedicatedYaml Set(string section, string key, string? value)
    {
        var rendered = Render(value);
        var i = FindKey(section, key, includeCommented: true);
        if (i >= 0)
        {
            var commented = _lines[i].TrimStart().StartsWith('#');
            var indent = SectionIndent(section); // also repairs a key written with the wrong indentation
            string? comment = null;
            if (!commented)
            {
                var old = _lines[i][(_lines[i].IndexOf(':') + 1)..];
                comment = SplitValue(old).Comment;
            }
            var text = $"{indent}{key}: {rendered}";
            if (comment is not null) text = text.PadRight(Math.Max(text.Length + 1, 45)) + "# " + comment;
            _lines[i] = text.TrimEnd();
            return this;
        }
        var s = FindSection(section);
        if (s < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add("");
            _lines.Add(section + ":");
            s = _lines.Count - 1;
        }
        _lines.Insert(s + 1, $"{SectionIndent(section)}{key}: {rendered}".TrimEnd());
        return this;
    }

    public override string ToString() => string.Join("\r\n", _lines).TrimEnd() + "\r\n";

    /// <summary>Atomic write: never leaves a half-written configuration behind.</summary>
    public void Save(string file)
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, ToString(), new UTF8Encoding(false));
        File.Move(tmp, file, overwrite: true);
    }

    private static string Render(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var needsQuotes = value.IndexOfAny([':', '#', '"', '\'', '{', '}', '[', ']', ',', '&', '*', '!', '|', '>', '%', '@', '`']) >= 0
                          || value != value.Trim();
        return needsQuotes ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : value;
    }

    private static string Indent(string line) => line[..(line.Length - line.TrimStart().Length)];

    /// <summary>
    /// Indentation used by the active keys of a section (Empyrion's own files use 2 spaces, EAM's default 4).
    /// A new or re-enabled key must use the same, or the game cannot read the file at all.
    /// </summary>
    private string SectionIndent(string section)
    {
        var s = FindSection(section);
        if (s >= 0)
            for (var i = s + 1; i < _lines.Count; i++)
            {
                var line = _lines[i];
                if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && line[0] != '#') break;
                var m = KeyLine().Match(line);
                if (m.Success && !m.Groups[2].Success && m.Groups[1].Value.Length > 0) return m.Groups[1].Value;
            }
        return "    ";
    }

    private int FindSection(string section)
    {
        for (var i = 0; i < _lines.Count; i++)
            if (_lines[i].TrimEnd() == section + ":") return i;
        return -1;
    }

    private int FindKey(string section, string key, bool includeCommented)
    {
        var s = FindSection(section);
        if (s < 0) return -1;
        var commented = -1;
        for (var i = s + 1; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && line[0] != '#') break; // next section
            var t = line.TrimStart();
            if (t.StartsWith(key + ":", StringComparison.Ordinal)) return i;
            if (includeCommented && commented < 0 && t.StartsWith('#') && t.TrimStart('#', ' ').StartsWith(key + ":", StringComparison.Ordinal))
                commented = i;
        }
        return commented;
    }
}
