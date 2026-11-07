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
        public const string ModVersion = "0.9.5";

        private ModGameAPI _api;
        private LinkServer _link;
        private ushort _seq = 1;
        private bool _hasChatEx;

        // Requests sent by the engine with a "rid": the game answers with the same sequence number,
        // so the answer can be tagged with the engine's request id.
        private sealed class Pending { public string Rid; public bool Full; }
        private readonly Dictionary<ushort, Pending> _pending = new Dictionary<ushort, Pending>();

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
                catch (Exception e) { link.Send(Ev("error", "op", r.Str("op"), "code", "exception", "detail", e.Message, "rid", r.Str("rid"))); }
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
            var rid = r.Str("rid");
            var id = (int)r.Num("id");
            switch (op)
            {
                case "ping": _link.Send(Ev("pong", "t", r.Num("t"), "rid", rid)); break;
                case "players": Request(CmdId.Request_Player_List, null, rid); break;
                case "player": Request(CmdId.Request_Player_Info, new Id(id), rid, string.Equals(r.Str("full"), "true", StringComparison.OrdinalIgnoreCase)); break;
                case "playfields": Request(CmdId.Request_Playfield_List, null, rid); break;
                case "pfstats": Request(CmdId.Request_Playfield_Stats, new PString(r.Str("pf") ?? ""), rid); break;
                case "dedi": Request(CmdId.Request_Dedi_Stats, null, rid); break;
                case "factions": Request(CmdId.Request_Get_Factions, new Id(1), rid); break;
                case "msg_all":
                    Request(CmdId.Request_InGameMessage_AllPlayers, new IdMsgPrio(0, Text(r), (byte)r.Num("prio", 1), (float)r.Num("secs", 10)), rid);
                    break;
                case "msg_player":
                    Request(CmdId.Request_InGameMessage_SinglePlayer, new IdMsgPrio(id, Text(r), (byte)r.Num("prio", 1), (float)r.Num("secs", 10)), rid);
                    break;
                case "console":
                    Request(CmdId.Request_ConsoleCommand, new PString(r.Str("cmd") ?? ""), rid);
                    break;

                // ---- step 5: players
                case "inventory_get": Request(CmdId.Request_Player_GetInventory, new Id(id), rid); break;
                case "inventory_set":
                    Request(CmdId.Request_Player_SetInventory, new Inventory(id, Stacks(r.List("toolbelt")), Stacks(r.List("bag"))), rid);
                    break;
                case "item_add":
                    Request(CmdId.Request_Player_AddItem, new IdItemStack(id, new ItemStack((int)r.Num("item"), (int)r.Num("count", 1))), rid);
                    break;
                case "credits_set": Request(CmdId.Request_Player_SetCredits, new IdCredits(id, r.Num("credits")), rid); break;
                case "credits_add": Request(CmdId.Request_Player_AddCredits, new IdCredits(id, r.Num("credits")), rid); break;
                case "player_set":
                {
                    var set = new PlayerInfoSet { entityId = id };
                    set.experiencePoints = OptInt(r, "exp");
                    set.upgradePoints = OptInt(r, "upgrade");
                    set.health = OptInt(r, "health");
                    set.healthMax = OptInt(r, "healthMax");
                    set.food = OptInt(r, "food");
                    set.oxygen = OptInt(r, "oxygen");
                    set.stamina = OptInt(r, "stamina");
                    set.radiation = OptInt(r, "radiation");
                    set.bodyTemp = OptInt(r, "bodyTemp");
                    if (r.OptNum("bpTime") is double bp) set.bpRemainingTime = (float)bp;
                    set.origin = OptInt(r, "origin");
                    if (OptInt(r, "factionGroup") is int fg) set.factionGroup = (byte)fg;
                    set.factionId = OptInt(r, "factionId");
                    if (OptInt(r, "factionRole") is int fr) set.factionRole = (byte)fr;
                    var sp = r.Str("startPlayfield");
                    if (!string.IsNullOrEmpty(sp)) set.startPlayfield = sp;
                    Request(CmdId.Request_Player_SetPlayerInfo, set, rid);
                    break;
                }
                case "teleport":
                {
                    var pos = new PVector3((float)r.Num("x"), (float)r.Num("y"), (float)r.Num("z"));
                    var rot = new PVector3((float)r.Num("rx"), (float)r.Num("ry"), (float)r.Num("rz"));
                    var pf = r.Str("pf");
                    if (!string.IsNullOrEmpty(pf)) Request(CmdId.Request_Player_ChangePlayerfield, new IdPlayfieldPositionRotation(id, pf, pos, rot), rid);
                    else Request(CmdId.Request_Entity_Teleport, new IdPositionRotation(id, pos, rot), rid);
                    break;
                }
                case "bp_finish": Request(CmdId.Request_Blueprint_Finish, new Id(id), rid); break;
                case "bp_resources":
                    Request(CmdId.Request_Blueprint_Resources, new BlueprintResources(id, new List<ItemStack>(Stacks(r.List("items"))), r.Num("replace") > 0), rid);
                    break;
                case "banned": Request(CmdId.Request_GetBannedPlayers, null, rid); break;

                // ---- step 6: structures and playfields
                case "structures": Request(CmdId.Request_GlobalStructure_List, null, rid); break;
                case "structures_update": Request(CmdId.Request_GlobalStructure_Update, new PString(r.Str("pf") ?? ""), rid); break;
                case "touch": Request(CmdId.Request_Structure_Touch, new Id(id), rid); break;
                case "destroy": Request(CmdId.Request_Entity_Destroy, new Id(id), rid); break;
                case "block_stats": Request(CmdId.Request_Structure_BlockStatistics, new Id(id), rid); break;
                case "rename": Request(CmdId.Request_Entity_SetName, new IdPlayfieldName(id, r.Str("pf") ?? "", r.Str("name") ?? ""), rid); break;
                case "entity_move":
                {
                    var pos = new PVector3((float)r.Num("x"), (float)r.Num("y"), (float)r.Num("z"));
                    var rot = new PVector3((float)r.Num("rx"), (float)r.Num("ry"), (float)r.Num("rz"));
                    var pf = r.Str("pf");
                    if (!string.IsNullOrEmpty(pf)) Request(CmdId.Request_Entity_ChangePlayfield, new IdPlayfieldPositionRotation(id, pf, pos, rot), rid);
                    else Request(CmdId.Request_Entity_Teleport, new IdPositionRotation(id, pos, rot), rid);
                    break;
                }
                case "entity_pos": Request(CmdId.Request_Entity_PosAndRot, new Id(id), rid); break;
                case "export":
                    Request(CmdId.Request_Entity_Export, new EntityExportInfo(id, r.Str("file") ?? "", r.Str("unload") == "true") { playfield = r.Str("pf") }, rid);
                    break;
                case "new_id": Request(CmdId.Request_NewEntityId, null, rid); break;
                case "spawn":
                {
                    var sp = new EntitySpawnInfo
                    {
                        forceEntityId = (int)r.Num("newId"),
                        playfield = r.Str("pf") ?? "",
                        pos = new PVector3((float)r.Num("x"), (float)r.Num("y"), (float)r.Num("z")),
                        rot = new PVector3((float)r.Num("rx"), (float)r.Num("ry"), (float)r.Num("rz")),
                        name = r.Str("name") ?? "",
                        type = (byte)r.Num("type", 3),
                        entityTypeName = "",
                        prefabName = r.Str("prefab") ?? "",
                        prefabDir = r.Str("prefabDir") ?? "",
                        factionGroup = (byte)r.Num("factionGroup"),
                        factionId = (int)r.Num("factionId"),
                        exportedEntityDat = r.Str("dat"),
                    };
                    Request(CmdId.Request_Entity_Spawn, sp, rid);
                    break;
                }
                case "pf_entities": Request(CmdId.Request_Playfield_Entity_List, new PString(r.Str("pf") ?? ""), rid); break;
                case "pf_load": Request(CmdId.Request_Load_Playfield, new PlayfieldLoad((float)r.Num("secs", 60), r.Str("pf") ?? "", 0), rid); break;

                // ---- step 7: faction messages
                case "msg_faction":
                    Request(CmdId.Request_InGameMessage_Faction, new IdMsgPrio(id, Text(r), (byte)r.Num("prio", 1), (float)r.Num("secs", 10)), rid);
                    break;
                default:
                    _link.Send(Ev("error", "op", op, "code", "unknown_op", "rid", rid));
                    break;
            }
        }

        private static int? OptInt(Dictionary<string, object> r, string k)
        {
            var v = r.OptNum(k);
            return v.HasValue ? (int?)(int)v.Value : null;
        }

        private static ItemStack[] Stacks(List<object> list)
        {
            var result = new List<ItemStack>();
            if (list != null)
                foreach (var o in list)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null || d.Num("id") <= 0 || d.Num("count") <= 0) continue;
                    var st = new ItemStack((int)d.Num("id"), (int)d.Num("count"));
                    st.slotIdx = (byte)d.Num("slot");
                    st.ammo = (int)d.Num("ammo");
                    st.decay = (int)d.Num("decay");
                    result.Add(st);
                }
            return result.ToArray();
        }

        private static List<object> StacksJson(ItemStack[] stacks)
        {
            var list = new List<object>();
            if (stacks != null)
                foreach (var st in stacks)
                    list.Add(new Dictionary<string, object> { ["id"] = st.id, ["count"] = st.count, ["slot"] = (int)st.slotIdx, ["ammo"] = st.ammo, ["decay"] = st.decay });
            return list;
        }

        private static string Text(Dictionary<string, object> r)
        {
            var t = r.Str("text") ?? "";
            return t.Length > 500 ? t.Substring(0, 500) : t;
        }

        private void Request(CmdId id, object data, string rid = null, bool full = false)
        {
            unchecked { _seq++; }
            if (_seq == 0) _seq = 1;
            if (rid != null || full)
            {
                if (_pending.Count > 1000) _pending.Clear(); // answers that never came: forget them
                _pending[_seq] = new Pending { Rid = rid, Full = full };
            }
            else _pending.Remove(_seq);
            _api.Game_Request(id, _seq, data);
        }

        // ------------------------------------------------------------- game → engine

        public void Game_Event(CmdId eventId, ushort seqNr, object data)
        {
            var link = _link;
            if (link == null || !link.Connected) return;
            try
            {
                Pending pending;
                if (_pending.TryGetValue(seqNr, out pending)) _pending.Remove(seqNr);
                var msg = Translate(eventId, data, pending != null && pending.Full);
                if (msg != null)
                {
                    if (pending != null && pending.Rid != null) msg["rid"] = pending.Rid;
                    link.Send(msg);
                }
            }
            catch (Exception e)
            {
                Log("EAM link: event " + eventId + " not forwarded: " + e.Message);
            }
        }

        private Dictionary<string, object> Translate(CmdId id, object data, bool full)
        {
            switch (id)
            {
                case CmdId.Event_Player_Connected when data is Id c: return Ev("player_connected", "id", c.id);
                case CmdId.Event_Player_Disconnected when data is Id d: return Ev("player_disconnected", "id", d.id);
                case CmdId.Event_Player_ChangedPlayfield when data is IdPlayfield ip: return Ev("player_playfield", "id", ip.id, "pf", ip.playfield);
                case CmdId.Event_Player_List when data is IdList l: return Ev("player_list", "ids", l.list ?? new List<int>());
                case CmdId.Event_Player_Info when data is PlayerInfo p:
                {
                    var info = PlayerInfo(p);
                    if (full)
                    {
                        info["toolbelt"] = StacksJson(p.toolbar);
                        info["bag"] = StacksJson(p.bag);
                        info["bpInFactory"] = p.bpInFactory;
                        info["bpRemaining"] = p.bpRemainingTime;
                        info["startPf"] = p.startPlayfield;
                        info["origin"] = (int)p.origin;
                        info["steamOwnerId"] = p.steamOwnerId;
                        info["clientId"] = p.clientId;
                    }
                    return info;
                }
                case CmdId.Event_Player_Inventory when data is Inventory inv:
                    return Ev("inventory", "id", inv.playerId, "toolbelt", StacksJson(inv.toolbelt), "bag", StacksJson(inv.bag));
                case CmdId.Event_Player_Credits when data is IdCredits cr: return Ev("credits", "id", cr.id, "credits", cr.credits);
                case CmdId.Event_BannedPlayers when data is BannedPlayerData bans:
                {
                    var bl = new List<object>();
                    if (bans.BannedPlayers != null)
                        foreach (var b in bans.BannedPlayers) bl.Add(Ev("b", "steamId", b.steam64Id.ToString(), "until", b.dateTime));
                    return Ev("banned", "list", bl);
                }
                case CmdId.Event_Ok: return Ev("ok");
                case CmdId.Event_GlobalStructure_List when data is GlobalStructureList gsl:
                {
                    var all = new List<object>();
                    if (gsl.globalStructures != null)
                        foreach (var kv in gsl.globalStructures)
                            if (kv.Value != null)
                                foreach (var g in kv.Value)
                                    all.Add(Ev("s", "id", g.id, "type", (int)g.type, "name", g.name, "pf", kv.Key, "fg", (int)g.factionGroup, "fid", g.factionId,
                                        "x", g.pos.x, "y", g.pos.y, "z", g.pos.z, "rx", g.rot.x, "ry", g.rot.y, "rz", g.rot.z,
                                        "powered", g.powered, "fuel", g.fuel, "devices", g.cntDevices, "blocks", g.cntBlocks, "triangles", g.cntTriangles,
                                        "lights", g.cntLights, "class", g.classNr, "core", (int)g.coreType, "pilot", g.pilotId,
                                        "visited", g.lastVisitedUTC, "docked", g.dockedShips ?? new List<int>(),
                                        "ss", g.SolarSystemName, "ssx", g.SolarSystemCoord.x, "ssy", g.SolarSystemCoord.y, "ssz", g.SolarSystemCoord.z));
                    return Ev("structures", "list", all);
                }
                case CmdId.Event_Structure_BlockStatistics when data is IdStructureBlockInfo bs:
                {
                    var stats = new List<object>();
                    if (bs.blockStatistics != null) foreach (var kv in bs.blockStatistics) stats.Add(Ev("b", "id", kv.Key, "count", kv.Value));
                    return Ev("block_stats", "id", bs.id, "list", stats);
                }
                case CmdId.Event_Entity_PosAndRot when data is IdPositionRotation epr:
                    return Ev("entity_pos", "id", epr.id, "x", epr.pos.x, "y", epr.pos.y, "z", epr.pos.z, "rx", epr.rot.x, "ry", epr.rot.y, "rz", epr.rot.z);
                case CmdId.Event_NewEntityId when data is Id nid: return Ev("new_id", "id", nid.id);
                case CmdId.Event_Playfield_Entity_List when data is PlayfieldEntityList pel:
                {
                    var ents = new List<object>();
                    if (pel.entities != null) foreach (var e in pel.entities) ents.Add(Ev("e", "id", e.id, "type", e.type, "x", e.pos.x, "y", e.pos.y, "z", e.pos.z));
                    return Ev("pf_entities", "pf", pel.playfield, "list", ents);
                }
                case CmdId.Event_Statistics when data is StatisticsParam st:
                    return Ev("stat", "kind", st.type.ToString(), "i1", st.int1, "i2", st.int2, "i3", st.int3, "i4", st.int4);
                case CmdId.Event_ConsoleCommand when data is ConsoleCommandInfo cc:
                    return Ev("console_cmd", "cmd", cc.command, "allowed", cc.allowed, "player", cc.playerEntityId);
                case CmdId.Event_Player_DisconnectedWaiting when data is Id dw: return Ev("player_waiting_lost", "id", dw.id);
                case CmdId.Event_TraderNPCItemSold when data is TraderNPCItemSoldInfo ts:
                    return Ev("trader_sold", "player", ts.playerEntityId, "item", ts.boughtItemId, "count", ts.boughtItemCount, "price", ts.boughtItemPrice);
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
                case CmdId.Event_Faction_Changed when data is FactionChangeInfo fc: return Ev("faction_changed", "id", fc.id, "factionId", fc.factionId);
                case CmdId.Event_Error when data is ErrorInfo err: return Ev("error", "code", err.errorType.ToString());
                case CmdId.Event_Error: return Ev("error", "code", "game_error");
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
