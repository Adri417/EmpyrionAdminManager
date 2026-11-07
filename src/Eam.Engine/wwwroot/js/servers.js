import { api } from "./api.js";
import { t, extend, errorText } from "./i18n.js";
import { html, raw, mount, $, $$, toast } from "./dom.js";

extend({
  fr: {
    srv_new: "Nouveau serveur", srv_name: "Nom du serveur", srv_name_hint: "Affiché dans EAM et dans la liste des serveurs du jeu. Sert aussi à nommer son dossier. Modifiable plus tard.",
    srv_game_port: "Port de jeu (+3 suivants)", srv_telnet_port: "Port telnet (interne)", srv_max_players: "Joueurs max",
    srv_password: "Mot de passe pour rejoindre (optionnel)", srv_description: "Description", srv_branch: "Version du jeu",
    branch_public: "Stable (public)", branch_experimental: "Expérimentale", srv_public: "Visible dans la liste publique des serveurs",
    srv_autostart: "Démarrer avec EAM", srv_autorestart: "Redémarrer automatiquement après un plantage",
    srv_create: "Créer et installer", srv_ports_hint: "Ports proposés automatiquement et vérifiés : aucun autre serveur ni programme ne les utilise.",
    state_NotInstalled: "Non installé", state_Installing: "Installation", state_Updating: "Mise à jour", state_Stopped: "Arrêté",
    state_Starting: "Démarrage", state_Running: "En ligne", state_Stopping: "Arrêt", state_Crashed: "Planté",
    act_start: "Démarrer", act_stop: "Arrêter", act_restart: "Redémarrer", act_kill: "Forcer l'arrêt", act_update: "Mettre à jour",
    act_validate: "Vérifier les fichiers", act_rename: "Renommer", act_delete: "Supprimer", act_cancel_stop: "Annuler l'arrêt",
    act_resume: "Réactiver le redémarrage auto", act_open: "Ouvrir", act_install: "Installer",
    tab_overview: "Vue d'ensemble", tab_console: "Console", tab_settings: "Réglages", tab_logs: "Journaux",
    folder: "Dossier", ports: "Ports", build: "Build Steam", branch: "Branche", since: "En ligne depuis",
    stop_title: "Arrêter le serveur", restart_title: "Redémarrer le serveur", delay_minutes: "Prévenir les joueurs pendant (minutes)",
    stop_message: "Message affiché aux joueurs", stop_hint: "Le serveur enregistre la partie (saveandexit) avant de s'arrêter. Il n'est coupé de force qu'au bout de 5 minutes sans réponse.",
    kill_confirm: "Forcer l'arrêt sans sauvegarder ? À utiliser seulement si le serveur est bloqué.",
    delete_title: "Supprimer le serveur", delete_files: "Supprimer aussi tous ses fichiers (jeu, sauvegardes)", delete_confirm: "Tapez le nom du serveur pour confirmer",
    rename_title: "Renommer le serveur", rename_hint: "Le dossier et le nom dans la liste du jeu suivent. Le serveur doit être arrêté.",
    console_hint: "Commandes de la console du serveur (telnet). Exemples : help, plys, say 'Bonjour'",
    send: "Envoyer", game_log: "Journal du jeu", eam_log: "Journal EAM du serveur", refresh: "Actualiser",
    applies_restart: "Enregistré. Appliqué au prochain démarrage du serveur.", crash_loop: "Plantages en boucle : redémarrage automatique suspendu.",
    no_server_yet: "Créez votre premier serveur : EAM télécharge SteamCMD et installe Empyrion tout seul.",
    progress_download_steamcmd: "Téléchargement de SteamCMD", progress_downloading: "Téléchargement", progress_verifying: "Vérification",
    progress_preallocating: "Préparation", progress_committing: "Finalisation", progress_done: "Terminé",
    err_name_taken: "Un serveur porte déjà ce nom.", err_invalid_name: "Nom : 2 à 60 caractères.",
    err_port_out_of_range: "Ports entre 1024 et 65535.", err_port_overlap: "Le port telnet chevauche les ports de jeu.",
    err_port_used_by_server: "Un autre serveur EAM utilise déjà un de ces ports.", err_port_used_by_program: "Un autre programme utilise déjà un de ces ports.",
    err_folder_used_by_server: "Ce dossier appartient déjà à un autre serveur.", err_folder_not_empty: "Le dossier de destination n'est pas vide.",
    err_server_busy: "Une opération est déjà en cours sur ce serveur.", err_server_must_be_stopped: "Arrêtez d'abord le serveur.",
    err_not_installed: "Le serveur n'est pas encore installé.", err_already_running: "Le serveur tourne déjà.",
    err_not_running: "Le serveur n'est pas en ligne.", err_game_files_missing: "Fichiers du jeu introuvables : lancez « Vérifier les fichiers ».",
    err_confirm_name_mismatch: "Le nom tapé ne correspond pas.", err_telnet_failed: "La console du serveur ne répond pas.",
    err_invalid_command: "Commande invalide.", err_steamcmd_windows_only: "SteamCMD ne fonctionne que sous Windows.",
    err_crash_loop: "Plantages en boucle.", err_start_timeout: "Le serveur met très longtemps à démarrer.",
    err_invalid_max_players: "Joueurs max : 1 à 200.", err_stop_cancelled: "Arrêt annulé.", err_unsafe_delete: "Suppression refusée : dossier hors d'EAM.",
  },
  en: {
    srv_new: "New server", srv_name: "Server name", srv_name_hint: "Shown in EAM and in the game's server list. Also names its folder. Can be changed later.",
    srv_game_port: "Game port (+3 next)", srv_telnet_port: "Telnet port (internal)", srv_max_players: "Max players",
    srv_password: "Join password (optional)", srv_description: "Description", srv_branch: "Game version",
    branch_public: "Stable (public)", branch_experimental: "Experimental", srv_public: "Listed in the public server list",
    srv_autostart: "Start with EAM", srv_autorestart: "Restart automatically after a crash",
    srv_create: "Create and install", srv_ports_hint: "Ports suggested automatically and checked: no other server or program uses them.",
    state_NotInstalled: "Not installed", state_Installing: "Installing", state_Updating: "Updating", state_Stopped: "Stopped",
    state_Starting: "Starting", state_Running: "Online", state_Stopping: "Stopping", state_Crashed: "Crashed",
    act_start: "Start", act_stop: "Stop", act_restart: "Restart", act_kill: "Force stop", act_update: "Update",
    act_validate: "Verify files", act_rename: "Rename", act_delete: "Delete", act_cancel_stop: "Cancel stop",
    act_resume: "Re-enable auto restart", act_open: "Open", act_install: "Install",
    tab_overview: "Overview", tab_console: "Console", tab_settings: "Settings", tab_logs: "Logs",
    folder: "Folder", ports: "Ports", build: "Steam build", branch: "Branch", since: "Online since",
    stop_title: "Stop the server", restart_title: "Restart the server", delay_minutes: "Warn players for (minutes)",
    stop_message: "Message shown to players", stop_hint: "The server saves the game (saveandexit) before stopping. It is only force-stopped after 5 minutes without response.",
    kill_confirm: "Force stop without saving? Only use this if the server is frozen.",
    delete_title: "Delete the server", delete_files: "Also delete all its files (game, saves)", delete_confirm: "Type the server name to confirm",
    rename_title: "Rename the server", rename_hint: "The folder and the name in the game list follow. The server must be stopped.",
    console_hint: "Server console commands (telnet). Examples: help, plys, say 'Hello'",
    send: "Send", game_log: "Game log", eam_log: "EAM server log", refresh: "Refresh",
    applies_restart: "Saved. Applied on the next server start.", crash_loop: "Crash loop: automatic restart suspended.",
    no_server_yet: "Create your first server: EAM downloads SteamCMD and installs Empyrion by itself.",
    progress_download_steamcmd: "Downloading SteamCMD", progress_downloading: "Downloading", progress_verifying: "Verifying",
    progress_preallocating: "Preparing", progress_committing: "Finishing", progress_done: "Done",
    err_name_taken: "A server already has this name.", err_invalid_name: "Name: 2 to 60 characters.",
    err_port_out_of_range: "Ports between 1024 and 65535.", err_port_overlap: "The telnet port overlaps the game ports.",
    err_port_used_by_server: "Another EAM server already uses one of these ports.", err_port_used_by_program: "Another program already uses one of these ports.",
    err_folder_used_by_server: "This folder already belongs to another server.", err_folder_not_empty: "The destination folder is not empty.",
    err_server_busy: "An operation is already running on this server.", err_server_must_be_stopped: "Stop the server first.",
    err_not_installed: "The server is not installed yet.", err_already_running: "The server is already running.",
    err_not_running: "The server is not online.", err_game_files_missing: "Game files not found: run \"Verify files\".",
    err_confirm_name_mismatch: "The typed name does not match.", err_telnet_failed: "The server console does not respond.",
    err_invalid_command: "Invalid command.", err_steamcmd_windows_only: "SteamCMD only works on Windows.",
    err_crash_loop: "Crash loop.", err_start_timeout: "The server is taking very long to start.",
    err_invalid_max_players: "Max players: 1 to 200.", err_stop_cancelled: "Stop cancelled.", err_unsafe_delete: "Delete refused: folder outside EAM.",
  },
});

