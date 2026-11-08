// Steps 4 and 5: players, backups, scheduler, every server and game option, scenarios.
import { api } from "./api.js";
import { t, extend, errorText, currentLanguage } from "./i18n.js";
import { html, raw, mount, $, $$, toast, formatBytes } from "./dom.js";

extend({
  fr: {
    tab_players: "Joueurs", tab_backups: "Sauvegardes", tab_schedule: "Planificateur", tab_options: "Options",
    // players
    pl_search: "Rechercher un joueur…", pl_online: "En ligne", pl_offline: "Hors ligne", pl_seen: "Vu pour la dernière fois",
    pl_first: "Première connexion", pl_playtime: "Temps de jeu", pl_none: "Aucun joueur vu sur ce serveur pour l'instant.",
    pl_banned: "Joueurs bannis", pl_back: "← Tous les joueurs", pl_role: "Rôle", role_player: "Joueur", role_gamemaster: "Maître du jeu",
    role_moderator: "Modérateur", role_admin: "Administrateur", pl_credits: "Crédits", pl_exp: "Expérience", pl_upgrade: "Points d'amélioration",
    pl_level: "Niveau", pl_health: "Santé", pl_pos: "Position", pl_factory: "Usine à plans", pl_inventory: "Inventaire", pl_toolbelt: "Barre d'outils",
    pl_bag: "Sac", pl_history: "Historique", pl_chat: "Messages du joueur", pl_pending: "{0} message(s) en attente de sa prochaine connexion",
    pl_offline_hint: "Le joueur n'est pas connecté : seules les actions hors ligne sont possibles (message, ban, rôle).",
    a_message: "Message", a_kick: "Expulser", a_ban: "Bannir", a_unban: "Débannir", a_credits: "Crédits", a_progress: "XP / points",
    a_item: "Donner un objet", a_teleport: "Téléporter", a_heal: "Soigner", a_bp: "Terminer le plan", a_role: "Changer le rôle",
    a_save_inv: "Enregistrer l'inventaire", a_remove: "Retirer",
    msg_offline_hint: "S'il est hors ligne, le message lui sera affiché à sa prochaine connexion.",
    kick_reason: "Raison (affichée au joueur)", ban_duration: "Durée", ban_hint: "Exemples : 30m, 12h, 7d, 1y",
    credits_mode: "Opération", credits_add: "Ajouter (négatif pour retirer)", credits_set: "Fixer à", amount: "Montant",
    item_search: "Chercher un objet (nom ou numéro)", count: "Quantité", item_id: "Numéro de l'objet",
    tp_playfield: "Playfield (laisser vide = le même)", tp_hint: "Le changement de playfield charge la planète : cela peut prendre un moment.",
    role_hint: "Écrit dans Saves\\adminconfig.yaml. Pris en compte au prochain redémarrage du serveur.",
    ev_join: "Connexion", ev_leave: "Déconnexion", ev_playfield: "Changement de playfield", ev_message_delivered: "Message hors ligne remis",
    done: "Fait.", queued: "Message enregistré : il sera remis à la prochaine connexion.", inv_changed: "Inventaire modifié : pensez à enregistrer.",
    // backups
    bk_create: "Créer une sauvegarde", bk_note: "Note (optionnelle)", bk_none: "Aucune sauvegarde pour l'instant.",
    bk_keep: "Sauvegardes automatiques gardées", bk_copy: "Copier aussi dans ce dossier (Google Drive, OneDrive…)",
    bk_copy_hint: "Dossier synchronisé de Google Drive ou OneDrive sur cette machine, par exemple C:\\Users\\Moi\\OneDrive\\Sauvegardes Empyrion. Laisser vide pour ne pas copier.",
    bk_copy_keep: "Sauvegardes gardées dans ce dossier", bk_settings: "Réglages des sauvegardes", bk_list: "Sauvegardes",
    bk_kind_manual: "Manuelle", bk_kind_scheduled: "Planifiée", "bk_kind_before-restore": "Avant restauration", "bk_kind_before-update": "Avant mise à jour",
    "bk_kind_before-wipe": "Avant wipe", bk_restore: "Restaurer", bk_download: "Télécharger", bk_running: "Sauvegarde en cours…",
    bk_restore_title: "Restaurer la sauvegarde", bk_restore_hint: "Le serveur doit être arrêté. EAM sauvegarde d'abord l'état actuel (« Avant restauration »), puis remet la partie, les plans des joueurs et adminconfig.yaml.",
    bk_restore_config: "Remettre aussi les options du serveur (dedicated) de cette sauvegarde", bk_restore_eam: "Remettre aussi les données EAM (chat, historique des joueurs, tâches)",
    bk_auto_hint: "Les sauvegardes manuelles ne sont jamais supprimées automatiquement.", bk_done: "Sauvegarde terminée : {0}", bk_restored: "Sauvegarde restaurée.",
    // scheduler
    sc_new: "Nouvelle tâche", sc_none: "Aucune tâche planifiée.", sc_kind: "Action", sc_when: "Quand", sc_next: "Prochaine", sc_last: "Dernière",
    sc_daily: "Tous les jours à heure fixe", sc_interval: "Toutes les N minutes", sc_times: "Heures (séparées par des virgules)", sc_days: "Jours (aucun coché = tous)",
    sc_minutes: "Intervalle (minutes)", sc_enabled: "Active", sc_run: "Lancer maintenant", sc_edit: "Modifier",
    k_restart: "Redémarrer", k_stop: "Arrêter", k_start: "Démarrer", k_backup: "Sauvegarder", k_message: "Message aux joueurs",
    k_command: "Commande console", k_wipe: "Wipe de playfields", k_cleanup: "Nettoyage des journaux", k_update: "Mise à jour du jeu (SteamCMD)",
    p_warn: "Prévenir les joueurs pendant (minutes)", p_message: "Message affiché", p_text: "Texte", p_mode: "Affichage", p_command: "Commande",
    p_playfields: "Playfields (séparés par des virgules)", p_wipe: "Que remettre à neuf", w_deposit: "Gisements", w_poi: "POI", w_terrain: "Terrain", w_all: "Tout",
    p_backup_first: "Sauvegarder avant", p_log_days: "Supprimer les journaux de plus de (jours)",
    wipe_hint: "Utilise la commande wipe du jeu sur les playfields indiqués (serveur en ligne). Le playfield est remis à neuf à son prochain chargement.",
    days_short: "Dim,Lun,Mar,Mer,Jeu,Ven,Sam", every_min: "toutes les {0} min",
    res_ok: "OK", res_running: "En cours", res_skipped_not_running: "Ignorée (serveur arrêté)", res_skipped_running: "Ignorée (déjà en ligne)", res_skipped_late: "Ignorée (EAM était arrêté)",
    // options
    op_game: "Partie", op_save: "Nom de la sauvegarde", op_save_hint: "Un nouveau nom crée une nouvelle partie au prochain démarrage. Les anciennes sauvegardes restent dans Saves\\Games.",
    op_scenario: "Scénario", op_mode: "Mode", op_seed: "Graine (seed)", op_scenarios: "Scénarios installés", op_server: "Options du serveur (dedicated)",
    op_gameopts: "Options de jeu de la sauvegarde « {0} »", op_gameopts_missing: "La sauvegarde « {0} » n'existe pas encore : démarrez le serveur une fois pour la créer, puis réglez ses options ici. Valeurs du scénario affichées pour information.",
    op_gameopts_hint: "Le serveur doit être arrêté pour changer les options de jeu (sinon le jeu les écrase).",
    op_filter: "Filtrer les options…", op_managed: "Géré par EAM (Réglages)", op_raw: "Modifier le fichier complet", op_raw_hint: "Pour les experts : le fichier dedicated-eam.yaml tel quel. Le nom, les ports et telnet restent ceux d'EAM.",
    op_install: "Installer / mettre à jour", op_installed: "Installé dans « {0} »", op_ws_hint: "Téléchargé depuis le Steam Workshop : toujours la dernière version.",
    op_folder_name: "Nom du dossier du scénario", op_use: "Utiliser ce scénario pour ce serveur", op_import: "Importer depuis un dossier",
    op_import_path: "Dossier du scénario sur cette machine", op_workshop_id: "Numéro Workshop", op_workshop_other: "Autre scénario du Workshop",
    op_current: "utilisé", op_stock: "fourni avec le jeu", op_saved_restart: "Enregistré. Appliqué au prochain démarrage.",
    op_job_done: "Scénario installé.", op_on: "activée", progress_download_workshop: "Téléchargement du scénario", progress_copy_scenario: "Copie du scénario",
    progress_backup_before_restore: "Sauvegarde avant restauration", progress_restoring: "Restauration",
    err_backup_running: "Une sauvegarde est déjà en cours.", err_backup_failed: "La sauvegarde a échoué.", err_backup_not_found: "Sauvegarde introuvable.",
    err_copy_dir_not_absolute: "Indiquez un chemin complet (ex. D:\\Sauvegardes).", err_copy_dir_unusable: "Ce dossier n'est pas utilisable.", err_invalid_keep: "Nombre invalide.",
    err_invalid_task_kind: "Action inconnue.", err_invalid_schedule: "Horaire invalide.", err_invalid_times: "Heures invalides (format 04:00).", err_invalid_days: "Jours invalides.",
    err_invalid_interval: "Intervalle : 5 minutes minimum.", err_invalid_wipe: "Choisissez quoi remettre à neuf.", err_invalid_playfields: "Indiquez au moins un playfield.",
    err_task_not_found: "Tâche introuvable.", err_task_running: "Cette tâche est déjà en cours.",
    err_save_not_created: "La sauvegarde n'existe pas encore : démarrez le serveur une fois.", err_invalid_game_name: "Nom de sauvegarde invalide.",
    err_invalid_scenario_name: "Nom de dossier invalide.", err_scenario_not_found: "Aucun scénario trouvé dans ce dossier.", err_folder_not_found: "Dossier introuvable.",
    err_scenario_in_use: "Ce scénario est utilisé par le serveur.", err_workshop_download_failed: "Téléchargement Workshop impossible.", err_scenario_install_failed: "Installation du scénario impossible.",
    err_invalid_yaml: "Fichier invalide.", err_invalid_key: "Nom d'option invalide.", err_invalid_value: "Valeur invalide.", err_invalid_section: "Section invalide.",
    err_player_not_found: "Joueur inconnu.", err_player_offline: "Le joueur doit être connecté pour cette action.", err_game_timeout: "Le jeu n'a pas répondu à temps.",
    err_game_error: "Le jeu a refusé l'action.", err_invalid_amount: "Montant invalide.", err_invalid_item: "Objet ou quantité invalide.", err_invalid_duration: "Durée invalide (ex. 7d).",
    err_invalid_steam_id: "SteamID invalide.", err_invalid_permission: "Rôle invalide.", err_unknown_action: "Action inconnue.", err_forbidden: "Votre rôle ne permet pas cette action.",
    err_step_failed: "Une étape a échoué.", err_io_error: "Erreur de fichier.",
  },
  en: {
    tab_players: "Players", tab_backups: "Backups", tab_schedule: "Scheduler", tab_options: "Options",
    pl_search: "Search a player…", pl_online: "Online", pl_offline: "Offline", pl_seen: "Last seen", pl_first: "First seen", pl_playtime: "Play time",
    pl_none: "No player seen on this server yet.", pl_banned: "Banned players", pl_back: "← All players", pl_role: "Role", role_player: "Player",
    role_gamemaster: "Game master", role_moderator: "Moderator", role_admin: "Admin", pl_credits: "Credits", pl_exp: "Experience", pl_upgrade: "Upgrade points",
    pl_level: "Level", pl_health: "Health", pl_pos: "Position", pl_factory: "Blueprint factory", pl_inventory: "Inventory", pl_toolbelt: "Toolbelt",
    pl_bag: "Bag", pl_history: "History", pl_chat: "Player's messages", pl_pending: "{0} message(s) waiting for the next login",
    pl_offline_hint: "The player is not connected: only offline actions are possible (message, ban, role).",
    a_message: "Message", a_kick: "Kick", a_ban: "Ban", a_unban: "Unban", a_credits: "Credits", a_progress: "XP / points", a_item: "Give item",
    a_teleport: "Teleport", a_heal: "Heal", a_bp: "Finish blueprint", a_role: "Change role", a_save_inv: "Save inventory", a_remove: "Remove",
    msg_offline_hint: "If the player is offline, the message is shown at their next login.",
    kick_reason: "Reason (shown to the player)", ban_duration: "Duration", ban_hint: "Examples: 30m, 12h, 7d, 1y",
    credits_mode: "Operation", credits_add: "Add (negative to remove)", credits_set: "Set to", amount: "Amount",
    item_search: "Search an item (name or number)", count: "Count", item_id: "Item number",
    tp_playfield: "Playfield (empty = same)", tp_hint: "Changing playfield loads the planet: it can take a moment.",
    role_hint: "Written to Saves\\adminconfig.yaml. Applied at the next server restart.",
    ev_join: "Login", ev_leave: "Logout", ev_playfield: "Playfield change", ev_message_delivered: "Offline message delivered",
    done: "Done.", queued: "Message saved: it will be shown at the next login.", inv_changed: "Inventory changed: remember to save.",
    bk_create: "Create a backup", bk_note: "Note (optional)", bk_none: "No backup yet.", bk_keep: "Automatic backups kept",
    bk_copy: "Also copy to this folder (Google Drive, OneDrive…)", bk_copy_hint: "Synchronised Google Drive or OneDrive folder on this machine. Leave empty to not copy.",
    bk_copy_keep: "Backups kept in that folder", bk_settings: "Backup settings", bk_list: "Backups", bk_kind_manual: "Manual", bk_kind_scheduled: "Scheduled",
    "bk_kind_before-restore": "Before restore", "bk_kind_before-update": "Before update", "bk_kind_before-wipe": "Before wipe", bk_restore: "Restore", bk_download: "Download",
    bk_running: "Backup running…", bk_restore_title: "Restore the backup",
    bk_restore_hint: "The server must be stopped. EAM first backs up the current state (\"Before restore\"), then puts back the save, the players' blueprints and adminconfig.yaml.",
    bk_restore_config: "Also restore the server options (dedicated) of this backup", bk_restore_eam: "Also restore EAM's data (chat, player history, tasks)",
    bk_auto_hint: "Manual backups are never removed automatically.", bk_done: "Backup done: {0}", bk_restored: "Backup restored.",
    sc_new: "New task", sc_none: "No scheduled task.", sc_kind: "Action", sc_when: "When", sc_next: "Next", sc_last: "Last",
    sc_daily: "Every day at fixed times", sc_interval: "Every N minutes", sc_times: "Times (comma separated)", sc_days: "Days (none = every day)",
    sc_minutes: "Interval (minutes)", sc_enabled: "Enabled", sc_run: "Run now", sc_edit: "Edit",
    k_restart: "Restart", k_stop: "Stop", k_start: "Start", k_backup: "Backup", k_message: "Message to players", k_command: "Console command",
    k_wipe: "Playfield wipe", k_cleanup: "Log cleanup", k_update: "Game update (SteamCMD)",
    p_warn: "Warn players for (minutes)", p_message: "Message shown", p_text: "Text", p_mode: "Display", p_command: "Command",
    p_playfields: "Playfields (comma separated)", p_wipe: "What to reset", w_deposit: "Deposits", w_poi: "POIs", w_terrain: "Terrain", w_all: "Everything",
    p_backup_first: "Back up first", p_log_days: "Delete logs older than (days)",
    wipe_hint: "Uses the game's wipe command on these playfields (server online). The playfield is reset at its next load.",
    days_short: "Sun,Mon,Tue,Wed,Thu,Fri,Sat", every_min: "every {0} min",
    res_ok: "OK", res_running: "Running", res_skipped_not_running: "Skipped (server stopped)", res_skipped_running: "Skipped (already online)", res_skipped_late: "Skipped (EAM was not running)",
    op_game: "Game", op_save: "Save game name", op_save_hint: "A new name creates a new game at the next start. Old saves stay in Saves\\Games.",
    op_scenario: "Scenario", op_mode: "Mode", op_seed: "Seed", op_scenarios: "Installed scenarios", op_server: "Server options (dedicated)",
    op_gameopts: "Game options of save \"{0}\"", op_gameopts_missing: "Save \"{0}\" does not exist yet: start the server once to create it, then set its options here. Scenario values shown for information.",
    op_gameopts_hint: "The server must be stopped to change game options (otherwise the game overwrites them).",
    op_filter: "Filter options…", op_managed: "Managed by EAM (Settings)", op_raw: "Edit the whole file", op_raw_hint: "For experts: dedicated-eam.yaml as is. Name, ports and telnet stay EAM's.",
    op_install: "Install / update", op_installed: "Installed in \"{0}\"", op_ws_hint: "Downloaded from the Steam Workshop: always the latest version.",
    op_folder_name: "Scenario folder name", op_use: "Use this scenario for this server", op_import: "Import from a folder",
    op_import_path: "Scenario folder on this machine", op_workshop_id: "Workshop number", op_workshop_other: "Another Workshop scenario",
    op_current: "in use", op_stock: "shipped with the game", op_saved_restart: "Saved. Applied at the next start.",
    op_job_done: "Scenario installed.", op_on: "on", progress_download_workshop: "Downloading the scenario", progress_copy_scenario: "Copying the scenario",
    progress_backup_before_restore: "Backup before restore", progress_restoring: "Restoring",
    err_backup_running: "A backup is already running.", err_backup_failed: "The backup failed.", err_backup_not_found: "Backup not found.",
    err_copy_dir_not_absolute: "Give a full path (e.g. D:\\Backups).", err_copy_dir_unusable: "This folder cannot be used.", err_invalid_keep: "Invalid number.",
    err_invalid_task_kind: "Unknown action.", err_invalid_schedule: "Invalid schedule.", err_invalid_times: "Invalid times (format 04:00).", err_invalid_days: "Invalid days.",
    err_invalid_interval: "Interval: 5 minutes minimum.", err_invalid_wipe: "Choose what to reset.", err_invalid_playfields: "Give at least one playfield.",
    err_task_not_found: "Task not found.", err_task_running: "This task is already running.",
    err_save_not_created: "The save does not exist yet: start the server once.", err_invalid_game_name: "Invalid save name.",
    err_invalid_scenario_name: "Invalid folder name.", err_scenario_not_found: "No scenario found in this folder.", err_folder_not_found: "Folder not found.",
    err_scenario_in_use: "This scenario is used by the server.", err_workshop_download_failed: "Workshop download failed.", err_scenario_install_failed: "Scenario installation failed.",
    err_invalid_yaml: "Invalid file.", err_invalid_key: "Invalid option name.", err_invalid_value: "Invalid value.", err_invalid_section: "Invalid section.",
    err_player_not_found: "Unknown player.", err_player_offline: "The player must be online for this action.", err_game_timeout: "The game did not answer in time.",
    err_game_error: "The game refused the action.", err_invalid_amount: "Invalid amount.", err_invalid_item: "Invalid item or count.", err_invalid_duration: "Invalid duration (e.g. 7d).",
    err_invalid_steam_id: "Invalid SteamID.", err_invalid_permission: "Invalid role.", err_unknown_action: "Unknown action.", err_forbidden: "Your role does not allow this action.",
    err_step_failed: "A step failed.", err_io_error: "File error.",
  },
});

