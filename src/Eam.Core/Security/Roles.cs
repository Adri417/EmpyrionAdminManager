namespace Eam.Core.Security;

/// <summary>
/// Access levels, from most to least powerful. Each level can do everything the levels below it can.
/// </summary>
public static class Roles
{
    /// <summary>Everything, including accounts, EAM settings and installing servers.</summary>
    public const string Owner = "owner";
    /// <summary>Runs the servers: start/stop, backups, configuration.</summary>
    public const string Admin = "admin";
    /// <summary>Players, structures, chat, warps.</summary>
    public const string GameMaster = "gamemaster";
    /// <summary>Chat, kick, temporary bans.</summary>
    public const string Moderator = "moderator";
    /// <summary>Sees everything, changes nothing.</summary>
    public const string Viewer = "viewer";

    public static readonly string[] All = [Owner, Admin, GameMaster, Moderator, Viewer];

    public static bool IsValid(string? role) => role is not null && Array.IndexOf(All, role) >= 0;

    /// <summary>Lower number = more power.</summary>
    public static int Rank(string role) => Array.IndexOf(All, role) is var i and >= 0 ? i : int.MaxValue;

    public static bool AtLeast(string role, string required) => Rank(role) <= Rank(required);
}
