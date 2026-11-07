using System.Text;

namespace Eam.Core.Servers;

public sealed record ElevatedPlayer(string SteamId, int Permission);

/// <summary>
/// Saves\adminconfig.yaml: the players with a role (Elevated: 3 GameMaster, 6 Moderator, 9 Admin) and the bans.
/// EAM only rewrites the Elevated list; every other part of the file is kept as it is.
/// </summary>
public sealed class AdminConfig
{
    public static readonly int[] Permissions = [3, 6, 9];

    public static string FileOf(ServerDefinition def) => Path.Combine(def.GameDir, "Saves", "adminconfig.yaml");

    public static IReadOnlyList<ElevatedPlayer> ReadElevated(string file)
    {
        if (!File.Exists(file)) return [];
        var list = new List<ElevatedPlayer>();
        var (_, block, _) = SplitSections(File.ReadAllLines(file), "Elevated");
        string? id = null;
        int? perm = null;
        void Flush()
        {
            if (id is { Length: > 0 } && perm is { } p) list.Add(new ElevatedPlayer(id, p));
            id = null; perm = null;
        }
        foreach (var raw in block)
        {
            var line = Strip(raw);
            if (line.Length == 0) continue;
            var t = line.TrimStart();
            if (t.StartsWith('-')) { Flush(); t = t[1..].TrimStart(); }
            var colon = t.IndexOf(':');
            if (colon < 0) continue;
            var k = t[..colon].Trim();
            var v = t[(colon + 1)..].Trim().Trim('"', '\'');
            if (k.Equals("Id", StringComparison.OrdinalIgnoreCase)) id = v;
            else if (k.Equals("Permission", StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out var p)) perm = p;
        }
        Flush();
        return list;
    }

    /// <summary>Sets (permission 3/6/9) or removes (0) the role of one player.</summary>
    public static void SetRole(string file, string steamId, int permission)
    {
        if (steamId.Length is < 5 or > 20 || !steamId.All(char.IsDigit)) throw new FormatException("invalid_steam_id");
        if (permission != 0 && !Permissions.Contains(permission)) throw new FormatException("invalid_permission");
        var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : ["---", "# Admin configuration (managed by Empyrion Admin Manager for the Elevated list)"];
        var players = ReadElevated(file).Where(p => p.SteamId != steamId).ToList();
        if (permission != 0) players.Add(new ElevatedPlayer(steamId, permission));

        var (before, _, after) = SplitSections(lines, "Elevated");
        var block = new List<string> { "Elevated:" };
        foreach (var p in players.OrderByDescending(p => p.Permission))
        {
            block.Add($"- Id: {p.SteamId}");
            block.Add($"  Permission: {p.Permission}");
        }
        if (players.Count == 0) block[0] = "Elevated: []";
        var all = before.Concat(block).Concat(after).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + ".tmp", string.Join("\r\n", all).TrimEnd() + "\r\n", new UTF8Encoding(false));
        File.Move(file + ".tmp", file, overwrite: true);
    }

    /// <summary>Lines before the section, the section's lines (header excluded), lines after it.</summary>
    private static (List<string> Before, List<string> Block, List<string> After) SplitSections(IReadOnlyList<string> lines, string name)
    {
        var start = -1;
        for (var i = 0; i < lines.Count; i++)
            if (Strip(lines[i]).TrimEnd() is var l && (l == name + ":" || l.StartsWith(name + ": ", StringComparison.Ordinal)))
            { start = i; break; }
        if (start < 0) return (lines.ToList(), [], []);
        var end = start + 1;
        while (end < lines.Count)
        {
            var l = lines[end];
            if (l.Length > 0 && char.IsLetter(l[0])) break; // next top-level key
            end++;
        }
        return (lines.Take(start).ToList(), lines.Skip(start + 1).Take(end - start - 1).ToList(), lines.Skip(end).ToList());
    }

    private static string Strip(string line)
    {
        var hash = line.IndexOf('#');
        return hash >= 0 ? line[..hash] : line;
    }

    public static string RoleName(int permission) => permission switch { 9 => "admin", 6 => "moderator", 3 => "gamemaster", _ => "player" };
}
