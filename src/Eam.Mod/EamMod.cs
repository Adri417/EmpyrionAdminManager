using System;
using System.Collections.Generic;
using System.IO;
using Eam.Link;
using Eleon.Modding;

namespace Eam.Mod
{
    /// <summary>
    /// EAM game mod. Installed by EAM in Content\Mods\EAM of each server, with its own
    /// port and secret key (eam-link.cfg). It only listens on 127.0.0.1: nothing is opened to the Internet.
    /// It forwards game events to the engine and runs the engine's requests on the game thread.
    /// </summary>
    public sealed class EamMod : ModInterface
    {
        public const string ModVersion = "0.3.1";

        private ModGameAPI _api;
        private LinkServer _link;
        private ushort _seq = 1;
        private bool _hasChatEx;

        public void Game_Start(ModGameAPI dediAPI)
        {
            _api = dediAPI;
            try
            {
                var dir = Path.GetDirectoryName(typeof(EamMod).Assembly.Location) ?? ".";
                var cfg = ReadConfig(Path.Combine(dir, "eam-link.cfg"));
                if (!cfg.TryGetValue("port", out var p) || !int.TryParse(p, out var port) || !cfg.TryGetValue("key", out var key) || key.Length < 16)
                {
                    Log("eam-link.cfg missing or invalid: EAM link disabled");
                    return;
                }
                _link = new LinkServer(port, key, Log);
                _link.ClientAuthenticated += () => _link.Send(Ev("hello", "ver", LinkProtocol.Version, "mod", ModVersion));
                _link.Start();
            }
            catch (Exception e)
            {
                Log("EAM link could not start: " + e.Message);
                _link = null;
            }
        }

        public void Game_Update()
        {
            var link = _link;
            if (link == null) return;
            var requests = link.Drain();
            if (requests == null) return;
            foreach (var r in requests)
            {
                try { Execute(r); }
                catch (Exception e) { link.Send(Ev("error", "op", r.Str("op"), "code", "exception", "detail", e.Message)); }
            }
        }

        public void Game_Exit()
        {
            try { _link?.Dispose(); } catch { }
            _link = null;
        }

        // ------------------------------------------------------------- engine → game

        private void Execute(Dictionary<string, object> r)
        {
            var op = r.Str("op");
            switch (op)
            {
                case "ping": _link.Send(Ev("pong", "t", r.Num("t"))); break;
                case "players": Request(CmdId.Request_Player_List, null); break;
                case "player": Request(CmdId.Request_Player_Info, new Id((int)r.Num("id"))); break;
                case "playfields": Request(CmdId.Request_Playfield_List, null); break;
                case "pfstats": Request(CmdId.Request_Playfield_Stats, new PString(r.Str("pf") ?? "")); break;
                case "dedi": Request(CmdId.Request_Dedi_Stats, null); break;
                case "factions": Request(CmdId.Request_Get_Factions, new Id(1)); break;
                case "msg_all":
                    Request(CmdId.Request_InGameMessage_AllPlayers, new IdMsgPrio(0, Text(r), (byte)r.Num("prio", 1), (float)r.Num("secs", 10)));
                    break;
                case "msg_player":
                    Request(CmdId.Request_InGameMessage_SinglePlayer, new IdMsgPrio((int)r.Num("id"), Text(r), (byte)r.Num("prio", 1), (float)r.Num("secs", 10)));
                    break;
                case "console":
                    Request(CmdId.Request_ConsoleCommand, new PString(r.Str("cmd") ?? ""));
                    break;
                default:
                    _link.Send(Ev("error", "op", op, "code", "unknown_op"));
                    break;
            }
        }

        private static string Text(Dictionary<string, object> r)
        {
            var t = r.Str("text") ?? "";
            return t.Length > 500 ? t.Substring(0, 500) : t;
        }

        private void Request(CmdId id, object data)
        {
            unchecked { _seq++; }
            if (_seq == 0) _seq = 1;
            _api.Game_Request(id, _seq, data);
        }

        // ------------------------------------------------------------- game → engine

        public void Game_Event(CmdId eventId, ushort seqNr, object data)
        {
            var link = _link;
            if (link == null || !link.Connected) return;
            try
            {
                var msg = Translate(eventId, data);
                if (msg != null) link.Send(msg);
            }
            catch (Exception e)
            {
                Log("EAM link: event " + eventId + " not forwarded: " + e.Message);
            }
        }

