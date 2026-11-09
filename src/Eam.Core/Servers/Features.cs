using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Eam.Core.Servers;

/// <summary>Settings of one feature, stored as JSON in the server's own database.</summary>
public interface IFeatureSettings
{
    /// <summary>Throws <see cref="FormatException"/> with an error code when a value is not acceptable.</summary>
    void Validate();
}

public static class FeatureStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static T Load<T>(ServerData data, string key) where T : IFeatureSettings, new()
    {
        var raw = data.Setting("feature." + key);
        if (string.IsNullOrEmpty(raw)) return new T();
        try { return JsonSerializer.Deserialize<T>(raw, Json) ?? new T(); }
        catch (JsonException) { return new T(); }
    }

    public static void Save<T>(ServerData data, string key, T value) where T : IFeatureSettings
    {
        value.Validate();
        data.SetSetting("feature." + key, JsonSerializer.Serialize(value, Json));
    }

    internal static void Need(bool ok, string code) { if (!ok) throw new FormatException(code); }

    /// <summary>"Akua, Omicron*" style list: names or prefixes ending with *; empty = every playfield.</summary>
    public static bool MatchesPlayfield(string patterns, string playfield)
    {
        if (string.IsNullOrWhiteSpace(patterns)) return true;
        foreach (var p in patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (p.EndsWith('*') ? playfield.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase) : string.Equals(p, playfield, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

/// <summary>Empyrion entity types used for structures.</summary>
public static class EntityTypes
{
    public const int BA = 2, CV = 3, SV = 4, HV = 5;
    public static string Name(int type) => type switch { 2 => "BA", 3 => "CV", 4 => "SV", 5 => "HV", _ => "#" + type };
    public static int Parse(string? name) => (name ?? "").ToUpperInvariant() switch { "BA" => 2, "CV" => 3, "SV" => 4, "HV" => 5, _ => 0 };
    public static bool IsStructure(int type) => type is BA or CV or SV or HV;
}

/// <summary>Owner groups of a structure (GlobalStructureInfo.factionGroup).</summary>
public static class FactionGroups
{
    public const int Faction = 0, Player = 1, Zirax = 2, Predator = 3, Prey = 4, Admin = 5, Talon = 6, Polaris = 7, Alien = 8, NoFaction = 255;
    /// <summary>Owned by a player or a player faction (not the game's NPC factions).</summary>
    public static bool IsPlayerOwned(int group, int id) => group is Faction or Player && id > 0;
}

// =========================================================================== step 6

public sealed class LimitRule
{
    /// <summary>"player" or "faction".</summary>
    public string Scope { get; set; } = "player";
    public string Playfields { get; set; } = "";
    /// <summary>BA, CV, SV, HV or "any".</summary>
    public string Type { get; set; } = "any";
    public int MaxCount { get; set; }
    public int MaxClass { get; set; }
    public int MaxBlocks { get; set; }
}

public sealed class AutoWipeRule
{
    public string Playfields { get; set; } = "";
    public string Type { get; set; } = "any";
    /// <summary>Removed when nobody visited it for this many days (0 = not used).</summary>
    public int NotVisitedDays { get; set; }
    /// <summary>Removed when it exists for more than this many hours (starter planets; 0 = not used).</summary>
    public int OlderThanHours { get; set; }
    /// <summary>Removed when it stays on the same playfield for more than this many hours (0 = not used).</summary>
    public int OnPlayfieldHours { get; set; }
}

public sealed class ReservedPlayfield
{
    public string Playfield { get; set; } = "";
    public List<int> Factions { get; set; } = [];
    public List<string> SteamIds { get; set; } = [];
    public List<int> Origins { get; set; } = [];
    /// <summary>"teleport" (to the target below) or "kick".</summary>
    public string Action { get; set; } = "teleport";
    public string TargetPlayfield { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; } = 100;
    public double Z { get; set; }
}

public sealed class WorldSettings : IFeatureSettings
{
    public int RefreshMinutes { get; set; } = 3;
    public bool LimitsEnabled { get; set; }
    public List<LimitRule> Limits { get; set; } = [];
    /// <summary>"warn" (list and warn the owner) or "destroy" (after the grace period, saved first in the structure library).</summary>
    public string LimitAction { get; set; } = "warn";
    public int LimitGraceHours { get; set; } = 24;
    public bool ExemptAdmins { get; set; } = true;
    public List<int> ExemptFactions { get; set; } = [];
    public bool AutoWipeEnabled { get; set; }
    public List<AutoWipeRule> AutoWipe { get; set; } = [];
    public bool ReservedEnabled { get; set; }
    public List<ReservedPlayfield> Reserved { get; set; } = [];
    /// <summary>Shared folders newer than this are never treated as obsolete.</summary>
    public int SharedMinAgeHours { get; set; } = 48;
    /// <summary>"sanction": structures over a limit are given to this faction (abbreviation) after the grace period.</summary>
    public string SanctionFaction { get; set; } = "";
    /// <summary>Removed this many hours after being given to the sanction faction (0 = kept).</summary>
    public int SanctionDestroyHours { get; set; }
    /// <summary>Alert when the whole server has more structures than this (0 = not checked; the game slows down beyond a few hundred).</summary>
    public int TotalMaxStructures { get; set; }
    /// <summary>Player structures nobody visited for this many hours are given to <see cref="PublicFaction"/> (0 = never).</summary>
    public int UntouchedPublicHours { get; set; }
    public string PublicFaction { get; set; } = "";

    public void Validate()
    {
        FeatureStore.Need(RefreshMinutes is >= 1 and <= 60, "invalid_refresh");
        FeatureStore.Need(LimitAction is "warn" or "destroy" or "sanction", "invalid_limit_action");
        FeatureStore.Need(LimitAction != "sanction" || Regex.IsMatch(SanctionFaction.Trim(), "^[^'\\s]{1,12}$"), "invalid_faction");
        FeatureStore.Need(UntouchedPublicHours == 0 || Regex.IsMatch(PublicFaction.Trim(), "^[^'\\s]{1,12}$"), "invalid_faction");
        FeatureStore.Need(SanctionDestroyHours >= 0 && TotalMaxStructures >= 0 && UntouchedPublicHours >= 0, "invalid_value");
        FeatureStore.Need(LimitGraceHours is >= 0 and <= 24 * 30, "invalid_grace");
        FeatureStore.Need(SharedMinAgeHours is >= 1 and <= 24 * 365, "invalid_age");
        foreach (var l in Limits)
        {
            FeatureStore.Need(l.Scope is "player" or "faction", "invalid_limit");
            FeatureStore.Need(l.Type == "any" || EntityTypes.Parse(l.Type) > 0, "invalid_limit");
            FeatureStore.Need(l.MaxCount >= 0 && l.MaxClass >= 0 && l.MaxBlocks >= 0 && (l.MaxCount + l.MaxClass + l.MaxBlocks) > 0, "invalid_limit");
        }
        foreach (var w in AutoWipe)
        {
            FeatureStore.Need(w.Type == "any" || EntityTypes.Parse(w.Type) > 0, "invalid_autowipe");
            FeatureStore.Need(w.NotVisitedDays >= 0 && w.OlderThanHours >= 0 && w.OnPlayfieldHours >= 0 && w.NotVisitedDays + w.OlderThanHours + w.OnPlayfieldHours > 0, "invalid_autowipe");
        }
        foreach (var r in Reserved)
        {
            FeatureStore.Need(r.Playfield.Trim().Length > 0, "invalid_reserved");
            FeatureStore.Need(r.Action is "kick" or "teleport", "invalid_reserved");
            FeatureStore.Need(r.Action == "kick" || r.TargetPlayfield.Trim().Length > 0, "invalid_reserved");
            FeatureStore.Need(r.SteamIds.All(s => Regex.IsMatch(s, "^[0-9]{17}$")), "invalid_steam_id");
        }
    }
}

// =========================================================================== step 7

public sealed class CustomCommand
{
    public string Command { get; set; } = "";
    public string Description { get; set; } = "";
    public string Response { get; set; } = "";
}

public sealed class KitItem
{
    public int Id { get; set; }
    public int Count { get; set; } = 1;
}

public sealed class Announcement
{
    public string Text { get; set; } = "";
}

public sealed class PlayfieldMessage
{
    public string Playfield { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class FactionChannel
{
    public int FactionId { get; set; }
    public string ChannelId { get; set; } = "";
}

public sealed class CommunitySettings : IFeatureSettings
{
    public bool ChatbotEnabled { get; set; } = true;
    public string Prefix { get; set; } = "CB:";
    /// <summary>Minimum delay between two commands of the same player that change the game.</summary>
    public int CooldownSeconds { get; set; } = 30;
    public List<CustomCommand> Commands { get; set; } = [];
    public bool HealEnabled { get; set; } = true;
    public bool ShipCommandsEnabled { get; set; } = true;
    public bool AdminCommandsEnabled { get; set; } = true;
    public bool SurvivalKitEnabled { get; set; }
    public List<KitItem> SurvivalKit { get; set; } = [];
    public int SurvivalCredits { get; set; }
    /// <summary>Changing this name lets every player claim the kit once more (for example after a wipe).</summary>
    public string SurvivalSeason { get; set; } = "1";
    /// <summary>CB:reset: the player starts again from zero (new character). Free resets during the first hours, then one every N hours.</summary>
    public bool ResetEnabled { get; set; }
    public int ResetFreeCount { get; set; } = 3;
    public int ResetFreeHours { get; set; } = 10;
    public int ResetEveryHours { get; set; } = 6;

    public List<string> AlertWords { get; set; } = ["admin", "help", "aide", "bug", "cheat"];

    public bool FilterEnabled { get; set; }
    public List<string> FilterWords { get; set; } = [];
    public bool FilterGlobalOnly { get; set; } = true;
    public int FilterWarnings { get; set; } = 3;
    public int FilterFirstBanMinutes { get; set; } = 60;
    /// <summary>Muted players who keep writing are kicked after this many messages (0 = never).</summary>
    public int MutedKickAfter { get; set; } = 3;

    public string Motd { get; set; } = "";
    public string Welcome { get; set; } = "";
    /// <summary>"X joined the server" / "X left the server" in the global chat.</summary>
    public bool JoinLeaveMessages { get; set; }
    public string JoinMessage { get; set; } = "{name} joined the server";
    public string LeaveMessage { get; set; } = "{name} left the server";
    /// <summary>Messages written from EAM show the EAM user's name in game ("Adrien: hello").</summary>
    public bool ShowAdminName { get; set; } = true;
    public List<PlayfieldMessage> PlayfieldMessages { get; set; } = [];

    /// <summary>Automatic announcements: one message of the list every N minutes, in turn.</summary>
    public bool AnnouncementsEnabled { get; set; }
    public int AnnouncementMinutes { get; set; } = 15;
    /// <summary>"chat", "popup" or "alert".</summary>
    public string AnnouncementMode { get; set; } = "chat";
    public bool AnnouncementsOnlyWithPlayers { get; set; } = true;
    public List<Announcement> Announcements { get; set; } = [];

    public string TranslateTo { get; set; } = "fr";

    public bool DiscordEnabled { get; set; }
    /// <summary>Bot token: never sent back to the interface.</summary>
    public string DiscordToken { get; set; } = "";
    public string DiscordPublicChannel { get; set; } = "";
    public string DiscordAdminChannel { get; set; } = "";
    public List<FactionChannel> DiscordFactionChannels { get; set; } = [];

    public void Validate()
    {
        FeatureStore.Need(Prefix.Trim().Length is >= 1 and <= 8 && !Prefix.Contains(' '), "invalid_prefix");
        FeatureStore.Need(CooldownSeconds is >= 0 and <= 3600, "invalid_cooldown");
        FeatureStore.Need(Commands.All(c => Regex.IsMatch(c.Command, @"^[A-Za-z0-9_\-?!]{1,24}$") && c.Response.Length is > 0 and <= 500), "invalid_command");
        FeatureStore.Need(SurvivalKit.All(k => k.Id > 0 && k.Count is > 0 and <= 100000) && SurvivalCredits is >= 0 and <= 100_000_000, "invalid_kit");
        FeatureStore.Need(FilterWarnings is >= 0 and <= 20 && FilterFirstBanMinutes is >= 1 and <= 60 * 24 * 30 && MutedKickAfter is >= 0 and <= 100, "invalid_filter");
        FeatureStore.Need(JoinMessage.Length <= 200 && LeaveMessage.Length <= 200, "invalid_message");
        FeatureStore.Need(AnnouncementMinutes is >= 1 and <= 1440 && AnnouncementMode is "chat" or "popup" or "alert"
            && Announcements.All(a => a.Text.Trim().Length is > 0 and <= 300), "invalid_announcement");
        FeatureStore.Need(Motd.Length <= 500 && Welcome.Length <= 500 && PlayfieldMessages.All(p => p.Playfield.Length > 0 && p.Text.Length is > 0 and <= 300), "invalid_message");
        FeatureStore.Need(Regex.IsMatch(TranslateTo, "^[a-z]{2}(-[A-Za-z]{2})?$"), "invalid_language");
        bool Snowflake(string s) => s.Length == 0 || Regex.IsMatch(s, "^[0-9]{15,22}$");
        FeatureStore.Need(Snowflake(DiscordPublicChannel) && Snowflake(DiscordAdminChannel) && DiscordFactionChannels.All(f => f.ChannelId.Length > 0 && Snowflake(f.ChannelId)), "invalid_discord_channel");
        FeatureStore.Need(!DiscordEnabled || DiscordToken.Length > 20, "discord_token_missing");
    }
}

// =========================================================================== step 8

public sealed class CheatRule
{
    public int ItemId { get; set; }
    /// <summary>Quantity above which the item is suspicious (0 = any quantity).</summary>
    public int Max { get; set; }
    /// <summary>1 maybe, 2 high, 3 extreme.</summary>
    public int Level { get; set; } = 2;
}

public sealed class SecuritySettings : IFeatureSettings
{
    /// <summary>Inventory history: a copy of every online player's inventory when it changes, and at death (to give it back).</summary>
    public bool InventoryHistoryEnabled { get; set; } = true;
    public int InventoryMinutes { get; set; } = 2;
    public int InventoryDays { get; set; } = 14;

    /// <summary>Playfields left out of statistics and position history (instances, event arenas…), comma separated, * allowed.</summary>
    public string ExcludedPlayfields { get; set; } = "";

    public bool AntiCheatEnabled { get; set; }
    public int ScanMinutes { get; set; } = 5;
    public List<CheatRule> CheatRules { get; set; } = [];
    /// <summary>A stack bigger than this is impossible (0 = not checked).</summary>
    public int MaxStack { get; set; }
    public List<int> Whitelist { get; set; } = [];
    /// <summary>Credits gained between two scans that look suspicious (0 = not checked).</summary>
    public double CreditsJump { get; set; }
    public bool AutoBanExtreme { get; set; }
    public string AutoBanDuration { get; set; } = "30d";
    public bool ExemptAdmins { get; set; } = true;

    public bool AttackLogEnabled { get; set; } = true;
    public int AttackRadius { get; set; } = 300;
    public bool AttackWarnOwners { get; set; }

    public bool PolesEnabled { get; set; }
    public string PolePlayfields { get; set; } = "";
    /// <summary>Distance from the centre (z) where the pole zone starts.</summary>
    public int PoleLimit { get; set; } = 1800;
    public int PoleAlertSeconds { get; set; } = 120;
    public int PoleMoveSeconds { get; set; } = 180;

    public bool EmergencyEnabled { get; set; } = true;
    public int EmergencyFailures { get; set; } = 3;
    public int EmergencyWindowMinutes { get; set; } = 30;
    /// <summary>Empty = the player's start playfield.</summary>
    public string EmergencyPlayfield { get; set; } = "";
    public double EmergencyX { get; set; }
    public double EmergencyY { get; set; } = 150;
    public double EmergencyZ { get; set; }

    public bool FloodEnabled { get; set; } = true;
    public string FloodText { get; set; } = "Cannot send package to client";
    public int FloodPerMinute { get; set; } = 100;

    public int PositionSeconds { get; set; } = 60;

    public void Validate()
    {
        FeatureStore.Need(ScanMinutes is >= 1 and <= 120, "invalid_scan");
        FeatureStore.Need(CheatRules.All(r => r.ItemId > 0 && r.Max >= 0 && r.Level is >= 1 and <= 3), "invalid_cheat_rule");
        FeatureStore.Need(MaxStack >= 0 && CreditsJump >= 0, "invalid_value");
        FeatureStore.Need(AttackRadius is >= 20 and <= 5000, "invalid_radius");
        FeatureStore.Need(PoleLimit is >= 100 and <= 100000 && PoleAlertSeconds is >= 10 && PoleMoveSeconds > PoleAlertSeconds, "invalid_poles");
        FeatureStore.Need(EmergencyFailures is >= 2 and <= 20 && EmergencyWindowMinutes is >= 5 and <= 24 * 60, "invalid_emergency");
        FeatureStore.Need(FloodText.Trim().Length >= 5 && FloodPerMinute is >= 5 and <= 100000, "invalid_flood");
        FeatureStore.Need(PositionSeconds is >= 15 and <= 600 && InventoryMinutes is >= 1 and <= 60 && InventoryDays is >= 1 and <= 365, "invalid_value");
        FeatureStore.Need(Regex.IsMatch(AutoBanDuration, "^[0-9]{1,4}[mhdy]$"), "invalid_duration");
    }
}

// =========================================================================== step 9

public sealed class OreConfig
{
    public string Key { get; set; } = "";
    public int ItemId { get; set; }
    /// <summary>Units per hour at level 1 (level N mines N times this).</summary>
    public double PerHour { get; set; } = 10;
    /// <summary>Price of level N = Price × N.</summary>
    public int Price { get; set; } = 10000;
}

public sealed class GameplaySettings : IFeatureSettings
{
    public bool AutominerEnabled { get; set; }
    public string AutominerPrefix { get; set; } = "am:";
    public int AutominerMaxLevel { get; set; } = 10;
    public int FuelPricePerHour { get; set; } = 100;
    public int MaxStoredHours { get; set; } = 48;
    public List<OreConfig> Ores { get; set; } = [];

    public bool EventsEnabled { get; set; } = true;
    public string EventsPrefix { get; set; } = "EV:";

    /// <summary>New Empyrion build on Steam: "off", "notify" (badge + journal) or "auto" (countdown, backup, update, restart).</summary>
    public string GameUpdateMode { get; set; } = "notify";
    public bool GameUpdateBackupFirst { get; set; } = true;

    public static readonly string[] DefaultOres =
        ["IronOre", "CopperOre", "SiliconOre", "CobaltOre", "MagnesiumOre", "NeodymiumOre", "PromethiumOre", "ErestrumOre",
         "ZascosiumOre", "SathiumOre", "GoldOre", "TitanOre", "PentaxidOre"];

    public void Validate()
    {
        FeatureStore.Need(AutominerPrefix.Trim().Length is >= 1 and <= 8 && EventsPrefix.Trim().Length is >= 1 and <= 8, "invalid_prefix");
        FeatureStore.Need(GameUpdateMode is "off" or "notify" or "auto", "invalid_value");
        FeatureStore.Need(AutominerMaxLevel is >= 1 and <= 50 && FuelPricePerHour >= 0 && MaxStoredHours is >= 1 and <= 24 * 30, "invalid_value");
        FeatureStore.Need(Ores.All(o => Regex.IsMatch(o.Key, "^[A-Za-z0-9_]{1,24}$") && o.ItemId > 0 && o.PerHour > 0 && o.Price >= 0), "invalid_ore");
        FeatureStore.Need(Ores.Select(o => o.Key.ToLowerInvariant()).Distinct().Count() == Ores.Count, "invalid_ore");
    }
}
