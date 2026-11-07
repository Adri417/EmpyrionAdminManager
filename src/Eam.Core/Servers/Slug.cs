using System.Text;

namespace Eam.Core.Servers;

public static class Slug
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>"Les Rescapés - Saison 6" → "les-rescapes-saison-6". Always a valid Windows folder name.</summary>
    public static string From(string name)
    {
        var sb = new StringBuilder();
        var dash = false;
        foreach (var raw in Fold(name ?? ""))
        {
            var c = raw;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') { sb.Append(c); dash = false; }
            else if (c is >= 'A' and <= 'Z') { sb.Append(char.ToLowerInvariant(c)); dash = false; }
            else if (!dash && sb.Length > 0) { sb.Append('-'); dash = true; }
            if (sb.Length >= 40) break;
        }
        var s = sb.ToString().Trim('-');
        if (s.Length == 0) s = "server";
        if (Reserved.Contains(s)) s += "-server";
        return s;
    }

    // Works without ICU (the app runs with invariant globalization).
    private const string From_ = "àáâãäåāăąçćčďèéêëēėęěìíîïīįłñńňòóôõöøōőŕřśšşťùúûüūůűųýÿźżžÀÁÂÃÄÅĀĂĄÇĆČĎÈÉÊËĒĖĘĚÌÍÎÏĪĮŁÑŃŇÒÓÔÕÖØŌŐŔŘŚŠŞŤÙÚÛÜŪŮŰŲÝŸŹŻŽ";
    private const string To_   = "aaaaaaaaacccdeeeeeeeeiiiiiilnnnoooooooorrssstuuuuuuuuyyzzzAAAAAAAAACCCDEEEEEEEEIIIIIILNNNOOOOOOOORRSSSTUUUUUUUUYYZZZ";

    private static IEnumerable<char> Fold(string name)
    {
        foreach (var c in name)
        {
            var i = From_.IndexOf(c);
            if (i >= 0) { yield return To_[i]; continue; }
            switch (c)
            {
                case 'æ': yield return 'a'; yield return 'e'; break;
                case 'Æ': yield return 'A'; yield return 'E'; break;
                case 'œ': yield return 'o'; yield return 'e'; break;
                case 'Œ': yield return 'O'; yield return 'E'; break;
                case 'ß': yield return 's'; yield return 's'; break;
                default: yield return c; break;
            }
        }
    }
}