const ROLES = ["owner", "admin", "gamemaster", "moderator", "viewer"];
const atLeast = (role, required) => ROLES.indexOf(role) <= ROLES.indexOf(required);
const fail = (ex) => toast(errorText(ex.code) + (ex.body?.detail && errorText(ex.code) === t("err_generic") ? ` (${ex.body.detail})` : ""), true);
const when = (utc) => (utc ? new Date(utc).toLocaleString([], { dateStyle: "short", timeStyle: "short" }) : "—");
const duration = (sec) => { const h = Math.floor(sec / 3600); return h ? `${h} h ${Math.floor((sec % 3600) / 60)} min` : `${Math.floor(sec / 60)} min`; };

const tools = { listener: null };

/** Live socket messages for these screens. Returns true when handled. */
export function toolsEvent(msg) {
  if (["backup_progress", "tasks", "job"].includes(msg.type)) {
    if (msg.type === "job" && msg.job === "scenario") {
      if (msg.ok) toast(t("op_job_done")); else toast(errorText(msg.error) + (msg.detail ? ` (${msg.detail})` : ""), true);
    }
    if (msg.type === "backup_progress" && msg.done) toast(t("bk_done", msg.done));
    if (msg.type === "backup_progress" && msg.error) toast(`${t("err_backup_failed")} (${msg.error})`, true);
    tools.listener?.(msg);
    return true;
  }
  return false;
}