const ROLES = ["owner", "admin", "gamemaster", "moderator", "viewer"];
const atLeast = (role, required) => ROLES.indexOf(role) <= ROLES.indexOf(required);
const store = { servers: [], me: null, onChange: null };

export function initServers(me) { store.me = me; }

/** Called by the live socket. */
export function serverEvent(msg) {
  if (msg.type === "server") {
    const i = store.servers.findIndex((s) => s.id === msg.data.id);
    if (i >= 0) store.servers[i] = msg.data; else store.servers.push(msg.data);
  }
  if (msg.type === "servers") loadServers();
  store.onChange?.();
}

export async function loadServers() {
  try { store.servers = await api.get("/api/servers"); } catch { store.servers = []; }
  store.onChange?.();
  return store.servers;
}

const busyStates = ["Installing", "Updating", "Starting", "Stopping"];
const stateClass = (s) => ({ Running: "ok", Crashed: "bad", Stopped: "", NotInstalled: "" })[s.state] ?? "busy";

function stateBadge(s) {
  return html`<span class="state ${stateClass(s)}">${t("state_" + s.state)}${s.operation === "stop" && s.progressText ? ` · ${s.progressText}` : ""}</span>`;
}

function progressBar(s) {
  if (s.progress == null) return "";
  const label = t("progress_" + (s.progressText || "").replace(/ /g, "_"));
  return html`<div class="hint">${label.startsWith("progress_") ? s.progressText : label} · ${s.progress.toFixed(1)} %</div>
    <div class="bar"><i data-pct="${s.progress}"></i></div>`;
}

