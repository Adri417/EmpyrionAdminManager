// Step 3: live view of a running server (players, playfields, performance) and chat.
import { api } from "./api.js";
import { t, extend, errorText } from "./i18n.js";
import { html, mount, $, toast, formatBytes } from "./dom.js";

extend({
  fr: {
    tab_chat: "Chat", live_players: "Joueurs en ligne", live_playfields: "Playfields actifs", live_fps: "FPS serveur",
    live_cpu: "Processeur (serveur)", live_ram: "Mémoire (serveur)", live_processes: "processus", live_nobody: "Personne n'est connecté.",
    live_no_playfield: "Aucun playfield chargé.", live_offline: "Le serveur est arrêté : la vue en direct s'affiche quand il tourne.",
    mod_ok: "Mod EAM connecté", mod_waiting: "Mod EAM : en attente", col_name: "Nom", col_faction: "Faction", col_playfield: "Playfield",
    col_level: "Niveau", col_ping: "Ping", col_online: "Connecté depuis", col_players: "Joueurs", col_mem: "Mémoire",
    col_structs: "Structures", col_pid: "Processus",
    chat_empty: "Aucun message pour l'instant.", chat_older: "Messages plus anciens", chat_mode_chat: "Dans le chat",
    chat_mode_popup: "Fenêtre à l'écran de tous", chat_placeholder: "Votre message…", chat_readonly: "Lecture seule : votre rôle ne permet pas d'envoyer de messages.",
    ch_Global: "Global", ch_Faction: "Faction", ch_Alliance: "Alliance", ch_SinglePlayer: "Privé", ch_Server: "Serveur",
    joined: "{0} s'est connecté à {1}", left: "{0} s'est déconnecté de {1}",
    err_mod_not_connected: "Le mod EAM n'est pas encore connecté à ce serveur.", err_server_not_running: "Le serveur n'est pas en ligne.",
    err_message_empty: "Message vide.", err_message_too_long: "Message trop long (300 caractères maximum).",
    err_mod_key_rejected: "Le mod EAM a refusé la clé : redémarrez le serveur.", err_mod_not_reachable: "Le mod EAM ne répond pas encore (il démarre avec le jeu).",
  },
  en: {
    tab_chat: "Chat", live_players: "Players online", live_playfields: "Active playfields", live_fps: "Server FPS",
    live_cpu: "CPU (server)", live_ram: "Memory (server)", live_processes: "processes", live_nobody: "Nobody is connected.",
    live_no_playfield: "No playfield loaded.", live_offline: "The server is stopped: the live view appears when it runs.",
    mod_ok: "EAM mod connected", mod_waiting: "EAM mod: waiting", col_name: "Name", col_faction: "Faction", col_playfield: "Playfield",
    col_level: "Level", col_ping: "Ping", col_online: "Online for", col_players: "Players", col_mem: "Memory",
    col_structs: "Structures", col_pid: "Process",
    chat_empty: "No message yet.", chat_older: "Older messages", chat_mode_chat: "In the chat",
    chat_mode_popup: "Message box on everyone's screen", chat_placeholder: "Your message…", chat_readonly: "Read only: your role cannot send messages.",
    ch_Global: "Global", ch_Faction: "Faction", ch_Alliance: "Alliance", ch_SinglePlayer: "Private", ch_Server: "Server",
    joined: "{0} joined {1}", left: "{0} left {1}",
    err_mod_not_connected: "The EAM mod is not connected to this server yet.", err_server_not_running: "The server is not online.",
    err_message_empty: "Empty message.", err_message_too_long: "Message too long (300 characters max).",
    err_mod_key_rejected: "The EAM mod refused the key: restart the server.", err_mod_not_reachable: "The EAM mod does not answer yet (it starts with the game).",
  },
});

const ROLES = ["owner", "admin", "gamemaster", "moderator", "viewer"];
const atLeast = (role, required) => ROLES.indexOf(role) <= ROLES.indexOf(required);

export const live = { snapshots: {}, chat: {}, chatListener: null };

/** Messages of the live socket that belong to step 3. Returns true when handled. */
export function liveEvent(msg, serverName) {
  if (msg.type === "live") { live.snapshots[msg.id] = msg.data; return true; }
  if (msg.type === "chat") {
    const list = live.chat[msg.id];
    if (list && !list.some((m) => m.id === msg.msg.id)) list.push(msg.msg);
    live.chatListener?.(msg.id, msg.msg);
    return true;
  }
  if (msg.type === "player_joined" || msg.type === "player_left") {
    toast(t(msg.type === "player_joined" ? "joined" : "left", msg.name, serverName(msg.id)));
    return true;
  }
  return false;
}

export async function fetchLive(id) {
  try { live.snapshots[id] = await api.get(`/api/servers/${id}/live`); } catch {}
  return live.snapshots[id];
}

export const playerCount = (id) => live.snapshots[id]?.players?.length;

function since(utc) {
  const s = Math.max(0, (Date.now() - new Date(utc).getTime()) / 1000);
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60);
  return h ? `${h} h ${m} min` : `${m} min`;
}

