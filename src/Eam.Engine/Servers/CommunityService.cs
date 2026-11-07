using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Eam.Core.Servers;

namespace Eam.Engine.Servers;

/// <summary>
/// Step 7: the players' chatbot (CB:), alerts on keywords, insult filter with growing bans, mute,
/// welcome message, message of the day, per-playfield messages, and the log of console commands typed in game.
/// </summary>
public sealed partial class CommunityService
{
    private readonly ServerManager _manager;
    private readonly LiveService _live;
    private readonly SchedulerService _scheduler;
    private readonly WorldService _world;
    private readonly Alerts _alerts;
    private readonly BackupService _backups;
    private readonly ILogger<CommunityService> _log;

    public CommunityService(ServerManager manager, LiveService live, SchedulerService scheduler, WorldService world, Alerts alerts, BackupService backups, ILogger<CommunityService> log)
    {
        _manager = manager;
        _live = live;
        _scheduler = scheduler;
        _world = world;
        _alerts = alerts;
        _backups = backups;
        _log = log;
        _live.ChatReceived += OnChatAsync;
        _live.PlayerJoined += OnJoinAsync;
        _live.ModEvent += OnModEventAsync;
    }

    /// <summary>Other features answering their own prefix (autominer, events): return true when handled.</summary>
    public List<Func<ServerDefinition, LivePlayer, string, Task<bool>>> CommandHandlers { get; } = [];

    public CommunitySettings Settings(ServerDefinition def) => FeatureStore.Load<CommunitySettings>(_live.Data(def), "community");

    public Task ReplyAsync(ServerDefinition def, LivePlayer p, string text) =>
        _live.SendToModAsync(def.Id, new { op = "msg_player", id = p.Id, text = text.Length > 500 ? text[..500] : text, prio = 1, secs = 12 });

    // ------------------------------------------------------------------ chat

    private async Task OnChatAsync(ServerDefinition def, ChatEntry entry, LivePlayer? sender)
    {
        if (sender?.SteamId is not { Length: > 0 } steam) return;
        var s = Settings(def);
        var data = _live.Data(def);
        var text = entry.Text.Trim();

        // Mute: EAM cannot stop the game from showing the message, so the player is warned, then kicked.
        if (data.Counter(steam, "mute") is { } mute && DateTime.TryParse(mute.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until))
        {
            if (until > DateTime.UtcNow)
            {
                var n = mute.Count + 1;
                data.SetCounter(steam, "mute", n, mute.Value);
                if (s.MutedKickAfter > 0 && n >= s.MutedKickAfter)
                {
                    await _manager.ConsoleAsync(def.Id, $"kick {sender.Id} 'Muted'", "EAM");
                    data.SetCounter(steam, "mute", 0, mute.Value);
                    await _alerts.RaiseAsync(def, "mute", 1, "kicked: muted player kept writing", steam, sender.Name, sender.Playfield);
                }
                else await ReplyAsync(def, sender, $"EAM: vous êtes muet jusqu'à {until.ToLocalTime():HH:mm} / you are muted.");
                return;
            }
            data.DeleteCounter(steam, "mute");
        }

        if (s.FilterEnabled && s.FilterWords.Count > 0 && (!s.FilterGlobalOnly || entry.Channel == "Global") && FindBadWord(text, s.FilterWords) is { } bad)
        {
            await FilterAsync(def, s, sender, steam, bad);
            return;
        }

        var lower = text.ToLowerInvariant();
        var alert = s.AlertWords.FirstOrDefault(w => w.Length > 0 && Regex.IsMatch(lower, $@"(^|\W){Regex.Escape(w.ToLowerInvariant())}(\W|$)"));
        if (alert is not null) await _alerts.RaiseAsync(def, "alert", 1, $"« {text} »", steam, sender.Name, sender.Playfield);

        foreach (var h in CommandHandlers)
            if (await h(def, sender, text)) return;

        if (!s.ChatbotEnabled) return;
        if (text.Equals("!mods", StringComparison.OrdinalIgnoreCase)) { await ReplyAsync(def, sender, ModsText(def)); return; }
        if (!text.StartsWith(s.Prefix, StringComparison.OrdinalIgnoreCase)) return;
        var command = text[s.Prefix.Length..].Trim();
        try { await CommandAsync(def, s, sender, steam, command); }
        catch (ServerException e) { await ReplyAsync(def, sender, "EAM: " + Explain(e.Code)); }
        catch (Exception e) { _log.LogWarning("Chatbot command '{Cmd}' failed: {Err}", command, e.Message); }
    }