function applyBars(root) { $$(".bar > i[data-pct]", root).forEach((i) => { i.style.width = `${i.dataset.pct}%`; }); }

const fail = (ex) => toast(ex.body?.detail && ex.code === "port_used_by_program" ? `${errorText(ex.code)} (${ex.body.detail})` : errorText(ex.code), true);

// ---------------------------------------------------------------- list
export async function renderServerList(page) {
  const canCreate = atLeast(store.me.role, "owner");
  const draw = () => {
    mount(page, html`
      ${canCreate ? html`<div><button class="btn primary" id="new">+ ${t("srv_new")}</button></div>` : ""}
      ${store.servers.length === 0
        ? html`<div class="card"><div class="empty"><div class="big">▣</div><b>${t("servers_empty_title")}</b><div>${t("no_server_yet")}</div></div></div>`
        : html`<div class="grid servers">${store.servers.map((s) => html`
          <a class="card server-card" href="#/server/${s.id}">
            <div class="server-head"><b>${s.name}</b>${stateBadge(s)}</div>
            <div class="hint">${t("ports")} ${s.gamePort}-${s.gamePort + 3} · ${t(s.branch === "experimental" ? "branch_experimental" : "branch_public")}</div>
            ${progressBar(s)}
            ${s.lastError ? html`<div class="error">${errorText(s.lastError)}</div>` : ""}
          </a>`)}</div>`}`);
    applyBars(page);
    $("#new", page)?.addEventListener("click", () => createDialog());
  };
  store.onChange = draw;
  draw();
  await loadServers();
}

