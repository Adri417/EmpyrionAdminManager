using System.Text;

namespace Eam.Core.Servers;

/// <summary>
/// Reads and edits dedicated.yaml line by line, so comments and the admin's own
/// settings are kept exactly as they are. Only the keys EAM changes are touched.
/// </summary>
public sealed class DedicatedYaml
{
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

    public DedicatedYaml Set(string section, string key, string? value)
    {
        var rendered = Render(value);
        var i = FindKey(section, key, includeCommented: true);
        if (i >= 0)
        {
            var indent = _lines[i].TrimStart().StartsWith('#') ? "    " : Indent(_lines[i]);
            _lines[i] = $"{indent}{key}: {rendered}".TrimEnd();
            return this;
        }
        var s = FindSection(section);
        if (s < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add("");
            _lines.Add(section + ":");
            s = _lines.Count - 1;
        }
        _lines.Insert(s + 1, $"    {key}: {rendered}".TrimEnd());
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