    private static string Explain(string code) => code switch
    {
        "cooldown" => "attendez un peu avant de recommencer / please wait",
        "not_yours" => "cette structure ne vous appartient pas / not your structure",
        "structure_not_found" => "structure inconnue / unknown structure",
        "player_offline" => "joueur non connecté / player not online",
        "already_claimed" => "déjà utilisé / already used",
        "forbidden" => "réservé aux admins / admins only",
        "disabled" => "commande désactivée / command disabled",
        _ => code,
    };

    internal static string? FindBadWord(string text, IEnumerable<string> words)
    {
        var norm = Plain(text);
        foreach (var w in words)
        {
            var nw = Plain(w.Trim());
            if (nw.Length == 0) continue;
            if (Regex.IsMatch(norm, $@"(^|[^a-z0-9]){Regex.Escape(nw)}([^a-z0-9]|$)")) return w;
        }
        return null;
    }

    /// <summary>Lower case without accents, so "Connard", "CONNARD" and "cönnard" match the same word.</summary>
    internal static string Plain(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.ToLowerInvariant())
            sb.Append(c switch
            {
                'à' or 'â' or 'ä' or 'á' or 'ã' => 'a', 'é' or 'è' or 'ê' or 'ë' => 'e', 'î' or 'ï' or 'í' or 'ì' => 'i',
                'ô' or 'ö' or 'ó' or 'ò' or 'õ' => 'o', 'û' or 'ü' or 'ú' or 'ù' => 'u', 'ç' => 'c', 'ñ' => 'n', 'ß' => 's',
                '0' => 'o', '1' => 'i', '3' => 'e', '4' => 'a', '@' => 'a', '$' => 's',
                _ => c,
            });
        return sb.ToString();
    }

    private async Task FilterAsync(ServerDefinition def, CommunitySettings s, LivePlayer p, string steam, string word)
    {
        var data = _live.Data(def);
        var warnings = (data.Counter(steam, "filter")?.Count ?? 0) + 1;
        if (warnings <= s.FilterWarnings)
        {
            data.SetCounter(steam, "filter", warnings);
            await ReplyAsync(def, p, $"EAM: langage interdit, avertissement {warnings}/{s.FilterWarnings} / forbidden language, warning {warnings}/{s.FilterWarnings}");
            await _alerts.RaiseAsync(def, "filter", 0, $"warning {warnings}/{s.FilterWarnings} ({word})", steam, p.Name, p.Playfield);
            return;
        }
        if (data.Meta(steam).BanExempt)
        {
            await _alerts.RaiseAsync(def, "filter", 1, $"{warnings} warnings ({word}), not banned: exempt", steam, p.Name, p.Playfield);
            return;
        }
        // Each new ban lasts twice as long as the previous one.
        var bans = data.Counter(steam, "filter_bans")?.Count ?? 0;
        var minutes = (int)Math.Min(s.FilterFirstBanMinutes * Math.Pow(2, bans), 60 * 24 * 365);
        data.SetCounter(steam, "filter_bans", bans + 1);
        data.SetCounter(steam, "filter", 0);
        await ReplyAsync(def, p, $"EAM: banni {minutes} min / banned {minutes} min");
        await Task.Delay(TimeSpan.FromSeconds(3));
        await _manager.ConsoleAsync(def.Id, $"ban {steam} {minutes}m", "EAM");
        data.PlayerEvent(steam, p.Name, "admin_ban", $"{minutes}m (insult filter)", "EAM");
        await _alerts.RaiseAsync(def, "filter", 2, $"banned {minutes} min after {s.FilterWarnings} warnings ({word})", steam, p.Name, p.Playfield);
    }

    // ------------------------------------------------------------------ chatbot commands

    private bool CoolingDown(ServerData data, string steam, CommunitySettings s)
    {
        var c = data.Counter(steam, "cb_cooldown");
        if (c is not null && DateTime.UtcNow - c.LastUtc < TimeSpan.FromSeconds(s.CooldownSeconds)) return true;
        data.SetCounter(steam, "cb_cooldown", (c?.Count ?? 0) + 1);
        return false;
    }

    private async Task CommandAsync(ServerDefinition def, CommunitySettings s, LivePlayer p, string steam, string command)
    {
        var data = _live.Data(def);
        var parts = command.Split([':', ' '], 3, StringSplitOptions.TrimEntries);
        var name = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : "";
        var rest = parts.Length > 2 ? parts[2] : "";
        data.AddJournal("chatbot", 0, s.Prefix + command, steam, p.Name, p.Playfield);

        var custom = s.Commands.FirstOrDefault(c => c.Command.Equals(name, StringComparison.OrdinalIgnoreCase) || c.Command.Equals(command, StringComparison.OrdinalIgnoreCase));
        if (custom is not null) { await ReplyAsync(def, p, Fill(custom.Response, p)); return; }

        switch (name)
        {
            case "?":
            case "help":
            case "aide":
            {
                var lines = new List<string> { $"{s.Prefix}info", $"{s.Prefix}time", $"{s.Prefix}nextrestart", $"{s.Prefix}nextwipe", $"{s.Prefix}w:<nom>:<message>", "!mods" };
                if (s.HealEnabled) lines.Add($"{s.Prefix}heal");
                if (s.SurvivalKitEnabled) lines.Add($"{s.Prefix}survival");
                if (s.ShipCommandsEnabled) lines.AddRange([$"{s.Prefix}getshiphere:<id>", $"{s.Prefix}getshipdown:<id>", $"{s.Prefix}gotoship:<id>", $"{s.Prefix}destroy:<id>"]);
                lines.Add($"{s.Prefix}wipe");
                if (s.ResetEnabled) lines.Add($"{s.Prefix}reset");
                lines.AddRange(s.Commands.Select(c => $"{s.Prefix}{c.Command} — {c.Description}"));
                await ReplyAsync(def, p, string.Join("\n", lines));
                break;
            }
            case "info":
            {
                var seen = data.Players().FirstOrDefault(x => x.SteamId == steam);
                await ReplyAsync(def, p, seen is null ? p.Name
                    : $"{p.Name}\nPremière connexion / first login: {seen.FirstSeenUtc.ToLocalTime():dd/MM/yyyy}\nTemps de jeu / play time: {seen.PlaytimeSeconds / 3600} h {seen.PlaytimeSeconds % 3600 / 60} min");
                break;
            }
            case "time":
                await ReplyAsync(def, p, $"{DateTime.Now:dd/MM/yyyy HH:mm}");
                break;
            case "nextrestart":
            case "nextwipe":
            {
                var next = _scheduler.NextRun(def.Id, name == "nextrestart" ? "restart" : "wipe");
                await ReplyAsync(def, p, next is { } n ? $"{n:dd/MM HH:mm} (dans / in {Math.Max(0, (int)(n - DateTime.Now).TotalMinutes)} min)" : "—");
                break;
            }
            case "w":
            {
                var target = _live.OnlinePlayers(def.Id).FirstOrDefault(x => x.Name.Equals(arg, StringComparison.OrdinalIgnoreCase))
                             ?? _live.OnlinePlayers(def.Id).FirstOrDefault(x => x.Name.StartsWith(arg, StringComparison.OrdinalIgnoreCase));
                if (target is null || rest.Length == 0) throw new ServerException("player_offline");
                await _live.SendToModAsync(def.Id, new { op = "msg_player", id = target.Id, text = $"[{p.Name}] {rest}", prio = 1, secs = 15 });
                await ReplyAsync(def, p, $"→ {target.Name}");
                break;
            }
            case "heal":
            {
                if (!s.HealEnabled) throw new ServerException("disabled");
                var last = data.Counter(steam, "cb_heal");
                if (last is not null && last.LastUtc.ToLocalTime().Date == DateTime.Now.Date) throw new ServerException("already_claimed");
                await _live.RequestAsync(def.Id, new() { ["op"] = "player_set", ["id"] = p.Id, ["health"] = Math.Max(1, (int)p.HealthMax), ["food"] = 10000, ["oxygen"] = 10000, ["stamina"] = 10000, ["radiation"] = 0 });
                data.SetCounter(steam, "cb_heal", (last?.Count ?? 0) + 1);
                await ReplyAsync(def, p, "EAM: soigné / healed");
                break;
            }
            case "survival":
            {
                if (!s.SurvivalKitEnabled) throw new ServerException("disabled");
                var claimed = data.Counter(steam, "cb_survival");
                if (claimed?.Value == s.SurvivalSeason) throw new ServerException("already_claimed");
                if (CoolingDown(data, steam, s)) throw new ServerException("cooldown");
                foreach (var k in s.SurvivalKit) await _live.RequestAsync(def.Id, new() { ["op"] = "item_add", ["id"] = p.Id, ["item"] = k.Id, ["count"] = k.Count });
                if (s.SurvivalCredits > 0) await _live.RequestAsync(def.Id, new() { ["op"] = "credits_add", ["id"] = p.Id, ["credits"] = s.SurvivalCredits });
                data.SetCounter(steam, "cb_survival", (claimed?.Count ?? 0) + 1, s.SurvivalSeason);
                data.PlayerEvent(steam, p.Name, "survival_kit", s.SurvivalSeason, "chatbot");
                await ReplyAsync(def, p, "EAM: kit de survie reçu / survival kit received");
                break;
            }
            case "getshiphere":
            case "gotoship":
            case "destroy":
            {
                if (!s.ShipCommandsEnabled) throw new ServerException("disabled");
                if (!int.TryParse(arg, out var sid)) throw new ServerException("structure_not_found");
                var st = data.Structure(sid);
                if (st is null || st.RemovedUtc is not null) throw new ServerException("structure_not_found");
                var mine = st.FactionGroup == FactionGroups.Player && st.FactionId == p.Id || st.FactionGroup == FactionGroups.Faction && st.FactionId == p.FactionId && p.FactionId > 0;
                if (!mine) throw new ServerException("not_yours");
                if (name == "destroy")
                {
                    // Asked twice within a minute, to avoid mistakes.
                    var pending = data.Counter(steam, "cb_destroy");
                    if (pending?.Value != arg || DateTime.UtcNow - pending.LastUtc > TimeSpan.FromMinutes(1))
                    {
                        data.SetCounter(steam, "cb_destroy", 1, arg);
                        await ReplyAsync(def, p, $"EAM: retapez {s.Prefix}destroy:{sid} dans la minute pour supprimer « {st.Name} » / type it again within a minute to confirm");
                        return;
                    }
                    data.DeleteCounter(steam, "cb_destroy");
                    if (CoolingDown(data, steam, s)) throw new ServerException("cooldown");
                    await _world.ExportAsync(def, st, p.Name, "CB:destroy");
                    await _live.RequestAsync(def.Id, new() { ["op"] = "destroy", ["id"] = sid }, TimeSpan.FromSeconds(30));
                    data.StructureEventBy(sid, "player_destroy", null, "chatbot", p.Name);
                    await ReplyAsync(def, p, $"EAM: « {st.Name} » supprimée / removed");
                }
                else if (name == "getshiphere")
                {
                    if (st.Type == EntityTypes.BA) throw new ServerException("not_yours");
                    if (!string.Equals(st.Playfield, p.Playfield, StringComparison.OrdinalIgnoreCase)) throw new ServerException("structure_not_found");
                    if (CoolingDown(data, steam, s)) throw new ServerException("cooldown");
                    await _live.RequestAsync(def.Id, new() { ["op"] = "entity_move", ["id"] = sid, ["x"] = p.X + 25, ["y"] = p.Y + 15, ["z"] = p.Z + 25 }, TimeSpan.FromSeconds(30));
                    data.StructureEventBy(sid, "player_getshiphere", null, $"{p.X:0}, {p.Y:0}, {p.Z:0}", p.Name);
                    await ReplyAsync(def, p, $"EAM: « {st.Name} » arrive / is coming");
                }
                else
                {
                    if (CoolingDown(data, steam, s)) throw new ServerException("cooldown");
                    var req = new Dictionary<string, object?> { ["op"] = "teleport", ["id"] = p.Id, ["x"] = st.X + 10, ["y"] = st.Y + 15, ["z"] = st.Z + 10 };
                    if (!string.Equals(st.Playfield, p.Playfield, StringComparison.OrdinalIgnoreCase)) req["pf"] = st.Playfield;
                    await _live.RequestAsync(def.Id, req, TimeSpan.FromSeconds(60));
                    data.PlayerEvent(steam, p.Name, "chatbot_gotoship", $"{sid} {st.Name}", "chatbot");
                }
                break;
            }
            case "getshipdown":
            {
                if (!s.ShipCommandsEnabled) throw new ServerException("disabled");
                if (!int.TryParse(arg, out var sid)) throw new ServerException("structure_not_found");
                var st = data.Structure(sid);
                if (st is null || st.RemovedUtc is not null || st.Type == EntityTypes.BA) throw new ServerException("structure_not_found");
                var mine = st.FactionGroup == FactionGroups.Player && st.FactionId == p.Id || st.FactionGroup == FactionGroups.Faction && st.FactionId == p.FactionId && p.FactionId > 0;
                if (!mine) throw new ServerException("not_yours");
                if (!string.Equals(st.Playfield, p.Playfield, StringComparison.OrdinalIgnoreCase)) throw new ServerException("structure_not_found");
                if (CoolingDown(data, steam, s)) throw new ServerException("cooldown");
                // Stuck in the sky or under the ground: brought down next to the player, at their height.
                await _live.RequestAsync(def.Id, new() { ["op"] = "entity_move", ["id"] = sid, ["x"] = p.X + 20, ["y"] = p.Y + 3, ["z"] = p.Z + 20 }, TimeSpan.FromSeconds(30));
                data.StructureEventBy(sid, "player_getshipdown", null, $"{p.X:0}, {p.Y:0}, {p.Z:0}", p.Name);
                await ReplyAsync(def, p, $"EAM: « {st.Name} » posé près de vous / set down next to you");
                break;
            }
            case "wipe":
            {
                var next = _scheduler.NextRun(def.Id, "wipe");
                var here = data.Tasks().Where(t => t.Enabled && t.Kind == "wipe")
                    .Any(t => t.Params.Contains("\"" + (p.Playfield ?? "") + "\"", StringComparison.OrdinalIgnoreCase) || t.Params.Contains("\"*\""));
                await ReplyAsync(def, p, next is { } n
                    ? $"Prochain wipe / next wipe: {n:dd/MM HH:mm}. {p.Playfield}: {(here ? "concerné / included" : "non concerné / not included")}"
                    : "Aucun wipe prévu / no wipe planned");
                break;
            }
            case "reset":
            {
                if (!s.ResetEnabled) throw new ServerException("disabled");
                var seen = data.Players().FirstOrDefault(x => x.SteamId == steam);
                var used = data.Counter(steam, "cb_reset");
                var free = (seen?.PlaytimeSeconds ?? 0) < s.ResetFreeHours * 3600 && (used?.Count ?? 0) < s.ResetFreeCount;
                if (!free && used is not null && DateTime.UtcNow - used.LastUtc < TimeSpan.FromHours(s.ResetEveryHours)) throw new ServerException("cooldown");
                var pending = data.Counter(steam, "cb_reset_confirm");
                if (pending is null || DateTime.UtcNow - pending.LastUtc > TimeSpan.FromMinutes(1))
                {
                    data.SetCounter(steam, "cb_reset_confirm", 1);
                    await ReplyAsync(def, p, $"EAM: ATTENTION, vous repartirez de zéro (inventaire, niveau, position). Retapez {s.Prefix}reset dans la minute pour confirmer. / You will start again from zero, type it again within a minute to confirm.");
                    return;
                }
                data.DeleteCounter(steam, "cb_reset_confirm");
                data.SetCounter(steam, "cb_reset", (used?.Count ?? 0) + 1);
                data.PlayerEvent(steam, p.Name, "chatbot_reset", "", "chatbot");
                await ReplyAsync(def, p, "EAM: reset dans 10 s, reconnectez-vous ensuite / reset in 10 s, log in again afterwards");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        await _manager.ConsoleAsync(def.Id, $"kick {p.Id} 'CB:reset'", "EAM chatbot");
                        // The game writes the player file when they leave: wait for it, then move it aside.
                        for (var i = 0; i < 30 && _live.OnlineBySteam(def.Id, steam) is not null; i++) await Task.Delay(TimeSpan.FromSeconds(2));
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        var file = PlayerService.PlayerFile(def, steam);
                        if (File.Exists(file))
                        {
                            var aside = Path.Combine(def.EamDir, "players-removed", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-reset");
                            Directory.CreateDirectory(aside);
                            File.Move(file, Path.Combine(aside, Path.GetFileName(file)));
                        }
                    }
                    catch (Exception e) { _log.LogWarning("CB:reset of {Name} failed: {Err}", p.Name, e.Message); }
                });
                break;
            }
            case "backup":
            {
                if (!s.AdminCommandsEnabled) throw new ServerException("disabled");
                if (!IsInGameAdmin(def, p, steam)) throw new ServerException("forbidden");
                await ReplyAsync(def, p, "EAM: sauvegarde en cours / backup started");
                _ = Task.Run(async () =>
                {
                    try { var b = await _backups.CreateAsync(def.Id, "manual", p.Name + " (in game)", "CB:backup"); await ReplyAsync(def, p, "EAM: sauvegarde faite / backup done"); }
                    catch (Exception e) { _log.LogWarning("In-game backup failed: {Err}", e.Message); }
                });
                break;
            }
            case "restart":
            case "shutdown":
            {
                if (!s.AdminCommandsEnabled) throw new ServerException("disabled");
                if (!IsInGameAdmin(def, p, steam)) throw new ServerException("forbidden");
                var minutes = int.TryParse(arg, out var m) ? Math.Clamp(m, 0, 30) : 5;
                await ReplyAsync(def, p, $"EAM: {name} dans / in {minutes} min");
                await _alerts.RaiseAsync(def, "chatbot", 1, $"{name} in {minutes} min asked in game", steam, p.Name, p.Playfield);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (name == "restart") await _manager.RestartAsync(def.Id, minutes, null, p.Name + " (in game)");
                        else await _manager.StopAsync(def.Id, minutes, null, p.Name + " (in game)");
                    }
                    catch (Exception e) { _log.LogWarning("In-game {Cmd} failed: {Err}", name, e.Message); }
                });
                break;
            }
            default:
                await ReplyAsync(def, p, $"EAM: ? → {s.Prefix}?");
                break;
        }
    }

    private static bool IsInGameAdmin(ServerDefinition def, LivePlayer p, string steam) =>
        p.Permission >= 9 || AdminConfig.ReadElevated(AdminConfig.FileOf(def)).Any(e => e.SteamId == steam && e.Permission >= 9);

    private static string Fill(string text, LivePlayer p) =>
        text.Replace("{PLAYERNAME}", p.Name, StringComparison.OrdinalIgnoreCase).Replace("{PLAYFIELD}", p.Playfield ?? "", StringComparison.OrdinalIgnoreCase);

    private static string ModsText(ServerDefinition def)
    {
        var dir = Path.Combine(def.GameDir, "Content", "Mods");
        var mods = Directory.Exists(dir) ? Directory.EnumerateDirectories(dir).Select(Path.GetFileName).Where(n => n is not null).ToList() : [];
        return mods.Count == 0 ? "Mods: —" : "Mods: " + string.Join(", ", mods);
    }

    // ------------------------------------------------------------------ joins, playfields, console commands

    private async Task OnJoinAsync(ServerDefinition def, LivePlayer p)
    {
        var s = Settings(def);
        if (s.Motd.Length == 0 && s.Welcome.Length == 0) return;
        var data = _live.Data(def);
        var first = p.SteamId is { } steam && data.PlayerEvents(steam, 5).Count(e => e.Kind == "join") <= 1;
        await Task.Delay(TimeSpan.FromSeconds(20)); // the player is still loading
        if (first && s.Welcome.Length > 0) await ReplyAsync(def, p, Fill(s.Welcome, p));
        else if (s.Motd.Length > 0) await ReplyAsync(def, p, Fill(s.Motd, p));
    }

    private async Task OnModEventAsync(ServerDefinition def, string ev, JsonElement m)
    {
        if (ev == "player_playfield")
        {
            var s = Settings(def);
            var pf = LiveService.Str(m, "pf") ?? "";
            var msg = s.PlayfieldMessages.FirstOrDefault(x => FeatureStore.MatchesPlayfield(x.Playfield, pf));
            if (msg is null || _live.Online(def.Id, LiveService.Int(m, "id")) is not { } p) return;
            await Task.Delay(TimeSpan.FromSeconds(8));
            await ReplyAsync(def, p, Fill(msg.Text, p));
        }
        else if (ev == "console_cmd")
        {
            var cmd = LiveService.Str(m, "cmd") ?? "";
            var p = _live.Online(def.Id, LiveService.Int(m, "player"));
            var dangerous = Regex.IsMatch(cmd, @"^\s*(ban|kick|wipe|destroy|remoteex|itemlist|give|im|godmode|gm|spawn|cheat|setfaction|faction|tele|tt)\b", RegexOptions.IgnoreCase);
            await _alerts.RaiseAsync(def, "console", dangerous ? 1 : 0, $"{cmd}{(LiveService.Str(m, "allowed") == "false" ? " (refused)" : "")}",
                p?.SteamId, p?.Name ?? $"#{LiveService.Int(m, "player")}", p?.Playfield);
        }
    }

    // ------------------------------------------------------------------ admin side

    public object Moderation(long id)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var names = data.Players().ToDictionary(p => p.SteamId, p => p.Name);
        object Row(CounterRow c) => new { c.SteamId, name = names.GetValueOrDefault(c.SteamId), c.Count, c.LastUtc, c.Value };
        return new
        {
            mutes = data.Counters("mute").Where(c => DateTime.TryParse(c.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var u) && u > DateTime.UtcNow).Select(Row),
            warnings = data.Counters("filter").Where(c => c.Key == "filter" && c.Count > 0).Select(Row),
        };
    }

    public void Mute(long id, string steamId, int minutes, string by)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        var name = data.Players().FirstOrDefault(p => p.SteamId == steamId)?.Name ?? steamId;
        if (minutes <= 0) data.DeleteCounter(steamId, "mute");
        else data.SetCounter(steamId, "mute", 0, DateTime.UtcNow.AddMinutes(Math.Min(minutes, 60 * 24 * 365)).ToString("O"));
        data.PlayerEvent(steamId, name, minutes <= 0 ? "admin_unmute" : "admin_mute", minutes <= 0 ? "" : $"{minutes} min", by);
    }

    public void ResetWarnings(long id, string steamId, string by)
    {
        var def = _manager.Definition(id);
        var data = _live.Data(def);
        data.DeleteCounter(steamId, "filter");
        data.DeleteCounter(steamId, "filter_bans");
        data.PlayerEvent(steamId, data.Players().FirstOrDefault(p => p.SteamId == steamId)?.Name ?? steamId, "admin_reset_warnings", "", by);
    }
}