async function createDialog() {
  let ports = { gamePort: 30000, telnetPort: 30004 };
  try { ports = await api.get("/api/servers/suggest-ports"); } catch {}
  const dlg = document.createElement("dialog");
  mount(dlg, html`<form method="dialog" id="cf">
    <h3>${t("srv_new")}</h3>
    <label>${t("srv_name")}</label><input name="name" type="text" required minlength="2" maxlength="60" autofocus>
    <div class="hint">${t("srv_name_hint")}</div>
    <div class="form-row">
      <div><label>${t("srv_game_port")}</label><input name="gamePort" type="number" min="1024" max="65530" value="${ports.gamePort}"></div>
      <div><label>${t("srv_telnet_port")}</label><input name="telnetPort" type="number" min="1024" max="65535" value="${ports.telnetPort}"></div>
    </div>
    <div class="hint">${t("srv_ports_hint")}</div>
    <div class="form-row">
      <div><label>${t("srv_max_players")}</label><input name="maxPlayers" type="number" min="1" max="200" value="8"></div>
      <div><label>${t("srv_branch")}</label><select name="branch"><option value="public">${t("branch_public")}</option><option value="experimental">${t("branch_experimental")}</option></select></div>
    </div>
    <label>${t("srv_password")}</label><input name="password" type="text" maxlength="64" autocomplete="off">
    <label>${t("srv_description")}</label><input name="description" type="text" maxlength="500">
    <label class="check"><input name="public" type="checkbox" checked> ${t("srv_public")}</label>
    <label class="check"><input name="autoStart" type="checkbox"> ${t("srv_autostart")}</label>
    <label class="check"><input name="autoRestart" type="checkbox" checked> ${t("srv_autorestart")}</label>
    <div class="error" id="cerr"></div>
    <div class="dialog-actions"><button class="btn" value="cancel" formnovalidate>${t("cancel")}</button>
      <button class="btn primary" value="ok">${t("srv_create")}</button></div></form>`);
  document.body.appendChild(dlg);
  const f = $("#cf", dlg);
  f.addEventListener("submit", async (e) => {
    if (e.submitter?.value === "cancel") { dlg.close(); return; }
    e.preventDefault();
    try {
      const s = await api.post("/api/servers", {
        name: f.name.value, gamePort: +f.gamePort.value, telnetPort: +f.telnetPort.value, maxPlayers: +f.maxPlayers.value,
        password: f.password.value, description: f.description.value, branch: f.branch.value,
        public: f.public.checked, autoStart: f.autoStart.checked, autoRestart: f.autoRestart.checked,
      });
      dlg.close();
      location.hash = `#/server/${s.id}`;
    } catch (ex) { $("#cerr", dlg).textContent = errorText(ex.code); }
  });
  dlg.addEventListener("close", () => dlg.remove());
  dlg.showModal();
}