export const toolTabs = (role) => [
  ...(atLeast(role, "moderator") ? ["players"] : []),
  ...(atLeast(role, "admin") ? ["backups", "schedule", "options"] : []),
];

export function renderTool(tab, el, s, me) {
  tools.listener = null;
  if (tab === "players") return renderPlayers(el, s, me);
  if (tab === "backups") return renderBackups(el, s, me);
  if (tab === "schedule") return renderSchedule(el, s, me);
  if (tab === "options") return renderOptions(el, s, me);
}

/** Small modal form. `build` returns the html of the fields; `submit(form)` returns true to close. */
function dialog(title, fields, submit, okLabel = t("save"), danger = false) {
  const dlg = document.createElement("dialog");
  mount(dlg, html`<form method="dialog" class="dlg"><h3>${title}</h3>${fields}<div class="error" id="derr"></div>
    <div class="dialog-actions"><button class="btn" value="cancel" formnovalidate>${t("cancel")}</button>
    <button class="btn ${danger ? "danger" : "primary"}" value="ok">${okLabel}</button></div></form>`);
  document.body.appendChild(dlg);
  const f = $("form", dlg);
  f.addEventListener("submit", async (e) => {
    if (e.submitter?.value === "cancel") { dlg.close(); return; }
    e.preventDefault();
    const btn = e.submitter;
    if (btn) btn.disabled = true;
    try { if ((await submit(f)) !== false) dlg.close(); }
    catch (ex) { $("#derr", dlg).textContent = errorText(ex.code) + (ex.body?.detail ? ` (${ex.body.detail})` : ""); }
    if (btn) btn.disabled = false;
  });
  dlg.addEventListener("close", () => dlg.remove());
  dlg.showModal();
  return dlg;
}

// =================================================================== players
let itemCache = { id: null, items: [] };
async function items(id) {
  if (itemCache.id === id && itemCache.items.length) return itemCache.items;
  try { itemCache = { id, items: await api.get(`/api/servers/${id}/players/items?lang=${currentLanguage()}`) }; } catch { itemCache = { id, items: [] }; }
  return itemCache.items;
}
const itemLabel = (list, id) => list.find((i) => i.id === id)?.label ?? `#${id}`;

async function renderPlayers(el, s, me) {
  if (window.__eamOpenPlayer) {
    // Opened from the overview: go straight to this player's sheet.
    const steam = window.__eamOpenPlayer;
    window.__eamOpenPlayer = null;
    return renderPlayer(el, s, me, steam, () => renderPlayers(el, s, me));
  }
  let filter = "";
  let list = [];
  const draw = () => {
    const q = filter.toLowerCase();
    const shown = list.filter((p) => !q || p.name.toLowerCase().includes(q) || p.steamId.includes(q))
      .sort((a, b) => (b.online ? 1 : 0) - (a.online ? 1 : 0));
    const tbody = $("#pl-body", el);
    if (!tbody) return;
    mount(tbody, html`${shown.map((p) => html`<tr class="clickable ${p.online ? "row-online" : ""}" data-steam="${p.steamId}">
      <td><b>${p.name}</b>${p.marked ? html`<span class="mark" title="${t("pm_marked")}">★</span>` : ""}<div class="hint">${p.steamId}</div></td>
      <td>${p.online ? html`<span class="state ok">${t("pl_online")}</span> <span class="hint">${p.playfield || ""}</span>` : html`<span class="hint">${when(p.lastSeenUtc)}</span>`}</td>
      <td>${t("role_" + p.role)}</td><td>${duration(p.playtimeSeconds)}</td></tr>`)}`);
    $$("tr[data-steam]", tbody).forEach((tr) => tr.addEventListener("click", () => renderPlayer(el, s, me, tr.dataset.steam, () => renderPlayers(el, s, me))));
  };
  mount(el, html`<div class="card">
    <div class="actions-row"><input id="pl-q" type="search" placeholder="${t("pl_search")}" autocomplete="off">
      <button class="btn" id="pl-banned">${t("pl_banned")}</button>
      ${atLeast(me.role, "moderator") ? html`<button class="btn" id="pl-factions">${t("fa_title")}</button>` : ""}
      ${atLeast(me.role, "gamemaster") ? html`<button class="btn" id="pl-inv">${t("ih_all")}</button>` : ""}
      ${atLeast(me.role, "moderator") ? html`<button class="btn danger" id="pl-kickall">${t("pm_kick_all")}</button>` : ""}</div>
    <div class="hint" id="pl-count"></div>
    <div class="table-wrap"><table><thead><tr><th>${t("col_name")}</th><th>${t("pl_seen")}</th><th>${t("pl_role")}</th><th>${t("pl_playtime")}</th></tr></thead>
    <tbody id="pl-body"></tbody></table></div><div id="pl-empty"></div></div>`);
  $("#pl-q", el).addEventListener("input", (e) => { filter = e.target.value; draw(); });
  $("#pl-banned", el).addEventListener("click", () => bannedDialog(s, me));
  $("#pl-factions", el)?.addEventListener("click", () => factionsDialog(s));
  $("#pl-inv", el)?.addEventListener("click", () => inventoryHistoryDialog(s, null, me));
  $("#pl-kickall", el)?.addEventListener("click", () => dialog(t("pm_kick_all"), html`<label>${t("kick_reason")}</label><input name="reason" maxlength="120">`,
    async (f) => { const r = await api.post(`/api/servers/${s.id}/players/kick-all`, { name: f.reason.value || "Server maintenance" }); toast(t("pm_kicked", r.kicked)); }, t("a_kick"), true));
  try { list = await api.get(`/api/servers/${s.id}/players`); } catch (ex) { fail(ex); }
  if (!list.length) mount($("#pl-empty", el), html`<div class="hint">${t("pl_none")}</div>`);
  else $("#pl-count", el).textContent = t("pm_count", list.filter((p) => p.online).length, list.length);
  draw();
}

async function bannedDialog(s, me) {
  let list = [], listError = null;
  try { list = await api.get(`/api/servers/${s.id}/players/banned`); } catch (ex) { listError = errorText(ex.code); }
  const mod = atLeast(me.role, "moderator");
  const ban = (body) => api.post(`/api/servers/${s.id}/players/ban-steam`, body);
  const dlg = dialog(t("pl_banned"), html`${list.length ? html`<div class="table-wrap"><table><tbody>${list.map((b) => html`<tr>
      <td><b>${b.name || "?"}</b><div class="hint">${b.steamId}</div></td><td>${when(b.untilUtc)}</td>
      <td>${mod ? html`<button type="button" class="btn small" data-unban="${b.steamId}">${t("a_unban")}</button>` : ""}</td></tr>`)}</tbody></table></div>`
    : html`<div class="hint">${listError || "—"}</div>`}
    ${mod ? html`<h4>${t("bs_title")}</h4><label>SteamID</label><input name="steam" inputmode="numeric" pattern="[0-9]{17}" maxlength="17" placeholder="7656119…">
      <label>${t("bs_duration")}</label><input name="duration" value="1d" maxlength="5"><div class="hint">${t("bs_duration_hint")}</div>` : ""}`,
    mod ? async (f) => {
      if (!f.steam.value.trim()) return true;
      await ban({ steamId: f.steam.value.trim(), duration: f.duration.value.trim(), unban: false });
      toast(t("done"));
    } : () => true, mod ? t("a_ban") : "OK", mod);
  dlg.querySelectorAll("[data-unban]").forEach((b) => b.addEventListener("click", async () => {
    b.disabled = true;
    try { await ban({ steamId: b.dataset.unban, unban: true }); toast(t("done")); b.closest("tr").remove(); } catch (ex) { fail(ex); b.disabled = false; }
  }));
}

async function factionsDialog(s) {
  let list = [];
  try { list = await api.get(`/api/servers/${s.id}/factions`); } catch (ex) { fail(ex); return; }
  dialog(t("fa_title"), list.length ? html`<div class="table-wrap"><table>
      <thead><tr><th>${t("fa_abbrev")}</th><th>${t("col_name")}</th><th>${t("fa_online")}</th></tr></thead>
      <tbody>${list.map((f) => html`<tr><td><b>${f.abbrev || "—"}</b><div class="hint">#${f.id}</div></td><td>${f.name || "—"}</td>
        <td>${f.online.length ? f.online.map((p) => p.name).join(", ") : "—"}</td></tr>`)}</tbody></table></div>`
    : html`<div class="hint">${t("fa_none")}</div>`, () => true, "OK");
}