        private Dictionary<string, object> Translate(CmdId id, object data)
        {
            switch (id)
            {
                case CmdId.Event_Player_Connected when data is Id c: return Ev("player_connected", "id", c.id);
                case CmdId.Event_Player_Disconnected when data is Id d: return Ev("player_disconnected", "id", d.id);
                case CmdId.Event_Player_ChangedPlayfield when data is IdPlayfield ip: return Ev("player_playfield", "id", ip.id, "pf", ip.playfield);
                case CmdId.Event_Player_List when data is IdList l: return Ev("player_list", "ids", l.list ?? new List<int>());
                case CmdId.Event_Player_Info when data is PlayerInfo p: return PlayerInfo(p);
                case CmdId.Event_Playfield_List when data is PlayfieldList pl: return Ev("playfield_list", "list", pl.playfields ?? new List<string>());
                case CmdId.Event_Playfield_Stats when data is PlayfieldStats s:
                    return Ev("playfield_stats", "pf", s.playfield, "fps", s.fps, "fpsmin", s.fpsmin, "mem", s.mem, "players", s.players,
                        "mobs", s.mobs, "structs", s.structs, "devices", s.devices, "chunks", s.chunks, "uptime", s.uptime, "pid", s.processId);
                case CmdId.Event_Playfield_Loaded when data is PlayfieldLoad ld: return Ev("playfield_loaded", "pf", ld.playfield, "pid", ld.processId, "sec", ld.sec);
                case CmdId.Event_Playfield_Unloaded when data is PlayfieldLoad ul: return Ev("playfield_unloaded", "pf", ul.playfield, "pid", ul.processId);
                case CmdId.Event_Dedi_Stats when data is DediStats ds: return Ev("dedi", "fps", ds.fps, "mem", ds.mem, "players", ds.players, "uptime", ds.uptime);
                case CmdId.Event_ChatMessageEx when data is ChatMsgData cx:
                    _hasChatEx = true;
                    return Ev("chat", "sender", cx.SenderEntityId, "senderType", cx.SenderType.ToString(), "channel", cx.Channel.ToString(),
                        "name", cx.SenderNameOverride, "faction", cx.SenderFactionId, "recipient", cx.RecipientEntityId,
                        "recipientFaction", cx.RecipientFactionId, "text", cx.Text);
                case CmdId.Event_ChatMessage when data is ChatInfo ci:
                    if (_hasChatEx) return null; // the newer event already carries the message
                    return Ev("chat", "sender", ci.playerId, "senderType", "Player", "channel", ChannelFromType(ci.type),
                        "recipient", ci.recipientEntityId, "recipientFaction", ci.recipientFactionId, "text", ci.msg);
                case CmdId.Event_Get_Factions when data is FactionInfoList fl:
                    var list = new List<object>();
                    if (fl.factions != null)
                        foreach (var f in fl.factions) list.Add(Ev("f", "id", f.factionId, "name", f.name, "abbrev", f.abbrev));
                    return Ev("factions", "list", list);
                case CmdId.Event_Error when data is ErrorInfo err: return Ev("error", "code", err.errorType.ToString());
                default: return null;
            }
        }

        private static string ChannelFromType(byte type)
        {
            // Legacy chat types: 3 = global, 5 = faction, 7 = alliance, 8/9 = private (observed values).
            switch (type)
            {
                case 5: return "Faction";
                case 7: return "Alliance";
                case 8: case 9: return "SinglePlayer";
                default: return "Global";
            }
        }

        private static Dictionary<string, object> PlayerInfo(PlayerInfo p)
        {
            return Ev("player_info",
                "id", p.entityId, "steamId", p.steamId, "name", p.playerName, "pf", p.playfield,
                "x", p.pos.x, "y", p.pos.y, "z", p.pos.z,
                "origin", p.origin, "factionGroup", p.factionGroup, "factionId", p.factionId, "factionRole", p.factionRole,
                "health", p.health, "healthMax", p.healthMax, "food", p.food, "foodMax", p.foodMax,
                "oxygen", p.oxygen, "oxygenMax", p.oxygenMax, "stamina", p.stamina, "staminaMax", p.staminaMax,
                "radiation", p.radiation, "bodyTemp", p.bodyTemp,
                "credits", p.credits, "exp", p.exp, "upgrade", p.upgrade, "kills", p.kills, "died", p.died,
                "ping", p.ping, "permission", p.permission);
        }

        private static Dictionary<string, object> Ev(string type, params object[] kv)
        {
            var d = new Dictionary<string, object> { ["ev"] = type };
            for (var i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        private static Dictionary<string, string> ReadConfig(string file)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(file)) return d;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        private void Log(string text)
        {
            try { _api?.Console_Write("[EAM] " + text); } catch { }
        }
    }
}