// ---------------------------------------------------------------- detail
export async function renderServerDetail(page, id, setTitle) {
  let tab = "overview";
  await loadServers();
  const get = () => store.servers.find((s) => s.id === id);
  if (!get()) { location.hash = "#/servers"; return; }
  const admin = atLeast(store.me.role, "admin");
  const owner = atLeast(store.me.role, "owner");

  const shell = () => {
    const s = get();
    if (!s) { location.hash = "#/servers"; return; }
    setTitle(s.name);
    mount(page, html`
      <div class="card server-top">
        <div class="server-head"><b class="big-name">${s.name}</b>${stateBadge(s)}</div>
        ${progressBar(s)}
        ${s.autoRestartSuspended ? html`<div class="error">${t("crash_loop")}</div>` : s.lastError ? html`<div class="error">${errorText(s.lastError)}</div>` : ""}
        <div class="actions-row" id="actions"></div>
      </div>
      <nav class="tabs">${["overview", ...(admin ? ["console", "settings", "logs"] : [])].map((k) =>
        html`<button type="button" data-tab="${k}" class="${k === tab ? "active" : ""}">${t("tab_" + k)}</button>`)}</nav>
      <div id="tab"></div>`);
    applyBars(page);
    drawActions(s);
    $$(".tabs button", page).forEach((b) => b.addEventListener("click", () => { tab = b.dataset.tab; shell(); }));
    drawTab(s);
  };

  const drawActions = (s) => {
    if (!admin) return;
    const busy = s.operation != null || busyStates.includes(s.state);
    const b = [];
    if (!s.installed && owner) b.push(["install", "act_install", "primary"]);
    if (s.installed && ["Stopped", "Crashed"].includes(s.state)) b.push(["start", "act_start", "primary"]);
    if (["Running", "Starting"].includes(s.state)) b.push(["stop", "act_stop", ""], ["restart", "act_restart", ""]);
    if (s.state === "Stopping" && s.operation) b.push(["cancel-stop", "act_cancel_stop", ""]);
    if (s.installed && ["Stopped", "Crashed"].includes(s.state)) b.push(["update", "act_update", ""], ["validate", "act_validate", ""]);
    if (["Running", "Starting", "Stopping", "Crashed"].includes(s.state)) b.push(["kill", "act_kill", "danger"]);
    if (s.autoRestartSuspended) b.push(["resume", "act_resume", ""]);
    if (owner && ["Stopped", "Crashed", "NotInstalled"].includes(s.state)) b.push(["rename", "act_rename", ""], ["delete", "act_delete", "danger"]);
    mount($("#actions", page), html`${b.map(([a, label, cls]) =>
      html`<button class="btn ${cls}" data-act="${a}" ${raw(busy && !["kill", "cancel-stop"].includes(a) ? "disabled" : "")}>${t(label)}</button>`)}`);
    $$("[data-act]", page).forEach((btn) => btn.addEventListener("click", () => act(btn.dataset.act, s)));
  };

  const act = async (a, s) => {
    try {
      if (a === "install" || a === "update") await api.post(`/api/servers/${id}/${a === "install" ? "validate" : "update"}`);
      else if (a === "validate") await api.post(`/api/servers/${id}/validate`);
      else if (a === "start") await api.post(`/api/servers/${id}/start`);
      else if (a === "stop" || a === "restart") stopDialog(s, a);
      else if (a === "cancel-stop") await api.post(`/api/servers/${id}/cancel-stop`);
      else if (a === "kill") { if (confirm(t("kill_confirm"))) await api.post(`/api/servers/${id}/kill`); }
      else if (a === "resume") { await api.post(`/api/servers/${id}/resume-auto-restart`); await loadServers(); }
      else if (a === "rename") renameDialog(s);
      else if (a === "delete") deleteDialog(s);
    } catch (ex) { fail(ex); }
  };

  const drawTab = (s) => {
    const el = $("#tab", page);
    if (tab === "overview") {
      mount(el, html`<div class="card"><dl class="kv">
        <dt>${t("folder")}</dt><dd>${s.rootDir}</dd>
        <dt>${t("ports")}</dt><dd>${s.gamePort}-${s.gamePort + 3} (UDP/TCP) · telnet ${s.telnetPort}</dd>
        <dt>${t("branch")}</dt><dd>${t(s.branch === "experimental" ? "branch_experimental" : "branch_public")}</dd>
        <dt>${t("build")}</dt><dd>${s.buildId || "—"}</dd>
        <dt>${t("srv_max_players")}</dt><dd>${s.maxPlayers ?? "—"}</dd>
        ${s.startedUtc ? html`<dt>${t("since")}</dt><dd>${new Date(s.startedUtc).toLocaleString()}</dd>` : ""}
      </dl></div>`);
    } else if (tab === "console") {
      if (el.dataset.tab === "console") return; // keep the console content between live updates
      mount(el, html`<div class="card"><div class="hint">${t("console_hint")}</div>
        <pre class="console" id="out"></pre>
        <form class="console-form" id="cform"><input name="cmd" type="text" autocomplete="off" maxlength="500"><button class="btn primary">${t("send")}</button></form></div>`);
      $("#cform", el).addEventListener("submit", async (e) => {
        e.preventDefault();
        const cmd = e.target.cmd.value.trim();
        if (!cmd) return;
        const out = $("#out", el);
        out.textContent += `> ${cmd}\n`;
        e.target.cmd.value = "";
        try { const r = await api.post(`/api/servers/${id}/console`, { command: cmd }); out.textContent += (r.output || "") + "\n"; }
        catch (ex) { out.textContent += `! ${errorText(ex.code)}\n`; }
        out.scrollTop = out.scrollHeight;
      });
    } else if (tab === "settings") {
      if (el.dataset.tab === "settings") return;
      mount(el, html`<form class="card" id="sf">
        <div class="form-row">
          <div><label>${t("srv_game_port")}</label><input name="gamePort" type="number" value="${s.gamePort}"></div>
          <div><label>${t("srv_telnet_port")}</label><input name="telnetPort" type="number" value="${s.telnetPort}"></div>
        </div>
        <div class="form-row">
          <div><label>${t("srv_max_players")}</label><input name="maxPlayers" type="number" min="1" max="200" value="${s.maxPlayers ?? 8}"></div>
          <div><label>${t("srv_branch")}</label><select name="branch">
            <option value="public" ${raw(s.branch === "public" ? "selected" : "")}>${t("branch_public")}</option>
            <option value="experimental" ${raw(s.branch === "experimental" ? "selected" : "")}>${t("branch_experimental")}</option></select></div>
        </div>
        <label>${t("srv_password")}</label><input name="password" type="text" maxlength="64" placeholder="${s.hasPassword ? "••••••" : ""}">
        <label>${t("srv_description")}</label><input name="description" type="text" maxlength="500" value="${s.description ?? ""}">
        <label class="check"><input name="public" type="checkbox" ${raw(s.isPublic ? "checked" : "")}> ${t("srv_public")}</label>
        <label class="check"><input name="autoStart" type="checkbox" ${raw(s.autoStart ? "checked" : "")}> ${t("srv_autostart")}</label>
        <label class="check"><input name="autoRestart" type="checkbox" ${raw(s.autoRestart ? "checked" : "")}> ${t("srv_autorestart")}</label>
        <button class="btn primary block">${t("save")}</button></form>`);
      $("#sf", el).addEventListener("submit", async (e) => {
        e.preventDefault();
        const f = e.target;
        try {
          const r = await api.put(`/api/servers/${id}/settings`, {
            gamePort: +f.gamePort.value, telnetPort: +f.telnetPort.value, maxPlayers: +f.maxPlayers.value,
            password: f.password.value === "" ? null : f.password.value, description: f.description.value, branch: f.branch.value,
            public: f.public.checked, autoStart: f.autoStart.checked, autoRestart: f.autoRestart.checked,
          });
          toast(r.appliesOnRestart ? t("applies_restart") : t("saved"));
          await loadServers();
        } catch (ex) { fail(ex); }
      });
    } else if (tab === "logs") {
      if (el.dataset.tab === "logs") return;
      mount(el, html`<div class="card">
        <div class="actions-row"><select id="lk"><option value="eam">${t("eam_log")}</option><option value="game">${t("game_log")}</option></select>
          <button class="btn" id="lr">${t("refresh")}</button></div>
        <pre class="console tall" id="lo"></pre></div>`);
      const load = async () => {
        try {
          const r = await api.get(`/api/servers/${id}/logs?kind=${$("#lk", el).value}&lines=400`);
          const out = $("#lo", el);
          out.textContent = r.lines.join("\n");
          out.scrollTop = out.scrollHeight;
        } catch (ex) { fail(ex); }
      };
      $("#lr", el).addEventListener("click", load);
      $("#lk", el).addEventListener("change", load);
      load();
    }
    el.dataset.tab = tab;
  };

  store.onChange = () => {
    // Live update: redraw the header and actions; keep forms the admin is typing in.
    const s = get();
    if (!s) { location.hash = "#/servers"; return; }
    const keep = $("#tab", page);
    const kept = keep?.dataset.tab === tab && tab !== "overview" ? keep : null;
    shell();
    if (kept) $("#tab", page).replaceWith(kept);
  };
  shell();
}

