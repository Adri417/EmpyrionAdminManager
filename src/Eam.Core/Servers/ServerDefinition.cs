namespace Eam.Core.Servers;

/// <summary>A game server managed by EAM. Each one owns a separate folder and separate ports.</summary>
public sealed record ServerDefinition
{
    public long Id { get; init; }
    /// <summary>Folder-safe name, derived from <see cref="Name"/>.</summary>
    public required string Slug { get; init; }
    /// <summary>Free name chosen by the admin, also shown in the game's server list.</summary>
    public required string Name { get; init; }
    /// <summary>servers\&lt;slug&gt;: contains game\ (the Empyrion installation) and eam\ (EAM's own files for this server).</summary>
    public required string RootDir { get; init; }
    /// <summary>First game port. Empyrion uses this port and the 3 following ones.</summary>
    public required int GamePort { get; init; }
    public required int TelnetPort { get; init; }
    public required string TelnetPassword { get; init; }
    /// <summary>Steam branch: "public" or "experimental".</summary>
    public string Branch { get; init; } = "public";
    public bool AutoStart { get; init; }
    public bool AutoRestart { get; init; } = true;
    public bool Installed { get; init; }
    public string? BuildId { get; init; }
    public DateTime CreatedUtc { get; init; }
    /// <summary>Secret shared with this server's EAM mod only (local link authentication).</summary>
    public string ModKey { get; init; } = "";

    /// <summary>Local port of the EAM mod: game port + 5 (or + 6 if telnet already uses + 5).</summary>
    public int ModPort => PortAllocator.ModPortFor(GamePort, TelnetPort);

    /// <summary>
    /// An existing Empyrion folder used in place (server imported from another tool without copying it).
    /// Null for servers installed by EAM: the game is then in RootDir\game.
    /// </summary>
    public string? GameDirOverride { get; init; }

    public string GameDir => GameDirOverride ?? Path.Combine(RootDir, "game");
    public string EamDir => Path.Combine(RootDir, "eam");
    /// <summary>
    /// EAM's own copy of the configuration. Steam updates can overwrite dedicated.yaml, never this file.
    /// </summary>
    public string YamlFile => Path.Combine(GameDir, "dedicated-eam.yaml");
    public string ShippedYamlFile => Path.Combine(GameDir, "dedicated.yaml");
    public string LogsDir => Path.Combine(EamDir, "logs");
    /// <summary>Per-server database (chat, players seen...). Never shared between servers.</summary>
    public string DatabaseFile => Path.Combine(EamDir, "server.db");
    public string ModDir => Path.Combine(GameDir, "Content", "Mods", "EAM");

    /// <summary>Every port this server uses (game block + telnet + EAM mod).</summary>
    public IEnumerable<int> AllPorts => Enumerable.Range(GamePort, PortAllocator.GamePortCount).Append(TelnetPort).Append(ModPort);
}