async function renderPlayer(el, s, me, steamId, back) {
  let p;
  try { p = await api.get(`/api/servers/${s.id}/players/${steamId}`); } catch (ex) { fail(ex); return; }
  const info = p.info;
  const gm = atLeast(me.role, "gamemaster");
  const admin = atLeast(me.role, "admin");
  const catalog = info ? await items(s.id) : [];
  const inv = info ? { toolbelt: [...(info.toolbelt || [])], bag: [...(info.bag || [])] } : null;
  let invDirty = false;

  const act = async (action, body = {}) => {
    const r = await api.post(`/api/servers/${s.id}/players/${steamId}/action`, { action, ...body });
    toast(r?.queued ? t("queued") : r?.appliesOnRestart ? t("role_hint") : t("done"));
    renderPlayer(el, s, me, steamId, back);
  };

  const stackRows = (key) => html`${inv[key].map((st, i) => html`<tr><td>${itemLabel(catalog, st.id)} <span class="hint">#${st.id}</span></td>
      <td>${admin ? html`<input type="number" min="1" max="100000" class="small" data-inv="${key}" data-i="${i}" value="${st.count}">` : st.count}</td>
      <td>${admin ? html`<button class="btn small" data-rm="${key}" data-i="${i}">${t("a_remove")}</button>` : ""}</td></tr>`)}`;

  const buttons = [
    ["message", "a_message", "moderator", false], ["kick", "a_kick", "moderator", true], ["ban", "a_ban", "moderator", false],
    ["unban", "a_unban", "moderator", false], ["credits", "a_credits", "gamemaster", true], ["progress", "a_progress", "gamemaster", true],
    ["item", "a_item", "gamemaster", true], ["teleport", "a_teleport", "gamemaster", true], ["heal", "a_heal", "gamemaster", true],
    ["bp", "a_bp", "gamemaster", true], ["role", "a_role", "admin", false],
    ["god", p.meta?.god ? "pm_god_off" : "pm_god_on", "gamemaster", false], ["tp_more", "pm_tp_more", "gamemaster", true],
    ["faction", "pm_faction", "admin", true], ["bp_res", "pm_bp_res", "gamemaster", true], ["survival", "pm_survival_reset", "gamemaster", false],
    ["playtime", "pm_playtime_reset", "admin", false], ["files", "pm_files", "admin", false], ["impersonate", "pm_impersonate", "admin", true],
  ].filter(([, , role, needsOnline]) => atLeast(me.role, role) && (!needsOnline || p.online));

  mount(el, html`<div class="card">
    <div class="actions-row"><button class="btn" id="pl-back">${t("pl_back")}</button></div>
    <div class="server-head"><b class="big-name">${p.name}</b>${p.online ? html`<span class="state ok">${t("pl_online")}</span>` : html`<span class="state">${t("pl_offline")}</span>`}</div>
    <dl class="kv"><dt>SteamID</dt><dd>${p.steamId} · <a href="https://steamcommunity.com/profiles/${p.steamId}" target="_blank" rel="noopener">${t("pl_steam_profile")}</a></dd>
      <dt>${t("pl_role")}</dt><dd>${t("role_" + ({ 9: "admin", 6: "moderator", 3: "gamemaster" }[p.permission] || "player"))}</dd>
      <dt>${t("pl_first")}</dt><dd>${when(p.firstSeenUtc)}</dd><dt>${t("pl_seen")}</dt><dd>${when(p.lastSeenUtc)}</dd>
      <dt>${t("pl_playtime")}</dt><dd>${duration(p.playtimeSeconds)}</dd>
      ${info ? html`<dt>${t("pl_credits")}</dt><dd>${Math.round(info.credits).toLocaleString()}</dd>
        <dt>${t("pl_level")}</dt><dd>${info.upgrade} · ${t("pl_exp")} ${info.exp}</dd>
        <dt>${t("pl_health")}</dt><dd>${Math.round(info.health)} / ${Math.round(info.healthMax)}</dd>
        <dt>${t("col_playfield")}</dt><dd>${info.pf}</dd><dt>${t("pl_pos")}</dt><dd>${Math.round(info.x)}, ${Math.round(info.y)}, ${Math.round(info.z)}</dd>
        ${info.bpInFactory ? html`<dt>${t("pl_factory")}</dt><dd>${info.bpInFactory} (${Math.round(info.bpRemaining || 0)} s)</dd>` : ""}` : ""}
    </dl>
    ${p.pendingMessages ? html`<div class="hint">${t("pl_pending", p.pendingMessages)}</div>` : ""}
    ${!p.online ? html`<div class="hint">${t("pl_offline_hint")}</div>` : ""}
    ${p.liveError ? html`<div class="error">${errorText(p.liveError)}</div>` : ""}
    <div class="actions-row wrap">${buttons.map(([a, label]) => html`<button class="btn" data-a="${a}">${t(label)}</button>`)}</div>
  </div>
  <div class="card"><h3>${t("pm_notes")}</h3>
    <textarea id="pm-note" rows="2" maxlength="2000" ${raw(atLeast(me.role, "moderator") ? "" : "disabled")}>${p.meta?.note ?? ""}</textarea>
    <div class="actions-row wrap"><label class="check"><input type="checkbox" id="pm-marked" ${raw(p.meta?.marked ? "checked" : "")}> ${t("pm_marked")}</label>
      <label class="check"><input type="checkbox" id="pm-exempt" ${raw(p.meta?.banExempt ? "checked" : "")}> ${t("pm_exempt")}</label>
      ${p.meta?.god ? html`<span class="state ok">${t("pm_god")}</span>` : ""}
      ${atLeast(me.role, "moderator") ? html`<button class="btn small" id="pm-save">${t("save")}</button>` : ""}</div></div>
  ${atLeast(me.role, "gamemaster") ? html`<div class="card"><h3>${t("ih_title")}</h3><div class="hint">${t("ih_hint")}</div>
    <div class="actions-row wrap">${p.online ? html`<button class="btn" id="ih-now">${t("ih_now")}</button>` : ""}
      ${admin && p.online ? html`<button class="btn" id="ih-tpl-load">${t("ih_tpl_load")}</button><button class="btn" id="ih-tpl-save">${t("ih_tpl_save")}</button>` : ""}</div>
    ${p.inventories?.length ? html`<div class="table-wrap"><table><tbody>${p.inventories.map((x) => html`<tr>
      <td>${when(x.utc)}</td><td><span class="badge">${t("ih_r_" + x.reason) === "ih_r_" + x.reason ? x.reason : t("ih_r_" + x.reason)}</span></td>
      <td>${x.playfield || ""}</td><td>${t("ih_items", x.items)}</td>
      <td class="actions-row"><button class="btn small" data-ih-view="${x.id}">${t("ih_view")}</button></td></tr>`)}</tbody></table></div>`
      : html`<div class="hint">${t("ih_none")}</div>`}</div>` : ""}
  ${inv ? html`<div class="card"><h3>${t("pl_inventory")}</h3>
    <h4>${t("pl_toolbelt")}</h4><div class="table-wrap"><table><tbody id="inv-toolbelt">${stackRows("toolbelt")}</tbody></table></div>
    <h4>${t("pl_bag")}</h4><div class="table-wrap"><table><tbody id="inv-bag">${stackRows("bag")}</tbody></table></div>
    ${admin ? html`<button class="btn primary" id="inv-save" disabled>${t("a_save_inv")}</button>` : ""}</div>` : ""}
  <div class="card"><h3>${t("pl_history")}</h3><div class="history">${p.events.map((e) => html`<div class="chat-line">
    <span class="chat-time">${when(e.utc)}</span><b>${t("ev_" + e.kind) === "ev_" + e.kind ? (e.kind.startsWith("admin_") ? `${e.by || ""} → ${e.kind.slice(6)}` : e.kind) : t("ev_" + e.kind)}</b>
    <span class="chat-text">${e.detail || ""}</span></div>`)}</div></div>
  <div class="card"><h3>${t("pl_chat")}</h3><div class="history">${p.chat.map((m) => html`<div class="chat-line">
    <span class="chat-time">${when(m.utc)}</span><span class="badge">${m.channel}</span> <span class="chat-text">${m.text}</span></div>`)}</div></div>`);

  $("#pl-back", el).addEventListener("click", back);
  $("#pm-save", el)?.addEventListener("click", () => act("meta", { note: $("#pm-note", el).value, marked: $("#pm-marked", el).checked, banExempt: $("#pm-exempt", el).checked }).catch(fail));
  $("#ih-now", el)?.addEventListener("click", () => act("inventory_snapshot").catch(fail));
  $$("[data-ih-view]", el).forEach((b) => b.addEventListener("click", () => snapshotDialog(s, +b.dataset.ihView, steamId, p.online, admin, catalog.length ? catalog : null, () => renderPlayer(el, s, me, steamId, back))));
  $("#ih-tpl-save", el)?.addEventListener("click", () => dialog(t("ih_tpl_save"), html`<label>${t("col_name")}</label><input name="n" maxlength="40" required autofocus>`,
    (f) => act("inventory_template_save", { name: f.n.value })));
  $("#ih-tpl-load", el)?.addEventListener("click", async () => {
    let tpls = [];
    try { tpls = await api.get(`/api/servers/${s.id}/inventory-templates`); } catch (ex) { fail(ex); return; }
    dialog(t("ih_tpl_load"), tpls.length ? html`<select name="n">${tpls.map((x) => html`<option>${x.name}</option>`)}</select><div class="hint">${t("ih_replace_hint")}</div>`
      : html`<div class="hint">${t("ih_tpl_none")}</div>`, (f) => (tpls.length ? act("inventory_template_load", { name: f.n.value }) : true), t("ih_restore"));
  });
  const markDirty = () => { invDirty = true; const b = $("#inv-save", el); if (b) b.disabled = false; };
  $$("[data-inv]", el).forEach((i) => i.addEventListener("change", () => { inv[i.dataset.inv][+i.dataset.i].count = Math.max(1, +i.value || 1); markDirty(); }));
  $$("[data-rm]", el).forEach((b) => b.addEventListener("click", () => {
    inv[b.dataset.rm].splice(+b.dataset.i, 1);
    b.closest("tr").remove();
    markDirty();
    toast(t("inv_changed"));
  }));
  $("#inv-save", el)?.addEventListener("click", async () => {
    if (!invDirty) return;
    try { await act("inventory_set", { toolbelt: inv.toolbelt, bag: inv.bag }); } catch (ex) { fail(ex); }
  });

  const handlers = {
    message: () => dialog(t("a_message"), html`<label>${t("p_text")}</label><input name="text" maxlength="300" required autofocus>
      <div class="hint">${t("msg_offline_hint")}</div>`, (f) => act("message", { text: f.text.value })),
    kick: () => dialog(t("a_kick"), html`<label>${t("kick_reason")}</label><input name="reason" maxlength="120">`,
      (f) => act("kick", { reason: f.reason.value }), t("a_kick"), true),
    ban: () => dialog(t("a_ban"), html`<label>${t("ban_duration")}</label><input name="duration" value="7d" pattern="\\d{1,4}[mhdy]" required>
      <div class="hint">${t("ban_hint")}</div><label>${t("kick_reason")}</label><input name="reason" maxlength="200">`,
      (f) => act("ban", { duration: f.duration.value, reason: f.reason.value }), t("a_ban"), true),
    unban: () => act("unban").catch(fail),
    credits: () => dialog(t("a_credits"), html`<label>${t("credits_mode")}</label><select name="mode">
        <option value="credits_add">${t("credits_add")}</option><option value="credits_set">${t("credits_set")}</option></select>
      <label>${t("amount")}</label><input name="amount" type="number" step="1" required autofocus>`,
      (f) => act(f.mode.value, { amount: +f.amount.value })),
    progress: () => dialog(t("a_progress"), html`<div class="form-row">
      <div><label>${t("pl_exp")}</label><input name="exp" type="number" min="0" value="${info?.exp ?? 0}"></div>
      <div><label>${t("pl_upgrade")}</label><input name="upgrade" type="number" min="0" value="${info?.upgrade ?? 0}"></div></div>`,
      (f) => act("progress", { exp: +f.exp.value, upgrade: +f.upgrade.value })),
    item: () => {
      const dlg = dialog(t("a_item"), html`<label>${t("item_search")}</label><input name="q" type="search" autocomplete="off" autofocus>
        <select name="item" size="8" class="picker" required></select>
        <label>${t("count")}</label><input name="count" type="number" min="1" max="100000" value="1" required>`,
        (f) => act("item_add", { itemId: +f.item.value, count: +f.count.value }));
      const f = $("form", dlg);
      const fill = () => {
        const q = f.q.value.trim().toLowerCase();
        const found = catalog.filter((i) => !q || i.label.toLowerCase().includes(q) || i.name.toLowerCase().includes(q) || String(i.id) === q).slice(0, 200);
        mount(f.item, html`${found.map((i) => html`<option value="${i.id}">${i.label} (#${i.id})</option>`)}
          ${/^\d+$/.test(q) && !found.length ? html`<option value="${q}">#${q}</option>` : ""}`);
        if (f.item.options.length) f.item.selectedIndex = 0;
      };
      f.q.addEventListener("input", fill);
      fill();
    },
    teleport: () => dialog(t("a_teleport"), html`<label>${t("tp_playfield")}</label><input name="pf" value="${info?.pf ?? ""}">
      <div class="form-row three"><div><label>X</label><input name="x" type="number" value="${Math.round(info?.x ?? 0)}"></div>
      <div><label>Y</label><input name="y" type="number" value="${Math.round(info?.y ?? 0)}"></div>
      <div><label>Z</label><input name="z" type="number" value="${Math.round(info?.z ?? 0)}"></div></div><div class="hint">${t("tp_hint")}</div>`,
      (f) => act("teleport", { playfield: f.pf.value, x: +f.x.value, y: +f.y.value, z: +f.z.value })),
    heal: () => act("heal").catch(fail),
    bp: () => act("bp_finish").catch(fail),
    god: () => act("god", { on: !p.meta?.god }).catch(fail),
    survival: () => act("survival_reset").catch(fail),
    playtime: () => act("playtime_reset").catch(fail),
    tp_more: async () => {
      let positions = [];
      try { positions = await api.get(`/api/servers/${s.id}/positions`); } catch { }
      dialog(t("pm_tp_more"), html`<label>${t("pm_tp_kind")}</label><select name="k">
          <option value="pos">${t("pm_tp_pos")}</option><option value="player">${t("pm_tp_player")}</option><option value="structure">${t("pm_tp_structure")}</option></select>
        <label>${t("pm_tp_pos")}</label><select name="pos">${positions.map((x) => html`<option>${x.name}</option>`)}</select>
        <label>${t("pm_tp_target")}</label><input name="target" placeholder="SteamID / ${t("pm_tp_structure")} #">
        <div class="hint">${t("pm_tp_hint")}</div>`,
        (f) => f.k.value === "pos" ? act("teleport_position", { name: f.pos.value })
          : f.k.value === "player" ? act("teleport_to_player", { target: f.target.value.trim() })
          : act("teleport_to_structure", { structureId: +f.target.value }));
    },
    faction: () => dialog(t("pm_faction"), html`<div class="form-row"><div><label>${t("pm_fgroup")}</label><input name="fg" type="number" min="0" max="255" value="${info?.factionGroup ?? ""}"></div>
      <div><label>${t("pm_fid")}</label><input name="fid" type="number" min="0" value="${info?.factionId ?? ""}"></div></div>
      <div class="form-row"><div><label>${t("pm_origin")}</label><input name="origin" type="number" min="0" max="255" value="${info?.origin ?? ""}"></div>
      <div><label>${t("pm_start_pf")}</label><input name="spf" value="${info?.startPlayfield ?? ""}"></div></div>`,
      (f) => act("player_set", { factionGroup: f.fg.value === "" ? null : +f.fg.value, factionId: f.fid.value === "" ? null : +f.fid.value,
        origin: f.origin.value === "" ? null : +f.origin.value, startPlayfield: f.spf.value || null })),
    bp_res: () => dialog(t("pm_bp_res"), html`<label>${t("pm_bp_items")}</label><textarea name="items" rows="4" placeholder="4297 x 100&#10;4298 x 50"></textarea>
      <label class="check"><input type="checkbox" name="replace"> ${t("pm_bp_replace")}</label>`,
      (f) => act("bp_resources", { replace: f.replace.checked, items: f.items.value.split("\n").map((l) => l.match(/(\d+)\s*(?:x|\*|×)?\s*(\d+)?/)).filter(Boolean)
        .map((m) => ({ id: +m[1], count: +(m[2] || 1), slot: 0, ammo: 0, decay: 0 })) })),
    files: () => {
      const dlg = dialog(t("pm_files"), html`<div class="hint">${t("pm_files_hint")}</div>
        <div class="actions-row wrap"><button type="button" class="btn" data-f="save">${t("pm_file_save")}</button>
        <button type="button" class="btn danger" data-f="reset">${t("pm_file_reset")}</button></div>
        ${p.saves?.length ? html`<div class="table-wrap"><table><tbody>${p.saves.map((x) => html`<tr><td>${x.file}</td><td>${formatBytes(x.size)}</td>
          <td><button type="button" class="btn small" data-load="${x.file}">${t("pm_file_load")}</button></td></tr>`)}</tbody></table></div>` : ""}`, () => true, "OK");
      $$("[data-f]", dlg).forEach((b) => b.addEventListener("click", () => {
        if (b.dataset.f === "reset" && !confirm(t("pm_reset_confirm"))) return;
        dlg.close(); act(b.dataset.f === "save" ? "save_file" : "reset").catch(fail);
      }));
      $$("[data-load]", dlg).forEach((b) => b.addEventListener("click", () => { dlg.close(); act("load_file", { name: b.dataset.load }).catch(fail); }));
    },
    impersonate: () => dialog(t("pm_impersonate"), html`<div class="hint">${t("pm_imp_hint")}</div>
      <label>${t("pm_imp_mode")}</label><select name="m"><option value="impersonate">${t("pm_imp_start")}</option><option value="impersonate_end">${t("pm_imp_end")}</option></select>`,
      (f) => act(f.m.value)),
    role: () => dialog(t("a_role"), html`<select name="perm">${[[0, "player"], [3, "gamemaster"], [6, "moderator"], [9, "admin"]].map(([v, r]) =>
        html`<option value="${v}" ${raw(v === p.permission ? "selected" : "")}>${t("role_" + r)}</option>`)}</select><div class="hint">${t("role_hint")}</div>`,
      (f) => act("role", { permission: +f.perm.value })),
  };
  $$("[data-a]", el).forEach((b) => b.addEventListener("click", () => handlers[b.dataset.a]()));
}

// =================================================================== inventory history (0.9.5)
function stacksTable(list, catalog) {
  return html`<div class="inv-grid">${(list || []).map((x) => html`<div>${catalog ? itemLabel(catalog, x.id) : "#" + x.id} <b>× ${x.count}</b></div>`)}</div>`;
}

async function snapshotDialog(s, snapId, steamId, online, admin, catalog, done) {
  let list = [];
  try { list = await api.get(`/api/servers/${s.id}/inventories?steamId=${steamId}`); } catch (ex) { fail(ex); return; }
  const x = list.find((i) => i.id === snapId);
  if (!x) return;
  const act = async (action, body) => {
    await api.post(`/api/servers/${s.id}/players/${steamId}/action`, { action, snapshotId: snapId, ...body });
    toast(t("done"));
    done?.();
  };
  const dlg = dialog(`${x.name} · ${when(x.utc)}`, html`<div class="hint">${t("ih_r_" + x.reason)}${x.playfield ? " · " + x.playfield : ""}</div>
    <h4>${t("pl_toolbelt")}</h4>${stacksTable(x.toolbelt, catalog)}<h4>${t("pl_bag")}</h4>${stacksTable(x.bag, catalog)}
    ${admin ? html`<div class="actions-row wrap">
      ${online ? html`<button type="button" class="btn primary" data-r="replace">${t("ih_restore")}</button>
        <button type="button" class="btn" data-r="add">${t("ih_give")}</button>` : html`<div class="hint">${t("ih_offline")}</div>`}
      <button type="button" class="btn" data-r="keep">${x.reason === "kept" ? t("ih_unkeep") : t("ih_keep")}</button></div>
      <div class="hint">${t("ih_replace_hint")}</div>` : ""}`, () => true, "OK", false);
  $$("[data-r]", dlg).forEach((b) => b.addEventListener("click", async () => {
    try {
      if (b.dataset.r === "keep") await act("inventory_keep", { on: x.reason !== "kept" });
      else await act("inventory_restore", { replace: b.dataset.r === "replace" });
      dlg.close();
    } catch (ex) { fail(ex); }
  }));
}

async function inventoryHistoryDialog(s, steamId, me) {
  let list = [];
  try { list = await api.get(`/api/servers/${s.id}/inventories?reason=death`); } catch (ex) { fail(ex); return; }
  const dlg = dialog(t("ih_deaths"), html`<div class="hint">${t("ih_deaths_hint")}</div>
    ${list.length ? html`<div class="table-wrap"><table><tbody>${list.map((x) => html`<tr><td><b>${x.name}</b></td><td>${when(x.utc)}</td><td>${x.playfield || ""}</td>
      <td>${t("ih_items", (x.toolbelt?.length || 0) + (x.bag?.length || 0))}</td><td><button type="button" class="btn small" data-v="${x.id}" data-s="${x.steamId}">${t("ih_view")}</button></td></tr>`)}</tbody></table></div>`
      : html`<div class="hint">${t("ih_none")}</div>`}`, () => true, "OK");
  const online = await api.get(`/api/servers/${s.id}/players`).catch(() => []);
  $$("[data-v]", dlg).forEach((b) => b.addEventListener("click", () => {
    dlg.close();
    snapshotDialog(s, +b.dataset.v, b.dataset.s, online.some((p) => p.steamId === b.dataset.s && p.online), atLeast(me.role, "admin"), null);
  }));
}

// =================================================================== backups
async function renderBackups(el, s, me) {
  let data;
  const load = async () => {
    try { data = await api.get(`/api/servers/${s.id}/backups`); } catch (ex) { fail(ex); return; }
    draw();
  };
  const draw = (pct) => {
    const owner = atLeast(me.role, "owner");
    mount(el, html`<div class="card">
      <div class="actions-row"><input id="bk-note" maxlength="200" placeholder="${t("bk_note")}">
        <button class="btn primary" id="bk-create" ${raw(data.running ? "disabled" : "")}>${t("bk_create")}</button>
        <button class="btn" id="bk-create-st" ${raw(data.running ? "disabled" : "")}>${t("bk_structures")}</button></div>
      ${data.running || pct != null ? html`<div class="hint">${t("bk_running")} ${pct != null ? `${pct} %` : ""}</div><div class="bar"><i data-pct="${pct ?? 0}"></i></div>` : ""}
      </div>
      <div class="card"><h3>${t("bk_list")}</h3>
      ${data.list.length ? html`<div class="table-wrap"><table><tbody>${data.list.map((b) => html`<tr>
        <td><b>${when(b.createdUtc)}</b><div class="hint">${b.name}</div></td>
        <td><span class="badge">${t("bk_kind_" + b.kind) === "bk_kind_" + b.kind ? b.kind : t("bk_kind_" + b.kind)}</span>${b.kept ? html` <span class="badge">${t("bk_kept")}</span>` : ""}${b.note ? html`<div class="hint">${b.note}</div>` : ""}${b.by ? html`<div class="hint">${b.by}</div>` : ""}</td>
        <td>${formatBytes(b.size)}</td>
        <td class="row-actions"><a class="btn small" href="/api/servers/${s.id}/backups/download?name=${encodeURIComponent(b.name)}" download>${t("bk_download")}</a>
          ${owner ? html`<button class="btn small" data-restore="${b.name}">${t("bk_restore")}</button>` : ""}
          <button class="btn small" data-part="${b.name}">${t("bk_part")}</button>
          <button class="btn small" data-keep="${b.name}" data-kept="${b.kept ? 1 : 0}">${b.kept ? t("ih_unkeep") : t("ih_keep")}</button>
          <button class="btn small danger" data-del="${b.name}">${t("act_delete")}</button></td></tr>`)}</tbody></table></div>`
        : html`<div class="hint">${t("bk_none")}</div>`}
      <div class="hint">${t("bk_auto_hint")}</div></div>
      <form class="card" id="bk-set"><h3>${t("bk_settings")}</h3>
        <label>${t("bk_keep")}</label><input name="keep" type="number" min="1" max="500" value="${data.settings.keep}">
        <label>${t("bk_copy")}</label><input name="copyDir" value="${data.settings.copyDir ?? ""}" placeholder="D:\\OneDrive\\Empyrion">
        <div class="hint">${t("bk_copy_hint")}</div>
        <label>${t("bk_copy_keep")}</label><input name="copyKeep" type="number" min="1" max="500" value="${data.settings.copyKeep}">
        <label>${t("bk_keep_days")}</label><input name="keepDays" type="number" min="0" max="3650" value="${data.settings.keepDays ?? 0}">
        <label>${t("bk_min_free")}</label><input name="minFreeGb" type="number" min="0" max="10000" value="${data.settings.minFreeGb ?? 10}">
        <button class="btn primary">${t("save")}</button></form>`);
    $$(".bar > i[data-pct]", el).forEach((i) => { i.style.width = `${i.dataset.pct}%`; });
    $("#bk-create", el).addEventListener("click", async () => {
      try { await api.post(`/api/servers/${s.id}/backups`, { note: $("#bk-note", el).value }); data.running = true; draw(0); } catch (ex) { fail(ex); }
    });
    $("#bk-create-st", el).addEventListener("click", async () => {
      try { await api.post(`/api/servers/${s.id}/backups`, { note: $("#bk-note", el).value, kind: "structures" }); data.running = true; draw(0); } catch (ex) { fail(ex); }
    });
    $$("[data-keep]", el).forEach((b) => b.addEventListener("click", async () => {
      try { await api.post(`/api/servers/${s.id}/backups/keep`, { name: b.dataset.keep, keep: b.dataset.kept !== "1" }); load(); } catch (ex) { fail(ex); }
    }));
    $$("[data-part]", el).forEach((b) => b.addEventListener("click", async () => {
      let c;
      try { c = await api.get(`/api/servers/${s.id}/backups/contents?name=${encodeURIComponent(b.dataset.part)}`); } catch (ex) { fail(ex); return; }
      dialog(`${t("bk_part")} · ${b.dataset.part}`, html`<div class="hint">${t("bk_part_hint")}</div>
        <label>${t("bk_part_structures")}</label><input name="st" placeholder="1234, 5678" list="bk-st">
        <datalist id="bk-st">${(c.structures || []).map((x) => html`<option value="${x.id}">${x.name || "?"} ${x.type} ${x.playfield || ""}${x.removed ? " ✝" : ""}</option>`)}</datalist>
        <div class="hint">${t("bk_part_count", (c.structures || []).length)}</div>
        <label>${t("bk_part_player")}</label><select name="pl"><option value="">—</option>${(c.players || []).map((x) => html`<option value="${x.steamId}">${x.name || x.steamId}</option>`)}</select>`,
        async (f) => {
          const structures = f.st.value.split(/[,;\s]+/).filter(Boolean).map(Number).filter((n) => n > 0);
          if (!structures.length && !f.pl.value) return false;
          await api.post(`/api/servers/${s.id}/backups/restore-part`, { name: b.dataset.part, structures, steamId: f.pl.value || null });
          toast(t("bk_restored"));
        }, t("bk_restore"), true);
    }));
    $$("[data-del]", el).forEach((b) => b.addEventListener("click", async () => {
      if (!confirm(`${t("act_delete")} ${b.dataset.del} ?`)) return;
      try { await api.post(`/api/servers/${s.id}/backups/delete`, { name: b.dataset.del }); load(); } catch (ex) { fail(ex); }
    }));
    $$("[data-restore]", el).forEach((b) => b.addEventListener("click", () => dialog(`${t("bk_restore_title")} ${b.dataset.restore}`,
      html`<div class="hint">${t("bk_restore_hint")}</div>
        <label class="check"><input type="checkbox" name="cfg"> ${t("bk_restore_config")}</label>
        <label class="check"><input type="checkbox" name="eam"> ${t("bk_restore_eam")}</label>`,
      async (f) => {
        await api.post(`/api/servers/${s.id}/backups/restore`, { name: b.dataset.restore, restoreConfig: f.cfg.checked, restoreEamData: f.eam.checked });
        toast(t("bk_restored"));
        load();
      }, t("bk_restore"), true)));
    $("#bk-set", el).addEventListener("submit", async (e) => {
      e.preventDefault();
      const f = e.target;
      try {
        await api.put(`/api/servers/${s.id}/backups/settings`, { keep: +f.keep.value, copyDir: f.copyDir.value, copyKeep: +f.copyKeep.value,
          keepDays: +f.keepDays.value, minFreeGb: +f.minFreeGb.value });
        toast(t("saved"));
      } catch (ex) { fail(ex); }
    });
  };
  tools.listener = (msg) => {
    if (msg.id !== s.id || msg.type !== "backup_progress" || !el.isConnected) return;
    if (msg.pct == null) { load(); return; }
    data.running = true;
    const bar = $(".bar > i", el);
    if (bar) { bar.style.width = `${msg.pct}%`; bar.parentElement.previousElementSibling.textContent = `${t("bk_running")} ${msg.pct} %`; }
    else draw(msg.pct);
  };
  await load();
}

// =================================================================== scheduler
const KINDS = ["restart", "backup", "message", "command", "wipe", "stop", "start", "update", "cleanup",
  "sequence", "backup_structures", "shared_cleanup", "limits", "cleanup_players", "cleanup_inventories"];

function describeSchedule(sc) {
  if (sc.type === "interval") return t("every_min", sc.minutes);
  if (sc.type === "monthly") return t("sc_monthly_desc", sc.day, (sc.times || []).join(", "));
  const days = t("days_short").split(",");
  return `${(sc.times || []).join(", ")}${sc.days?.length ? ` · ${sc.days.map((d) => days[d]).join(" ")}` : ""}`;
}

function resultText(r) {
  if (!r) return "—";
  const [code, ...rest] = r.split(" ");
  if (code.startsWith("error:")) return errorText(code.slice(6)) + (rest.length ? ` ${rest.join(" ")}` : "");
  const k = "res_" + code;
  return (t(k) === k ? code : t(k)) + (rest.length ? ` · ${rest.join(" ")}` : "");
}

async function renderSchedule(el, s) {
  let list = [];
  const load = async () => {
    try { list = await api.get(`/api/servers/${s.id}/tasks`); } catch (ex) { fail(ex); }
    draw();
  };
  const draw = () => {
    mount(el, html`<div class="card"><div class="actions-row"><button class="btn primary" id="sc-new">+ ${t("sc_new")}</button></div>
      ${list.length ? html`<div class="table-wrap"><table><thead><tr><th>${t("sc_kind")}</th><th>${t("sc_when")}</th><th>${t("sc_next")}</th><th>${t("sc_last")}</th><th></th></tr></thead>
      <tbody>${list.map((x) => html`<tr class="${x.enabled ? "" : "muted"}">
        <td><b>${t("k_" + x.kind)}</b><div class="hint">${taskSummary(x)}</div></td><td>${describeSchedule(x.schedule)}</td>
        <td>${x.enabled ? when(x.nextRunUtc) : "—"}</td><td>${when(x.lastRunUtc)}<div class="hint">${x.running ? t("res_running") : resultText(x.lastResult)}</div></td>
        <td class="row-actions"><label class="check inline"><input type="checkbox" data-toggle="${x.id}" ${raw(x.enabled ? "checked" : "")}> ${t("sc_enabled")}</label>
          <button class="btn small" data-edit="${x.id}">${t("sc_edit")}</button><button class="btn small" data-run="${x.id}">${t("sc_run")}</button>
          <button class="btn small danger" data-del="${x.id}">${t("act_delete")}</button></td></tr>`)}</tbody></table></div>`
        : html`<div class="hint">${t("sc_none")}</div>`}</div>`);
    $("#sc-new", el).addEventListener("click", () => taskDialog(s, null, load));
    $$("[data-edit]", el).forEach((b) => b.addEventListener("click", () => taskDialog(s, list.find((x) => x.id === +b.dataset.edit), load)));
    $$("[data-run]", el).forEach((b) => b.addEventListener("click", async () => {
      try { await api.post(`/api/servers/${s.id}/tasks/${b.dataset.run}/run`); toast(t("done")); load(); } catch (ex) { fail(ex); }
    }));
    $$("[data-del]", el).forEach((b) => b.addEventListener("click", async () => {
      if (!confirm(`${t("act_delete")} ?`)) return;
      try { await api.post(`/api/servers/${s.id}/tasks/${b.dataset.del}/delete`); load(); } catch (ex) { fail(ex); }
    }));
    $$("[data-toggle]", el).forEach((c) => c.addEventListener("change", async () => {
      const x = list.find((y) => y.id === +c.dataset.toggle);
      try { await api.put(`/api/servers/${s.id}/tasks/${x.id}`, { kind: x.kind, enabled: c.checked, schedule: x.schedule, params: x.params }); load(); }
      catch (ex) { fail(ex); c.checked = !c.checked; }
    }));
  };
  tools.listener = (msg) => { if (msg.type === "tasks" && msg.id === s.id && el.isConnected) load(); };
  await load();
}

function taskSummary(x) {
  const p = x.params || {};
  if (x.kind === "message") return p.text;
  if (x.kind === "command") return p.command;
  if (x.kind === "wipe") return `${t("w_" + p.wipe)} : ${p.solarSystem ? "☀ " + p.solarSystem : (p.playfields || []).join(", ")}${p.exclude?.length ? " − " + p.exclude.join(", ") : ""}`;
  if (x.kind === "sequence") return (p.steps || []).map((st) => t("k_" + st.kind) + (st.delaySeconds ? ` +${st.delaySeconds}s` : "")).join(" → ");
  if (["cleanup_players", "cleanup_inventories"].includes(x.kind)) return t("sc_days_n", p.days ?? 90);
  if (["restart", "stop", "update"].includes(x.kind)) return `${p.warnMinutes} min${p.backupFirst ? ` · ${t("p_backup_first")}` : ""}`;
  return "";
}

function taskDialog(s, x, done) {
  const p = x?.params || { warnMinutes: 5, logDays: 14, mode: "chat", wipe: "poi", backupFirst: false };
  const sc = x?.schedule || { type: "daily", times: ["04:00"], days: [] };
  const days = t("days_short").split(",");
  const dlg = dialog(x ? t("sc_edit") : t("sc_new"), html`
    <label>${t("sc_kind")}</label><select name="kind">${KINDS.map((k) => html`<option value="${k}" ${raw(k === (x?.kind ?? "restart") ? "selected" : "")}>${t("k_" + k)}</option>`)}</select>
    <label>${t("sc_when")}</label><select name="type"><option value="daily" ${raw(sc.type === "daily" ? "selected" : "")}>${t("sc_daily")}</option>
      <option value="interval" ${raw(sc.type === "interval" ? "selected" : "")}>${t("sc_interval")}</option>
      <option value="monthly" ${raw(sc.type === "monthly" ? "selected" : "")}>${t("sc_monthly")}</option></select>
    <div data-show="daily monthly"><label>${t("sc_times")}</label><input name="times" value="${(sc.times || []).join(", ")}" placeholder="04:00, 16:00"></div>
    <div data-show="monthly"><label>${t("sc_month_day")}</label><input name="monthDay" type="number" min="1" max="28" value="${sc.day || 1}"></div>
    <div data-show="daily"><label>${t("sc_days")}</label><div class="days">${days.map((d, i) => html`<label class="check inline"><input type="checkbox" name="day" value="${i}" ${raw(sc.days?.includes(i) ? "checked" : "")}> ${d}</label>`)}</div></div>
    <div data-show="interval"><label>${t("sc_minutes")}</label><input name="minutes" type="number" min="5" value="${sc.minutes || 60}"></div>
    <div data-for="restart stop update"><label>${t("p_warn")}</label><input name="warn" type="number" min="0" max="60" value="${p.warnMinutes ?? 5}">
      <label>${t("p_message")}</label><input name="message" maxlength="120" value="${p.message ?? ""}" placeholder="Redémarrage du serveur / Server restart"></div>
    <div data-for="restart update wipe"><label class="check"><input type="checkbox" name="backupFirst" ${raw(p.backupFirst ? "checked" : "")}> ${t("p_backup_first")}</label></div>
    <div data-for="message"><label>${t("p_text")}</label><input name="text" maxlength="300" value="${p.text ?? ""}">
      <label>${t("p_mode")}</label><select name="mode"><option value="chat">${t("chat_mode_chat")}</option>
      <option value="popup" ${raw(p.mode === "popup" ? "selected" : "")}>${t("chat_mode_popup")}</option></select></div>
    <div data-for="command"><label>${t("p_command")}</label><input name="command" maxlength="500" value="${p.command ?? ""}"></div>
    <div data-for="wipe"><label>${t("p_playfields")}</label><input name="playfields" value="${(p.playfields || []).join(", ")}">
      <label>${t("p_wipe")}</label><select name="wipe">${["poi", "deposit", "terrain", "all"].map((w) => html`<option value="${w}" ${raw(p.wipe === w ? "selected" : "")}>${t("w_" + w)}</option>`)}</select>
      <div class="hint">${t("wipe_hint")} ${t("sc_all_hint")}</div>
      <label>${t("sc_solar")}</label><input name="solarSystem" value="${p.solarSystem ?? ""}">
      <label>${t("sc_exclude")}</label><input name="exclude" value="${(p.exclude || []).join(", ")}"></div>
    <div data-for="cleanup_players cleanup_inventories"><label>${t("sc_days_old")}</label><input name="days" type="number" min="1" max="3650" value="${p.days ?? 90}"></div>
    <div data-for="sequence"><label>${t("sc_steps")}</label><textarea name="steps" rows="4" placeholder="message 0&#10;backup 60&#10;restart 300">${(p.steps || []).map((st) => `${st.kind} ${st.delaySeconds || 0}`).join("\n")}</textarea>
      <div class="hint">${t("sc_steps_hint")}</div></div>
    <div data-for="cleanup"><label>${t("p_log_days")}</label><input name="logDays" type="number" min="1" max="365" value="${p.logDays ?? 14}"></div>
    <label class="check"><input type="checkbox" name="enabled" ${raw(x?.enabled === false ? "" : "checked")}> ${t("sc_enabled")}</label>`,
  async (f) => {
    const body = {
      kind: f.kind.value, enabled: f.enabled.checked,
      schedule: f.type.value === "interval" ? { type: "interval", minutes: +f.minutes.value }
        : f.type.value === "monthly" ? { type: "monthly", times: f.times.value.split(",").map((v) => v.trim()).filter(Boolean), day: +f.monthDay.value }
        : { type: "daily", times: f.times.value.split(",").map((v) => v.trim()).filter(Boolean), days: $$("[name=day]", f).filter((c) => c.checked).map((c) => +c.value) },
      params: {
        warnMinutes: +f.warn.value, message: f.message.value || null, backupFirst: f.backupFirst.checked, text: f.text.value, mode: f.mode.value,
        command: f.command.value, playfields: f.playfields.value.split(",").map((v) => v.trim()).filter(Boolean), wipe: f.wipe.value, logDays: +f.logDays.value,
        solarSystem: f.solarSystem.value.trim() || null, exclude: f.exclude.value.split(",").map((v) => v.trim()).filter(Boolean), days: +f.days.value || 90,
      },
    };
    if (body.kind === "sequence") {
      const shared = { ...body.params };
      body.params.steps = f.steps.value.split("\n").map((l) => l.trim().split(/\s+/)).filter((a) => a[0])
        .map(([kind, delay]) => ({ kind, delaySeconds: +delay || 0, params: shared }));
    }
    if (x) await api.put(`/api/servers/${s.id}/tasks/${x.id}`, body); else await api.post(`/api/servers/${s.id}/tasks`, body);
    done();
  });
  const f = $("form", dlg);
  const sync = () => {
    $$("[data-show]", f).forEach((d) => { d.hidden = !d.dataset.show.split(" ").includes(f.type.value); });
    $$("[data-for]", f).forEach((d) => { d.hidden = !d.dataset.for.split(" ").includes(f.kind.value); });
  };
  f.kind.addEventListener("change", sync);
  f.type.addEventListener("change", sync);
  sync();
}

// =================================================================== options and scenarios
function optionEditor(id, entries, opts = {}) {
  const isBool = (v) => /^(true|false)$/i.test(v ?? "");
  return html`<input class="opt-filter" data-for="${id}" type="search" placeholder="${t("op_filter")}" autocomplete="off">
    <div class="opts" id="${id}">${entries.map((e, i) => html`<div class="opt ${e.active ? "" : "off"}" data-i="${i}" data-key="${(e.section ? e.section + " " : "") + e.key}">
      <label class="check inline"><input type="checkbox" class="opt-on" ${raw(e.active ? "checked" : "")} ${raw(e.managed || opts.readOnly ? "disabled" : "")}>
        <b>${e.key}</b>${e.section ? html` <span class="hint">${e.section}</span>` : ""}</label>
      ${isBool(e.value)
        ? html`<select class="opt-val" ${raw(e.managed || opts.readOnly ? "disabled" : "")}>${["True", "False"].map((v) =>
            html`<option ${raw(v.toLowerCase() === String(e.value).toLowerCase() ? "selected" : "")}>${e.value === e.value.toLowerCase() ? v.toLowerCase() : v}</option>`)}</select>`
        : html`<input class="opt-val" value="${e.value}" ${raw(e.managed || opts.readOnly ? "disabled" : "")}>`}
      <div class="hint">${e.managed ? t("op_managed") : e.help || ""}</div></div>`)}</div>`;
}

function wireEditor(root, id, entries) {
  const box = $(`#${id}`, root);
  $(`.opt-filter[data-for="${id}"]`, root).addEventListener("input", (e) => {
    const q = e.target.value.toLowerCase();
    $$(".opt", box).forEach((o) => { o.hidden = q && !(o.dataset.key.toLowerCase().includes(q) || o.textContent.toLowerCase().includes(q)); });
  });
  $$(".opt", box).forEach((o) => {
    const on = $(".opt-on", o);
    const val = $(".opt-val", o);
    on.addEventListener("change", () => o.classList.toggle("off", !on.checked));
    val.addEventListener("input", () => { if (!on.checked && !on.disabled) { on.checked = true; o.classList.remove("off"); } });
    val.addEventListener("change", () => { if (!on.checked && !on.disabled) { on.checked = true; o.classList.remove("off"); } });
  });
  return () => $$(".opt", box).map((o) => {
    const e = entries[+o.dataset.i];
    const active = $(".opt-on", o).checked;
    const value = $(".opt-val", o).value;
    return { e, active, value };
  }).filter(({ e, active, value }) => !e.managed && (active !== e.active || (active && value !== e.value)))
    .map(({ e, active, value }) => ({ section: e.section || "", key: e.key, value, active }));
}

async function renderOptions(el, s, me) {
  let c;
  try { c = await api.get(`/api/servers/${s.id}/config`); } catch (ex) { fail(ex); mount(el, html`<div class="card"><div class="error">${errorText(ex.code)}</div></div>`); return; }
  const owner = atLeast(me.role, "owner");
  const stopped = ["Stopped", "Crashed"].includes(s.state);
  const go = c.gameOptions;
  const serverOpts = c.options.filter((o) => !["GameName", "CustomScenario", "Mode", "Seed"].includes(o.key));
  mount(el, html`
    <form class="card" id="op-game"><h3>${t("op_game")}</h3>
      <label>${t("op_save")}</label><input name="gameName" list="op-saves" value="${c.gameName}" required maxlength="60">
      <datalist id="op-saves">${c.saves.map((x) => html`<option value="${x.name}">`)}</datalist>
      <div class="hint">${t("op_save_hint")}</div>
      <div class="form-row"><div><label>${t("op_scenario")}</label><select name="scenario">
        ${c.scenarios.map((x) => html`<option value="${x.name}" ${raw(x.name === c.scenario ? "selected" : "")}>${x.name}</option>`)}
        ${c.scenario && !c.scenarios.some((x) => x.name === c.scenario) ? html`<option selected value="${c.scenario}">${c.scenario} (?)</option>` : ""}</select></div>
        <div><label>${t("op_mode")}</label><select name="mode">${["Survival", "Creative"].map((m) => html`<option ${raw(m === c.mode ? "selected" : "")}>${m}</option>`)}</select></div>
        <div><label>${t("op_seed")}</label><input name="seed" type="number" value="${c.seed ?? ""}"></div></div>
      <button class="btn primary">${t("save")}</button></form>

    <div class="card"><h3>${t("op_scenarios")}</h3>
      <div class="ws-list">${c.known.map((k) => html`<div class="ws-item"><div><b>${k.label}</b>
          <div class="hint">Workshop ${k.workshopId} · ${k.installed ? t("op_installed", k.installed) : t("op_ws_hint")}</div></div>
          ${owner ? html`<button class="btn" data-ws="${k.workshopId}" data-name="${k.installed || k.name}">${t("op_install")}</button>` : ""}</div>`)}</div>
      <div class="table-wrap"><table><tbody>${c.scenarios.map((x) => html`<tr><td><b>${x.name}</b>
          ${x.name === c.scenario ? html` <span class="state ok">${t("op_current")}</span>` : ""}
          ${x.workshopId ? html` <span class="badge">Workshop ${x.workshopId}</span>` : x.stock ? html` <span class="hint">${t("op_stock")}</span>` : ""}
          ${x.description ? html`<div class="hint">${x.description}</div>` : ""}</td>
        <td class="row-actions">${owner && !x.stock && x.name !== c.scenario ? html`<button class="btn small danger" data-delsc="${x.name}">${t("act_delete")}</button>` : ""}</td></tr>`)}</tbody></table></div>
      ${owner ? html`<div class="actions-row"><button class="btn" id="op-ws-other">${t("op_workshop_other")}</button><button class="btn" id="op-import">${t("op_import")}</button></div>` : ""}
    </div>

    <div class="card"><h3>${t("op_gameopts", c.gameName)}</h3>
      ${go.saveExists ? html`<div class="hint">${t("op_gameopts_hint")}</div>` : html`<div class="hint">${t("op_gameopts_missing", c.gameName)}</div>`}
      ${optionEditor("op-go", go.entries, { readOnly: !go.saveExists })}
      ${go.saveExists ? html`<button class="btn primary" id="op-go-save" ${raw(stopped ? "" : "disabled")}>${t("save")}</button>` : ""}</div>

    <div class="card"><h3>${t("op_server")}</h3>
      ${optionEditor("op-srv", serverOpts)}
      <button class="btn primary" id="op-srv-save">${t("save")}</button></div>

    ${owner ? html`<details class="card"><summary><b>${t("op_raw")}</b></summary><div class="hint">${t("op_raw_hint")}</div>
      <textarea id="op-raw" class="console tall" spellcheck="false">${c.raw}</textarea><button class="btn" id="op-raw-save">${t("save")}</button></details>` : ""}`);

  const reload = () => renderOptions(el, s, me);
  const saved = (r) => toast(r?.appliesOnRestart ? t("op_saved_restart") : t("saved"));

  $("#op-game", el).addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = e.target;
    const changes = [["GameName", f.gameName.value.trim()], ["CustomScenario", f.scenario.value], ["Mode", f.mode.value], ["Seed", f.seed.value]]
      .filter(([, v]) => v !== "").map(([key, value]) => ({ section: "GameConfig", key, value, active: true }));
    try { saved(await api.put(`/api/servers/${s.id}/config/options`, { changes })); reload(); } catch (ex) { fail(ex); }
  });

  const srvChanges = wireEditor(el, "op-srv", serverOpts);
  $("#op-srv-save", el).addEventListener("click", async () => {
    const changes = srvChanges();
    if (!changes.length) return;
    try { saved(await api.put(`/api/servers/${s.id}/config/options`, { changes })); reload(); } catch (ex) { fail(ex); }
  });
  const goChanges = wireEditor(el, "op-go", go.entries);
  $("#op-go-save", el)?.addEventListener("click", async () => {
    const changes = goChanges();
    if (!changes.length) return;
    try { await api.put(`/api/servers/${s.id}/config/gameoptions`, { changes }); toast(t("saved")); reload(); } catch (ex) { fail(ex); }
  });
  $("#op-raw-save", el)?.addEventListener("click", async () => {
    try { saved(await api.put(`/api/servers/${s.id}/config/raw`, { text: $("#op-raw", el).value })); reload(); } catch (ex) { fail(ex); }
  });

  const wsDialog = (id, name) => dialog(t("op_install"), html`
    <label>${t("op_workshop_id")}</label><input name="ws" value="${id ?? ""}" pattern="\\d{3,20}" required ${raw(id ? "readonly" : "")}>
    <label>${t("op_folder_name")}</label><input name="name" value="${name ?? ""}" required maxlength="80">
    <label class="check"><input type="checkbox" name="use" checked> ${t("op_use")}</label><div class="hint">${t("op_ws_hint")}</div>`,
  async (f) => { await api.post(`/api/servers/${s.id}/scenarios/workshop`, { workshopId: f.ws.value, name: f.name.value, use: f.use.checked }); });
  $$("[data-ws]", el).forEach((b) => b.addEventListener("click", () => wsDialog(b.dataset.ws, b.dataset.name)));
  $("#op-ws-other", el)?.addEventListener("click", () => wsDialog(null, ""));
  $("#op-import", el)?.addEventListener("click", () => dialog(t("op_import"), html`
    <label>${t("op_import_path")}</label><input name="path" required placeholder="C:\\...\\Reforged Eden 2">
    <label>${t("op_folder_name")}</label><input name="name" required maxlength="80">
    <label class="check"><input type="checkbox" name="use" checked> ${t("op_use")}</label>`,
  async (f) => { await api.post(`/api/servers/${s.id}/scenarios/import`, { path: f.path.value, name: f.name.value, use: f.use.checked }); }));
  $$("[data-delsc]", el).forEach((b) => b.addEventListener("click", async () => {
    if (!confirm(`${t("act_delete")} ${b.dataset.delsc} ?`)) return;
    try { await api.post(`/api/servers/${s.id}/scenarios/delete`, { path: "", name: b.dataset.delsc, use: false }); reload(); } catch (ex) { fail(ex); }
  }));
  tools.listener = (msg) => { if (msg.type === "job" && msg.id === s.id && msg.ok && el.isConnected) reload(); };
}