function stopDialog(s, mode) {
  const dlg = document.createElement("dialog");
  mount(dlg, html`<form method="dialog" id="sd">
    <h3>${t(mode === "restart" ? "restart_title" : "stop_title")}</h3>
    <label>${t("delay_minutes")}</label><input name="delay" type="number" min="0" max="60" value="${s.state === "Running" ? 5 : 0}">
    <label>${t("stop_message")}</label><input name="message" type="text" maxlength="120" value="${mode === "restart" ? "Redémarrage du serveur / Server restart" : "Arrêt du serveur / Server shutdown"}">
    <div class="hint">${t("stop_hint")}</div>
    <div class="dialog-actions"><button class="btn" value="cancel" formnovalidate>${t("cancel")}</button>
      <button class="btn primary" value="ok">${t(mode === "restart" ? "act_restart" : "act_stop")}</button></div></form>`);
  document.body.appendChild(dlg);
  const f = $("#sd", dlg);
  f.addEventListener("submit", async (e) => {
    if (e.submitter?.value === "cancel") { dlg.close(); return; }
    e.preventDefault();
    try { await api.post(`/api/servers/${s.id}/${mode}`, { delayMinutes: +f.delay.value, message: f.message.value }); dlg.close(); }
    catch (ex) { fail(ex); }
  });
  dlg.addEventListener("close", () => dlg.remove());
  dlg.showModal();
}