export function renderLive(el, s) {
  const l = live.snapshots[s.id];
  if (!["Running", "Starting", "Stopping"].includes(s.state) || !l) {
    mount(el, html`<div class="card"><div class="hint">${t("live_offline")}</div></div>`);
    return;
  }
  const mod = l.connected
    ? html`<span class="state ok">${t("mod_ok")}${l.modVersion ? ` ${l.modVersion}` : ""}</span>`
    : html`<span class="state busy">${l.linkError ? errorText(l.linkError) : t("mod_waiting")}</span>`;
  mount(el, html`
    <div class="grid">
      <div class="card"><h3>${t("live_players")}</h3><div class="stat">${l.players.length}</div><div class="hint">${mod}</div></div>
      <div class="card"><h3>${t("live_cpu")}</h3><div class="stat">${l.cpuPercent.toFixed(0)}<small>%</small></div><div class="bar"><i data-pct="${l.cpuPercent}"></i></div></div>
      <div class="card"><h3>${t("live_ram")}</h3><div class="stat">${formatBytes(l.memoryBytes)}</div><div class="hint">${l.processCount} ${t("live_processes")}</div></div>
      <div class="card"><h3>${t("live_fps")}</h3><div class="stat">${l.fps != null ? l.fps.toFixed(0) : "—"}</div></div>
    </div>
    <div class="card"><h3>${t("live_players")}</h3>
      ${l.players.length === 0 ? html`<div class="hint">${t("live_nobody")}</div>` : html`<div class="table-wrap"><table>
        <thead><tr><th>${t("col_name")}</th><th>${t("col_faction")}</th><th>${t("col_playfield")}</th><th>${t("col_level")}</th><th>${t("col_ping")}</th><th>${t("col_online")}</th></tr></thead>
        <tbody>${l.players.map((p) => html`<tr><td><b>${p.name}</b><div class="hint">${p.steamId || ""}</div></td><td>${p.faction || "—"}</td>
          <td>${p.playfield || "—"}</td><td>${p.level}</td><td>${p.ping} ms</td><td>${since(p.onlineSinceUtc)}</td></tr>`)}</tbody></table></div>`}
    </div>
    <div class="card"><h3>${t("live_playfields")}</h3>
      ${l.playfields.length === 0 ? html`<div class="hint">${t("live_no_playfield")}</div>` : html`<div class="table-wrap"><table>
        <thead><tr><th>${t("col_name")}</th><th>${t("col_players")}</th><th>FPS</th><th>${t("col_mem")}</th><th>${t("col_structs")}</th><th>${t("col_pid")}</th></tr></thead>
        <tbody>${l.playfields.map((p) => html`<tr><td><b>${p.name}</b></td><td>${p.players}</td><td>${p.fps || "—"}</td>
          <td>${p.memoryMb ? `${p.memoryMb} Mo` : "—"}</td><td>${p.structures}</td><td>${p.processId || "—"}</td></tr>`)}</tbody></table></div>`}
    </div>`);
  el.querySelectorAll(".bar > i[data-pct]").forEach((i) => { i.style.width = `${Math.min(100, i.dataset.pct)}%`; });
}

// ---------------------------------------------------------------- chat
function line(m) {
  const time = new Date(m.utc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
  return html`<div class="chat-line ${m.source === "eam" ? "from-eam" : ""}">
    <span class="chat-time">${time}</span><span class="badge">${t("ch_" + m.channel) === "ch_" + m.channel ? m.channel : t("ch_" + m.channel)}</span>
    <b>${m.senderName}</b> <span class="chat-text">${m.text}</span></div>`;
}

export async function renderChat(el, s, me) {
  const canSend = atLeast(me.role, "moderator");
  mount(el, html`<div class="card">
    <div id="chat-more-wrap"></div>
    <div class="chat" id="chat-list"></div>
    ${canSend ? html`<form class="console-form" id="chat-form">
        <input name="text" type="text" maxlength="300" autocomplete="off" placeholder="${t("chat_placeholder")}">
        <select name="mode"><option value="chat">${t("chat_mode_chat")}</option><option value="popup">${t("chat_mode_popup")}</option></select>
        <button class="btn primary">${t("send")}</button></form>`
      : html`<div class="hint">${t("chat_readonly")}</div>`}</div>`);
  const list = $("#chat-list", el);
  const draw = (keepScroll) => {
    const msgs = live.chat[s.id] || [];
    const atBottom = list.scrollHeight - list.scrollTop - list.clientHeight < 40;
    mount(list, msgs.length ? html`${msgs.map(line)}` : html`<div class="hint">${t("chat_empty")}</div>`);
    if (!keepScroll || atBottom) list.scrollTop = list.scrollHeight;
    mount($("#chat-more-wrap", el), msgs.length >= 100 ? html`<button class="btn" id="chat-more">${t("chat_older")}</button>` : html``);
    $("#chat-more", el)?.addEventListener("click", async () => {
      try {
        const older = await api.get(`/api/servers/${s.id}/chat?before=${msgs[0].id}&limit=100`);
        live.chat[s.id] = [...older, ...msgs];
        const h = list.scrollHeight;
        draw(true);
        list.scrollTop = list.scrollHeight - h;
      } catch (ex) { toast(errorText(ex.code), true); }
    });
  };
  try { live.chat[s.id] = await api.get(`/api/servers/${s.id}/chat?limit=100`); } catch { live.chat[s.id] = []; }
  draw(false);
  live.chatListener = (id) => { if (id === s.id && list.isConnected) draw(true); };
  $("#chat-form", el)?.addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = e.target;
    const text = f.text.value.trim();
    if (!text) return;
    f.querySelector("button").disabled = true;
    try {
      await api.post(`/api/servers/${s.id}/chat`, { text, mode: f.mode.value });
      f.text.value = "";
      if (f.mode.value === "popup") toast(t("saved"));
    } catch (ex) { toast(errorText(ex.code), true); }
    f.querySelector("button").disabled = false;
    f.text.focus();
  });
}