function renameDialog(s) {
  const dlg = document.createElement("dialog");
  mount(dlg, html`<form method="dialog" id="rd">
    <h3>${t("rename_title")}</h3>
    <label>${t("srv_name")}</label><input name="name" type="text" minlength="2" maxlength="60" required value="${s.name}">
    <div class="hint">${t("rename_hint")}</div><div class="error" id="rerr"></div>
    <div class="dialog-actions"><button class="btn" value="cancel" formnovalidate>${t("cancel")}</button>
      <button class="btn primary" value="ok">${t("save")}</button></div></form>`);
  document.body.appendChild(dlg);
  const f = $("#rd", dlg);
  f.addEventListener("submit", async (e) => {
    if (e.submitter?.value === "cancel") { dlg.close(); return; }
    e.preventDefault();
    try { await api.post(`/api/servers/${s.id}/rename`, { name: f.name.value }); dlg.close(); await loadServers(); }
    catch (ex) { $("#rerr", dlg).textContent = errorText(ex.code); }
  });
  dlg.addEventListener("close", () => dlg.remove());
  dlg.showModal();
}

function deleteDialog(s) {
  const dlg = document.createElement("dialog");
  mount(dlg, html`<form method="dialog" id="dd">
    <h3>${t("delete_title")} « ${s.name} »</h3>
    <label class="check"><input name="files" type="checkbox"> ${t("delete_files")}</label>
    <label>${t("delete_confirm")}</label><input name="confirm" type="text" autocomplete="off" required>
    <div class="error" id="derr"></div>
    <div class="dialog-actions"><button class="btn" value="cancel" formnovalidate>${t("cancel")}</button>
      <button class="btn danger" value="ok">${t("act_delete")}</button></div></form>`);
  document.body.appendChild(dlg);
  const f = $("#dd", dlg);
  f.addEventListener("submit", async (e) => {
    if (e.submitter?.value === "cancel") { dlg.close(); return; }
    e.preventDefault();
    try {
      await api.post(`/api/servers/${s.id}/delete`, { deleteFiles: f.files.checked, confirmName: f.confirm.value });
      dlg.close(); location.hash = "#/servers";
    } catch (ex) { $("#derr", dlg).textContent = errorText(ex.code); }
  });
  dlg.addEventListener("close", () => dlg.remove());
  dlg.showModal();
}

export function dashboardServers() { return store.servers; }
export function setServersListener(fn) { store.onChange = fn; }
