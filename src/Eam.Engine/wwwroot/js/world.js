// Step 6: structures, structure library and blueprints, limits, playfields, maps and galaxy.
import { api } from "./api.js";
import { t, extend } from "./i18n.js";
import { html, raw, mount, $, $$, toast, formatBytes } from "./dom.js";
import { atLeast, fail, when, num, dialog, settingsForm } from "./ui.js";
import { playerCount } from "./live.js";

extend({
  fr: {
    tab_structures: "Structures", tab_playfields: "Playfields", add: "Ajouter",
    st_search: "Rechercher (nom, numéro, propriétaire)…", st_all_types: "Tous les types", st_all_pf: "Tous les playfields", st_refresh: "Relire depuis le jeu", st_refresh_warn: "Des joueurs sont connectés. Le jeu se fige quelques secondes pendant la lecture : un joueur qui change de planète à ce moment peut être déconnecté. EAM relit la liste tout seul quand personne n'est connecté.",
    st_library: "Bibliothèque", st_rules: "Limites et règles", st_shared: "Nettoyer Shared", st_last: "Liste lue {0}", st_never: "Liste pas encore lue (serveur arrêté ou mod non connecté).",
    st_owner: "Propriétaire", st_type: "Type", st_class: "Classe", st_blocks: "Blocs", st_devices: "Appareils", st_visited: "Dernière visite", st_count: "{0} structure(s)",
    st_removed: "Détruites ou retirées depuis 2 jours", st_warned: "Au-delà d'une limite", st_powered: "Alimentée", st_first: "Vue pour la première fois",
    st_history: "Historique", st_block_list: "Blocs et appareils", st_pos: "Position", st_system: "Système",
    sa_touch: "Marquer comme visitée", sa_rename: "Renommer", sa_move: "Déplacer", sa_faction: "Changer de faction", sa_export: "Sauvegarder dans la bibliothèque",
    sa_clone: "Cloner", sa_destroy: "Supprimer", sa_save_first: "La sauvegarder d'abord dans la bibliothèque (conseillé)", sa_to_player: "Près d'un joueur connecté",
    sa_coords: "Ou à ces coordonnées", sa_faction_hint: "Abréviation de la faction (ex. ABC). Utilise la commande console « faction entity » du jeu.",
    sa_clone_hint: "EAM exporte la structure puis la fait apparaître à côté (+60 m) ou à la position indiquée.", sa_note: "Note",
    sa_destroy_confirm: "Supprimer « {0} » ({1}) ?", lib_title: "Bibliothèque de structures", lib_structures: "Structures sauvegardées",
    lib_blueprints: "Blueprints (.epb)", lib_none: "Rien pour l'instant.", lib_spawn: "Faire apparaître", lib_upload: "Envoyer un blueprint (.epb)",
    lib_hint: "Les structures sont sauvegardées ici avant chaque suppression par EAM (limites, wipes auto, bouton Supprimer).",
    sp_title: "Faire apparaître « {0} »", sp_pf: "Playfield", sp_name: "Nom (optionnel)", sp_owner: "Propriétaire",
    owner_admin: "Admin", owner_alien: "Alien", owner_faction: "Faction (numéro)", owner_player: "Joueur (numéro d'entité)", sp_owner_id: "Numéro de faction ou de joueur",
    rules_limits: "Limites de structures", rules_limits_on: "Activer les limites", rules_limit_list: "Règles", rules_scope: "Par", scope_player: "joueur", scope_faction: "faction",
    rules_pf: "Playfields (vide = tous, * possible)", rules_max_count: "Nombre max", rules_max_class: "Classe max", rules_max_blocks: "Blocs max",
    rules_action: "Quand une limite est dépassée", act_warn: "Avertir le propriétaire et lister", act_destroy: "Avertir, puis supprimer après le délai (sauvegardée d'abord)",
    rules_grace: "Délai avant suppression (heures)", rules_exempt_admins: "Les joueurs admin, modérateur et maître du jeu ne sont pas limités", rules_exempt_factions: "Factions protégées (numéros)",
    rules_autowipe: "Wipes automatiques de structures", rules_autowipe_on: "Activer", rules_autowipe_list: "Règles", rules_not_visited: "Non visitée depuis (jours)", rules_older: "Existe depuis plus de (heures)",
    rules_autowipe_hint: "Vérifié à chaque lecture de la liste. Chaque structure supprimée est d'abord sauvegardée dans la bibliothèque. 0 = critère non utilisé.",
    rules_reserved: "Playfields réservés", rules_reserved_on: "Activer", rules_reserved_list: "Playfields", rules_factions: "Factions autorisées", rules_steam: "SteamID autorisés", rules_origins: "Origines autorisées",
    rules_res_action: "Sinon", res_teleport: "Téléporter", res_kick: "Expulser", rules_target: "Playfield de destination", rules_reserved_hint: "Les admins passent toujours. Numéros séparés par des virgules.",
    rules_refresh: "Lire la liste des structures toutes les (minutes, 10 au minimum : le jeu se fige quelques secondes pendant la lecture)", rules_shared_age: "Nettoyage Shared : ignorer les dossiers modifiés depuis moins de (heures)",
    rules_violations: "Structures hors limites", rules_none: "Aucune structure hors limites.", rules_wipe_now: "Seraient supprimées par les wipes automatiques",
    sh_title: "Nettoyage du dossier Shared", sh_hint: "Dossiers de structures qui n'existent plus dans la partie. Ils sont déplacés dans eam\\shared-removed (pas supprimés) : vous pouvez les remettre à la main.",
    sh_found: "{0} dossier(s), {1}", sh_none: "Aucun dossier obsolète.", sh_clean: "Déplacer ces dossiers", sh_done: "{0} dossier(s) déplacé(s) dans {1}",
    pf_loaded: "Chargé", pf_not_loaded: "Non chargé", pf_players: "Joueurs", pf_mem: "Mémoire", pf_map: "Carte", pf_load: "Charger", pf_wipe: "Wipe",
    pf_galaxy: "Galaxie", pf_back: "← Playfields", map_2d: "Vue de dessus", map_3d: "Vue 3D", map_hint: "Glisser pour tourner (3D), molette pour zoomer.",
    map_structures: "Structures", map_players: "Joueurs", map_other: "Autres entités", gal_none: "Galaxie non disponible : la sauvegarde n'existe pas encore ou son format est inconnu. Systèmes connus par les structures :",
    gal_systems: "{0} système(s)", wipe_confirm: "Wipe « {0} » sur {1} ?",
    err_structure_not_found: "Structure inconnue.", err_export_failed: "Le jeu n'a pas exporté la structure.", err_library_not_found: "Introuvable dans la bibliothèque.",
    err_blueprint_not_found: "Blueprint introuvable.", err_invalid_file: "Fichier refusé (.epb, 50 Mo max).", err_invalid_name: "Nom invalide.", err_invalid_playfield: "Playfield invalide.",
    err_invalid_faction: "Faction invalide.", err_invalid_source: "Source invalide.", err_structures_not_fresh: "Relisez d'abord la liste des structures (serveur en ligne).",
    err_mod_not_connected: "Le mod EAM n'est pas connecté (serveur arrêté ?).", err_invalid_limit: "Règle de limite incomplète.", err_invalid_autowipe: "Règle de wipe incomplète.",
    err_invalid_reserved: "Playfield réservé incomplet.", err_invalid_refresh: "Intervalle invalide.", err_invalid_grace: "Délai invalide.", err_invalid_age: "Âge invalide.",
    err_invalid_limit_action: "Action invalide.", err_invalid_json: "Données invalides.", err_not_found: "Introuvable.",
  },
  en: {
    tab_structures: "Structures", tab_playfields: "Playfields", add: "Add",
    st_search: "Search (name, number, owner)…", st_all_types: "All types", st_all_pf: "All playfields", st_refresh: "Read from the game", st_refresh_warn: "Players are online. The game freezes a few seconds while reading: a player changing planet at that moment can be disconnected. EAM reads the list by itself when nobody is online.",
    st_library: "Library", st_rules: "Limits and rules", st_shared: "Clean Shared", st_last: "List read {0}", st_never: "List not read yet (server stopped or mod not connected).",
    st_owner: "Owner", st_type: "Type", st_class: "Class", st_blocks: "Blocks", st_devices: "Devices", st_visited: "Last visit", st_count: "{0} structure(s)",
    st_removed: "Destroyed or removed in the last 2 days", st_warned: "Over a limit", st_powered: "Powered", st_first: "First seen",
    st_history: "History", st_block_list: "Blocks and devices", st_pos: "Position", st_system: "System",
    sa_touch: "Mark as visited", sa_rename: "Rename", sa_move: "Move", sa_faction: "Change faction", sa_export: "Save to the library",
    sa_clone: "Clone", sa_destroy: "Remove", sa_save_first: "Save it to the library first (recommended)", sa_to_player: "Next to an online player",
    sa_coords: "Or at these coordinates", sa_faction_hint: "Faction abbreviation (e.g. ABC). Uses the game's \"faction entity\" console command.",
    sa_clone_hint: "EAM exports the structure and spawns it next to it (+60 m) or at the given position.", sa_note: "Note",
    sa_destroy_confirm: "Remove \"{0}\" ({1})?", lib_title: "Structure library", lib_structures: "Saved structures",
    lib_blueprints: "Blueprints (.epb)", lib_none: "Nothing yet.", lib_spawn: "Spawn", lib_upload: "Upload a blueprint (.epb)",
    lib_hint: "Structures are saved here before every removal by EAM (limits, automatic wipes, Remove button).",
    sp_title: "Spawn \"{0}\"", sp_pf: "Playfield", sp_name: "Name (optional)", sp_owner: "Owner",
    owner_admin: "Admin", owner_alien: "Alien", owner_faction: "Faction (number)", owner_player: "Player (entity number)", sp_owner_id: "Faction or player number",
    rules_limits: "Structure limits", rules_limits_on: "Enable limits", rules_limit_list: "Rules", rules_scope: "Per", scope_player: "player", scope_faction: "faction",
    rules_pf: "Playfields (empty = all, * allowed)", rules_max_count: "Max count", rules_max_class: "Max class", rules_max_blocks: "Max blocks",
    rules_action: "When a limit is exceeded", act_warn: "Warn the owner and list it", act_destroy: "Warn, then remove after the delay (saved first)",
    rules_grace: "Delay before removal (hours)", rules_exempt_admins: "Admin, moderator and game master players are not limited", rules_exempt_factions: "Protected factions (numbers)",
    rules_autowipe: "Automatic structure wipes", rules_autowipe_on: "Enable", rules_autowipe_list: "Rules", rules_not_visited: "Not visited for (days)", rules_older: "Exists for more than (hours)",
    rules_autowipe_hint: "Checked every time the list is read. Every removed structure is first saved to the library. 0 = criterion not used.",
    rules_reserved: "Reserved playfields", rules_reserved_on: "Enable", rules_reserved_list: "Playfields", rules_factions: "Allowed factions", rules_steam: "Allowed SteamIDs", rules_origins: "Allowed origins",
    rules_res_action: "Otherwise", res_teleport: "Teleport", res_kick: "Kick", rules_target: "Target playfield", rules_reserved_hint: "Admins always pass. Numbers separated by commas.",
    rules_refresh: "Read the structure list every (minutes, 10 minimum: the game freezes a few seconds while reading it)", rules_shared_age: "Shared cleanup: ignore folders changed less than (hours) ago",
    rules_violations: "Structures over the limits", rules_none: "No structure over the limits.", rules_wipe_now: "Would be removed by automatic wipes",
    sh_title: "Shared folder cleanup", sh_hint: "Folders of structures that no longer exist in the game. They are moved to eam\\shared-removed (not deleted): you can put them back by hand.",
    sh_found: "{0} folder(s), {1}", sh_none: "No obsolete folder.", sh_clean: "Move these folders", sh_done: "{0} folder(s) moved to {1}",
    pf_loaded: "Loaded", pf_not_loaded: "Not loaded", pf_players: "Players", pf_mem: "Memory", pf_map: "Map", pf_load: "Load", pf_wipe: "Wipe",
    pf_galaxy: "Galaxy", pf_back: "← Playfields", map_2d: "Top view", map_3d: "3D view", map_hint: "Drag to rotate (3D), wheel to zoom.",
    map_structures: "Structures", map_players: "Players", map_other: "Other entities", gal_none: "Galaxy not available: the save does not exist yet or its format is unknown. Systems known from structures:",
    gal_systems: "{0} system(s)", wipe_confirm: "Wipe \"{0}\" on {1}?",
    err_structure_not_found: "Unknown structure.", err_export_failed: "The game did not export the structure.", err_library_not_found: "Not found in the library.",
    err_blueprint_not_found: "Blueprint not found.", err_invalid_file: "File refused (.epb, 50 MB max).", err_invalid_name: "Invalid name.", err_invalid_playfield: "Invalid playfield.",
    err_invalid_faction: "Invalid faction.", err_invalid_source: "Invalid source.", err_structures_not_fresh: "Read the structure list first (server online).",
    err_mod_not_connected: "The EAM mod is not connected (server stopped?).", err_invalid_limit: "Incomplete limit rule.", err_invalid_autowipe: "Incomplete wipe rule.",
    err_invalid_reserved: "Incomplete reserved playfield.", err_invalid_refresh: "Invalid interval.", err_invalid_grace: "Invalid delay.", err_invalid_age: "Invalid age.",
    err_invalid_limit_action: "Invalid action.", err_invalid_json: "Invalid data.", err_not_found: "Not found.",
  },
});

const TYPES = ["BA", "CV", "SV", "HV"];
const ago = (utc) => when(utc);

export const worldTabs = (role) => (atLeast(role, "moderator") ? ["structures", "playfields"] : []);

const world = { listener: null };
export function worldEvent(msg) {
  if (msg.type === "structures") { world.listener?.(msg); return true; }
  return false;
}

export function renderWorld(tab, el, s, me) {
  world.listener = null;
  if (tab === "structures") return renderStructures(el, s, me);
  if (tab === "playfields") return renderPlayfields(el, s, me);
}

// =================================================================== structures
async function renderStructures(el, s, me) {
  let data = { list: [], removed: [] };
  const f = { q: "", type: "", pf: "" };
  const gm = atLeast(me.role, "gamemaster");
  const admin = atLeast(me.role, "admin");
  const load = async () => {
    try { data = await api.get(`/api/servers/${s.id}/structures`); } catch (ex) { fail(ex); }
    draw();
  };
  const draw = () => {
    const pfs = [...new Set(data.list.map((x) => x.playfield))].sort();
    const q = f.q.toLowerCase();
    const shown = data.list.filter((x) => (!f.type || x.type === f.type) && (!f.pf || x.playfield === f.pf)
      && (!q || x.name.toLowerCase().includes(q) || String(x.id).includes(q) || (x.owner || "").toLowerCase().includes(q)));
    mount(el, html`<div class="card">
      <div class="actions-row wrap"><input id="st-q" type="search" placeholder="${t("st_search")}" value="${f.q}">
        <select id="st-type"><option value="">${t("st_all_types")}</option>${TYPES.map((x) => html`<option ${raw(f.type === x ? "selected" : "")}>${x}</option>`)}</select>
        <select id="st-pf"><option value="">${t("st_all_pf")}</option>${pfs.map((x) => html`<option ${raw(f.pf === x ? "selected" : "")}>${x}</option>`)}</select>
        <button class="btn" id="st-refresh">${t("st_refresh")}</button>
        ${gm ? html`<button class="btn" id="st-lib">${t("st_library")}</button>` : ""}
        <button class="btn" id="st-rules">${t("st_rules")}</button>
        ${admin ? html`<button class="btn" id="st-shared">${t("st_shared")}</button>` : ""}</div>
      <div class="hint">${data.lastRefreshUtc ? t("st_last", ago(data.lastRefreshUtc)) : t("st_never")} · ${t("st_count", shown.length)}</div>
      <div class="table-wrap"><table><thead><tr><th>${t("col_name")}</th><th>${t("st_type")}</th><th>${t("st_owner")}</th><th>${t("col_playfield")}</th>
        <th>${t("st_class")}</th><th>${t("st_blocks")}</th><th>${t("st_visited")}</th></tr></thead>
      <tbody>${shown.slice(0, 1000).map((x) => html`<tr class="clickable" data-sid="${x.id}">
        <td><b>${x.name}</b> ${x.warnedUtc ? html`<span class="badge warn" title="${t("st_warned")}">!</span>` : ""}<div class="hint">${x.id}</div></td>
        <td>${x.type}</td><td>${x.owner}</td><td>${x.playfield}</td><td>${x.class}</td><td>${num(x.blocks)}</td><td class="hint">${ago(x.lastVisitedUtc)}</td></tr>`)}</tbody></table></div></div>
      ${data.removed.length ? html`<details class="card"><summary><b>${t("st_removed")}</b> (${data.removed.length})</summary><div class="table-wrap"><table><tbody>
        ${data.removed.map((x) => html`<tr class="clickable muted" data-sid="${x.id}"><td><b>${x.name}</b><div class="hint">${x.id}</div></td><td>${x.type}</td><td>${x.owner}</td>
          <td>${x.playfield}</td><td class="hint">${ago(x.removedUtc)}</td></tr>`)}</tbody></table></div></details>` : ""}`);
    $("#st-q", el).addEventListener("input", (e) => { f.q = e.target.value; const pos = e.target.selectionStart; draw(); const i = $("#st-q", el); i.focus(); i.setSelectionRange(pos, pos); });
    $("#st-type", el).addEventListener("change", (e) => { f.type = e.target.value; draw(); });
    $("#st-pf", el).addEventListener("change", (e) => { f.pf = e.target.value; draw(); });
    $("#st-refresh", el).addEventListener("click", async (e) => {
      const btn = e.target;
      const go = async () => { btn.disabled = true; try { await api.post(`/api/servers/${s.id}/structures/refresh`); await load(); } catch (ex) { fail(ex); btn.disabled = false; } };
      // Reading the list freezes the game a few seconds: players changing playfield at that moment are disconnected.
      if (playerCount(s.id) > 0) dialog(t("st_refresh"), html`<div class="hint">${t("st_refresh_warn")}</div>`, async () => { go(); }, t("st_refresh"), true);
      else go();
    });
    $("#st-lib", el)?.addEventListener("click", () => libraryDialog(s, me));
    $("#st-rules", el).addEventListener("click", () => rulesView(el, s, me, () => renderStructures(el, s, me)));
    $("#st-shared", el)?.addEventListener("click", () => sharedDialog(s));
    $$("tr[data-sid]", el).forEach((tr) => tr.addEventListener("click", () => structureDialog(s, me, +tr.dataset.sid, load)));
  };
  world.listener = (msg) => { if (msg.id === s.id && el.isConnected && !document.querySelector("dialog")) load(); };
  await load();
}

async function structureDialog(s, me, sid, reload) {
  let d;
  try { d = await api.get(`/api/servers/${s.id}/structures/${sid}`); } catch (ex) { fail(ex); return; }
  const x = d.structure;
  const gm = atLeast(me.role, "gamemaster") && !x.removedUtc;
  const dlg = dialog(`${x.name} (${x.id})`, html`
    <dl class="kv"><dt>${t("st_type")}</dt><dd>${d.type} · ${t("st_class")} ${x.class}</dd><dt>${t("st_owner")}</dt><dd>${d.owner}</dd>
      <dt>${t("col_playfield")}</dt><dd>${x.playfield}${x.solarSystem ? ` (${x.solarSystem})` : ""}</dd>
      <dt>${t("st_pos")}</dt><dd>${Math.round(x.x)}, ${Math.round(x.y)}, ${Math.round(x.z)}</dd>
      <dt>${t("st_blocks")}</dt><dd>${num(x.blocks)} · ${t("st_devices")} ${num(x.devices)}</dd>
      <dt>${t("st_visited")}</dt><dd>${when(x.lastVisitedUtc)}</dd><dt>${t("st_first")}</dt><dd>${when(x.firstSeenUtc)}</dd></dl>
    ${gm ? html`<div class="actions-row wrap">${["touch", "rename", "move", "faction", "export", "clone", "destroy"].map((a) =>
      html`<button type="button" class="btn small ${a === "destroy" ? "danger" : ""}" data-act="${a}">${t("sa_" + a)}</button>`)}</div>` : ""}
    <h4>${t("st_history")}</h4><div class="history">${d.history.length ? d.history.map((h) => html`<div><span class="chat-time">${when(h.utc)}</span> <b>${h.kind}</b>
      ${h.old ?? ""} ${h.new ? `→ ${h.new}` : ""} ${h.by ? html`<span class="hint">(${h.by})</span>` : ""}</div>`) : html`<div class="hint">—</div>`}</div>
    ${d.blocks ? html`<details><summary>${t("st_block_list")} (${d.blocks.length})</summary><div class="table-wrap"><table><tbody>
      ${d.blocks.map((b) => html`<tr><td>${b.name || "#" + b.id}</td><td>${num(b.count)}</td></tr>`)}</tbody></table></div></details>` : ""}`, null, "", false, true);
  $$("[data-act]", dlg).forEach((b) => b.addEventListener("click", () => { dlg.close(); structureAction(s, x, b.dataset.act, reload); }));
}

function structureAction(s, x, action, reload) {
  const post = async (body) => {
    const r = await api.post(`/api/servers/${s.id}/structures/${x.id}/action`, { action, ...body });
    toast(t("done"));
    setTimeout(reload, 1500);
    return r;
  };
  if (action === "touch") { post({}).catch(fail); return; }
  if (action === "rename") {
    dialog(t("sa_rename"), html`<input name="name" value="${x.name}" maxlength="60" required>`, (f) => post({ name: f.name.value }));
    return;
  }
  if (action === "faction") {
    dialog(t("sa_faction"), html`<input name="faction" maxlength="12" required><div class="hint">${t("sa_faction_hint")}</div>`, (f) => post({ faction: f.faction.value }));
    return;
  }
  if (action === "export") {
    dialog(t("sa_export"), html`<label>${t("sa_note")}</label><input name="note" maxlength="200">`, (f) => post({ note: f.note.value }));
    return;
  }
  if (action === "destroy") {
    dialog(t("sa_destroy_confirm", x.name, x.id), html`<label class="check"><input type="checkbox" name="save" checked> ${t("sa_save_first")}</label>`,
      (f) => post({ saveFirst: f.save.checked }), t("sa_destroy"), true);
    return;
  }
  const players = [];
  const coords = html`<label>${t("sp_pf")}</label><input name="pf" value="${x.playfield}">
    <div class="form-row three"><div><label>X</label><input name="x" type="number" step="any" value="${Math.round(x.x + (action === "clone" ? 60 : 0))}"></div>
    <div><label>Y</label><input name="y" type="number" step="any" value="${Math.round(x.y + (action === "clone" ? 20 : 0))}"></div>
    <div><label>Z</label><input name="z" type="number" step="any" value="${Math.round(x.z + (action === "clone" ? 60 : 0))}"></div></div>`;
  if (action === "clone") {
    dialog(t("sa_clone"), html`<div class="hint">${t("sa_clone_hint")}</div><label>${t("sp_name")}</label><input name="name" maxlength="60">${coords}`,
      (f) => post({ name: f.name.value, playfield: f.pf.value, x: +f.x.value, y: +f.y.value, z: +f.z.value }));
    return;
  }
  if (action === "move") {
    api.get(`/api/servers/${s.id}/players`).then((list) => {
      players.push(...list.filter((p) => p.online));
      dialog(t("sa_move"), html`<label>${t("sa_to_player")}</label><select name="player"><option value="">—</option>
        ${players.map((p) => html`<option value="${p.steamId}">${p.name} (${p.playfield || "?"})</option>`)}</select>
        <h4>${t("sa_coords")}</h4>${coords}`,
      (f) => post(f.player.value ? { toPlayer: f.player.value } : { playfield: f.pf.value, x: +f.x.value, y: +f.y.value, z: +f.z.value }));
    }).catch(fail);
  }
}

async function libraryDialog(s, me) {
  let lib;
  try { lib = await api.get(`/api/servers/${s.id}/library`); } catch (ex) { fail(ex); return; }
  const admin = atLeast(me.role, "admin");
  const dlg = dialog(t("lib_title"), html`<div class="hint">${t("lib_hint")}</div>
    <h4>${t("lib_structures")}</h4>${lib.structures.length ? html`<div class="table-wrap"><table><tbody>${lib.structures.map((e) => html`<tr>
      <td><b>${e.structureName}</b> <span class="hint">${e.structureId} · ${["", "", "BA", "CV", "SV", "HV"][e.type] || e.type}</span><div class="hint">${when(e.createdUtc)} · ${e.by}${e.note ? " · " + e.note : ""}</div></td>
      <td class="hint">${e.playfield}</td><td>${formatBytes(e.size)}</td>
      <td class="row-actions"><button type="button" class="btn small" data-spawn-lib="${e.name}">${t("lib_spawn")}</button>
      ${admin ? html`<button type="button" class="btn small danger" data-del-lib="${e.name}">✕</button>` : ""}</td></tr>`)}</tbody></table></div>` : html`<div class="hint">${t("lib_none")}</div>`}
    <h4>${t("lib_blueprints")}</h4>${lib.blueprints.length ? html`<div class="table-wrap"><table><tbody>${lib.blueprints.map((b) => html`<tr>
      <td><b>${b.name}</b> <span class="hint">${["", "", "BA", "CV", "SV", "HV"][b.type] || "?"}</span></td><td>${formatBytes(b.size)}</td>
      <td class="row-actions"><button type="button" class="btn small" data-spawn-bp="${b.name}" data-type="${b.type}">${t("lib_spawn")}</button>
      ${admin ? html`<button type="button" class="btn small danger" data-del-bp="${b.name}">✕</button>` : ""}</td></tr>`)}</tbody></table></div>` : html`<div class="hint">${t("lib_none")}</div>`}
    ${admin ? html`<label>${t("lib_upload")}</label><input type="file" id="bp-file" accept=".epb">` : ""}`, null, "", false, true);
  const again = () => { dlg.close(); libraryDialog(s, me); };
  $$("[data-del-lib]", dlg).forEach((b) => b.addEventListener("click", async () => {
    if (!confirm(`${t("act_delete")} ?`)) return;
    try { await api.post(`/api/servers/${s.id}/library/delete`, { name: b.dataset.delLib }); again(); } catch (ex) { fail(ex); }
  }));
  $$("[data-del-bp]", dlg).forEach((b) => b.addEventListener("click", async () => {
    if (!confirm(`${t("act_delete")} ?`)) return;
    try { await api.post(`/api/servers/${s.id}/blueprints/delete`, { name: b.dataset.delBp }); again(); } catch (ex) { fail(ex); }
  }));
  $("#bp-file", dlg)?.addEventListener("change", async (e) => {
    const file = e.target.files[0];
    if (!file) return;
    try { await api.upload(`/api/servers/${s.id}/blueprints`, file); toast(t("done")); again(); } catch (ex) { fail(ex); }
  });
  const spawn = (source, name, type) => dialog(t("sp_title", name), html`
    <label>${t("sp_pf")}</label><input name="pf" required>
    <div class="form-row three"><div><label>X</label><input name="x" type="number" step="any" value="0"></div><div><label>Y</label><input name="y" type="number" step="any" value="100"></div>
      <div><label>Z</label><input name="z" type="number" step="any" value="0"></div></div>
    <label>${t("sp_name")}</label><input name="name" maxlength="60">
    <label>${t("st_type")}</label><select name="type">${[2, 3, 4, 5].map((v) => html`<option value="${v}" ${raw(+type === v ? "selected" : "")}>${["", "", "BA", "CV", "SV", "HV"][v]}</option>`)}</select>
    <label>${t("sp_owner")}</label><select name="fg"><option value="5">${t("owner_admin")}</option><option value="8">${t("owner_alien")}</option>
      <option value="0">${t("owner_faction")}</option><option value="1">${t("owner_player")}</option></select>
    <label>${t("sp_owner_id")}</label><input name="fid" type="number" value="0">`,
  async (f) => {
    const r = await api.post(`/api/servers/${s.id}/library/spawn`, { source, name, playfield: f.pf.value, x: +f.x.value, y: +f.y.value, z: +f.z.value,
      type: +f.type.value, factionGroup: +f.fg.value, factionId: +f.fid.value, newName: f.name.value });
    toast(`${t("done")} (${r.id})`);
  }, t("lib_spawn"));
  $$("[data-spawn-lib]", dlg).forEach((b) => b.addEventListener("click", () => { dlg.close(); spawn("library", b.dataset.spawnLib, lib.structures.find((e) => e.name === b.dataset.spawnLib)?.type); }));
  $$("[data-spawn-bp]", dlg).forEach((b) => b.addEventListener("click", () => { dlg.close(); spawn("blueprint", b.dataset.spawnBp, b.dataset.type); }));
}

async function sharedDialog(s) {
  let p;
  try { p = await api.get(`/api/servers/${s.id}/shared`); } catch (ex) { fail(ex); return; }
  dialog(t("sh_title"), html`<div class="hint">${t("sh_hint")}</div>
    <p>${p.count ? t("sh_found", p.count, formatBytes(p.bytes)) : t("sh_none")}</p>
    ${p.count ? html`<div class="history">${p.list.map((x) => html`<div>${x.name} <span class="hint">${formatBytes(x.size)} · ${when(x.modifiedUtc)}</span></div>`)}</div>` : ""}`,
  p.count ? async () => { const r = await api.post(`/api/servers/${s.id}/shared/clean`); toast(t("sh_done", r.count, r.target)); } : null, t("sh_clean"));
}

async function rulesView(el, s, me, back) {
  const admin = atLeast(me.role, "admin");
  let v = { limits: [], autoWipe: [] };
  try { v = await api.get(`/api/servers/${s.id}/structures/violations`); } catch (ex) { fail(ex); }
  mount(el, html`<div class="actions-row"><button class="btn" id="rl-back">${t("pf_back").replace("Playfields", t("tab_structures"))}</button></div>
    <div class="card"><h3>${t("rules_violations")}</h3>${v.limits.length ? html`<div class="table-wrap"><table><tbody>${v.limits.map((x) => html`<tr>
      <td><b>${x.structureName}</b><div class="hint">${x.structureId} · ${x.type} · ${x.playfield}</div></td><td>${x.owner}</td><td>${x.detail}<div class="hint">${x.rule}</div></td>
      <td class="hint">${x.warnedUtc ? when(x.warnedUtc) : ""}</td></tr>`)}</tbody></table></div>` : html`<div class="hint">${t("rules_none")}</div>`}
    ${v.autoWipe.length ? html`<h4>${t("rules_wipe_now")}</h4><div class="history">${v.autoWipe.map((x) => html`<div><b>${x.name}</b> ${x.id} · ${x.type} · ${x.playfield} — ${x.reason}</div>`)}</div>` : ""}</div>
    <div id="rl-form"></div>`);
  $("#rl-back", el).addEventListener("click", back);
  if (!admin) return;
  const typeOpts = [["any", "*"], ...TYPES.map((x) => [x, x])];
  await settingsForm($("#rl-form", el), `/api/servers/${s.id}/features/world`, [
    { title: t("rules_limits"), fields: [
      { k: "limitsEnabled", type: "check", label: t("rules_limits_on") },
      { k: "limits", type: "rows", label: t("rules_limit_list"), cols: [
        { k: "scope", label: t("rules_scope"), type: "select", options: [["player", t("scope_player")], ["faction", t("scope_faction")]] },
        { k: "type", label: t("st_type"), type: "select", options: typeOpts },
        { k: "playfields", label: t("rules_pf"), type: "text" },
        { k: "maxCount", label: t("rules_max_count"), type: "number" },
        { k: "maxClass", label: t("rules_max_class"), type: "number" },
        { k: "maxBlocks", label: t("rules_max_blocks"), type: "number" }] },
      { k: "limitAction", type: "select", label: t("rules_action"), options: [["warn", t("act_warn")], ["destroy", t("act_destroy")], ["sanction", t("act_sanction")]] },
      { k: "sanctionFaction", type: "text", label: t("rules_sanction_faction"), hint: t("rules_sanction_hint") },
      { k: "sanctionDestroyHours", type: "number", label: t("rules_sanction_hours") },
      { k: "totalMaxStructures", type: "number", label: t("rules_total_max") },
      { k: "limitGraceHours", type: "number", label: t("rules_grace") },
      { k: "exemptAdmins", type: "check", label: t("rules_exempt_admins") },
      { k: "exemptFactions", type: "ints", label: t("rules_exempt_factions") }] },
    { title: t("rules_autowipe"), hint: t("rules_autowipe_hint"), fields: [
      { k: "autoWipeEnabled", type: "check", label: t("rules_autowipe_on") },
      { k: "autoWipe", type: "rows", label: t("rules_autowipe_list"), cols: [
        { k: "type", label: t("st_type"), type: "select", options: typeOpts },
        { k: "playfields", label: t("rules_pf"), type: "text" },
        { k: "notVisitedDays", label: t("rules_not_visited"), type: "number" },
        { k: "olderThanHours", label: t("rules_older"), type: "number" },
        { k: "onPlayfieldHours", label: t("rules_on_pf"), type: "number" }] },
      { k: "untouchedPublicHours", type: "number", label: t("rules_public_hours"), hint: t("rules_public_hint") },
      { k: "publicFaction", type: "text", label: t("rules_public_faction") }] },
    { title: t("rules_reserved"), hint: t("rules_reserved_hint"), fields: [
      { k: "reservedEnabled", type: "check", label: t("rules_reserved_on") },
      { k: "reserved", type: "rows", label: t("rules_reserved_list"), cols: [
        { k: "playfield", label: t("col_playfield"), type: "text" },
        { k: "factions", label: t("rules_factions"), type: "ints" },
        { k: "steamIds", label: t("rules_steam"), type: "csv" },
        { k: "origins", label: t("rules_origins"), type: "ints" },
        { k: "action", label: t("rules_res_action"), type: "select", options: [["teleport", t("res_teleport")], ["kick", t("res_kick")]] },
        { k: "targetPlayfield", label: t("rules_target"), type: "text" },
        { k: "x", label: "X", type: "number" }, { k: "y", label: "Y", type: "number", default: 100 }, { k: "z", label: "Z", type: "number" }] }] },
    { title: t("tab_options"), open: false, fields: [
      { k: "refreshMinutes", type: "number", label: t("rules_refresh") },
      { k: "sharedMinAgeHours", type: "number", label: t("rules_shared_age") }] },
  ]);
}

// =================================================================== playfields, maps, galaxy
async function renderPlayfields(el, s, me) {
  let list = [];
  try { list = await api.get(`/api/servers/${s.id}/playfields`); } catch (ex) { fail(ex); }
  const admin = atLeast(me.role, "admin");
  mount(el, html`<div class="card"><div class="actions-row"><button class="btn" id="pf-galaxy">${t("pf_galaxy")}</button></div>
    <div class="table-wrap"><table><thead><tr><th>${t("col_playfield")}</th><th>${t("pf_loaded")}</th><th>${t("pf_players")}</th><th>${t("tab_structures")}</th><th></th></tr></thead>
    <tbody>${list.map((p) => html`<tr><td><b>${p.name}</b>${p.system ? html`<div class="hint">${p.system}</div>` : ""}</td>
      <td>${p.loaded ? html`<span class="state ok">${p.loaded.fps} FPS</span> <span class="hint">${p.loaded.memoryMb} Mo</span>` : html`<span class="hint">${t("pf_not_loaded")}</span>`}</td>
      <td>${p.players.join(", ")}</td><td>${p.structures}</td>
      <td class="row-actions"><button class="btn small" data-map="${p.name}">${t("pf_map")}</button>
        ${admin ? html`<button class="btn small" data-load="${p.name}">${t("pf_load")}</button>
        <select class="small" data-wipe="${p.name}"><option value="">${t("pf_wipe")}…</option>${["poi", "deposit", "terrain", "all"].map((w) => html`<option value="${w}">${t("w_" + w)}</option>`)}</select>` : ""}</td></tr>`)}</tbody></table></div></div>`);
  $("#pf-galaxy", el).addEventListener("click", () => galaxyView(el, s, me));
  $$("[data-map]", el).forEach((b) => b.addEventListener("click", () => mapView(el, s, me, b.dataset.map)));
  $$("[data-load]", el).forEach((b) => b.addEventListener("click", async () => {
    try { await api.post(`/api/servers/${s.id}/playfields/action`, { playfield: b.dataset.load, action: "load" }); toast(t("done")); } catch (ex) { fail(ex); }
  }));
  $$("[data-wipe]", el).forEach((sel) => sel.addEventListener("change", async () => {
    const w = sel.value;
    sel.value = "";
    if (!w || !confirm(t("wipe_confirm", t("w_" + w), sel.dataset.wipe))) return;
    try { await api.post(`/api/servers/${s.id}/playfields/action`, { playfield: sel.dataset.wipe, action: "wipe_" + w }); toast(t("done")); } catch (ex) { fail(ex); }
  }));
}

const COLORS = { BA: "#4fc3f7", CV: "#ffb74d", SV: "#aed581", HV: "#ce93d8", player: "#ff5252", other: "#78909c" };

/** Interactive 2D/3D point view drawn on a canvas. points: [{x,y,z,color,size,label}] */
function pointView(canvas, points, mode) {
  const ctx = canvas.getContext("2d");
  const st = { yaw: 0.6, pitch: 0.5, zoom: 1, drag: null, hover: null };
  const ext = Math.max(100, ...points.map((p) => Math.max(Math.abs(p.x), Math.abs(p.z), Math.abs(p.y))));
  const cx = points.length ? (Math.min(...points.map((p) => p.x)) + Math.max(...points.map((p) => p.x))) / 2 : 0;
  const cz = points.length ? (Math.min(...points.map((p) => p.z)) + Math.max(...points.map((p) => p.z))) / 2 : 0;
  const cy = points.length ? (Math.min(...points.map((p) => p.y)) + Math.max(...points.map((p) => p.y))) / 2 : 0;
  const span = Math.max(50, ...points.map((p) => Math.max(Math.abs(p.x - cx), Math.abs(p.z - cz), Math.abs(p.y - cy)))) * 1.15;
  const project = (p) => {
    const W = canvas.width, H = canvas.height, k = (Math.min(W, H) / 2 / span) * st.zoom;
    let x = p.x - cx, y = p.y - cy, z = p.z - cz;
    if (mode() === "2d") return { sx: W / 2 + x * k, sy: H / 2 - z * k, d: 0 };
    const c = Math.cos(st.yaw), s = Math.sin(st.yaw);
    [x, z] = [x * c - z * s, x * s + z * c];
    const cp = Math.cos(st.pitch), sp = Math.sin(st.pitch);
    [y, z] = [y * cp - z * sp, y * sp + z * cp];
    return { sx: W / 2 + x * k, sy: H / 2 - y * k, d: z };
  };
  const draw = () => {
    const W = (canvas.width = canvas.clientWidth * devicePixelRatio), H = (canvas.height = canvas.clientHeight * devicePixelRatio);
    ctx.clearRect(0, 0, W, H);
    ctx.strokeStyle = "rgba(128,128,128,.25)";
    ctx.lineWidth = 1;
    if (mode() === "2d") {
      for (let i = -4; i <= 4; i++) {
        const a = project({ x: cx + (i * span) / 4, y: cy, z: cz - span }), b = project({ x: cx + (i * span) / 4, y: cy, z: cz + span });
        const c = project({ x: cx - span, y: cy, z: cz + (i * span) / 4 }), d = project({ x: cx + span, y: cy, z: cz + (i * span) / 4 });
        ctx.beginPath(); ctx.moveTo(a.sx, a.sy); ctx.lineTo(b.sx, b.sy); ctx.moveTo(c.sx, c.sy); ctx.lineTo(d.sx, d.sy); ctx.stroke();
      }
    } else {
      const axes = [[{ x: cx - span, y: cy, z: cz }, { x: cx + span, y: cy, z: cz }], [{ x: cx, y: cy, z: cz - span }, { x: cx, y: cy, z: cz + span }], [{ x: cx, y: cy - span, z: cz }, { x: cx, y: cy + span, z: cz }]];
      axes.forEach(([a, b]) => { const p = project(a), q = project(b); ctx.beginPath(); ctx.moveTo(p.sx, p.sy); ctx.lineTo(q.sx, q.sy); ctx.stroke(); });
    }
    const pr = points.map((p) => ({ p, ...project(p) })).sort((a, b) => b.d - a.d);
    for (const q of pr) {
      ctx.fillStyle = q.p.color;
      ctx.beginPath();
      ctx.arc(q.sx, q.sy, (q.p.size ?? 4) * devicePixelRatio, 0, Math.PI * 2);
      ctx.fill();
      if (q.p.label && (q.p.always || st.hover === q.p)) {
        ctx.fillStyle = getComputedStyle(canvas).color;
        ctx.font = `${12 * devicePixelRatio}px sans-serif`;
        ctx.fillText(q.p.label, q.sx + 6 * devicePixelRatio, q.sy - 6 * devicePixelRatio);
      }
    }
    st.projected = pr;
  };
  canvas.onmousedown = (e) => { st.drag = { x: e.clientX, y: e.clientY }; };
  window.addEventListener("mouseup", () => { st.drag = null; });
  canvas.onmousemove = (e) => {
    if (st.drag && mode() === "3d") {
      st.yaw += (e.clientX - st.drag.x) * 0.01;
      st.pitch = Math.max(-1.5, Math.min(1.5, st.pitch + (e.clientY - st.drag.y) * 0.01));
      st.drag = { x: e.clientX, y: e.clientY };
      draw();
      return;
    }
    const r = canvas.getBoundingClientRect();
    const mx = (e.clientX - r.left) * devicePixelRatio, my = (e.clientY - r.top) * devicePixelRatio;
    const hit = (st.projected || []).filter((q) => Math.hypot(q.sx - mx, q.sy - my) < 8 * devicePixelRatio).pop();
    if ((hit?.p ?? null) !== st.hover) { st.hover = hit?.p ?? null; draw(); }
  };
  canvas.onwheel = (e) => { e.preventDefault(); st.zoom = Math.max(0.2, Math.min(40, st.zoom * (e.deltaY < 0 ? 1.2 : 1 / 1.2))); draw(); };
  void ext;
  draw();
  return draw;
}

async function mapView(el, s, me, pf) {
  let m;
  try { m = await api.get(`/api/servers/${s.id}/playfields/map?pf=${encodeURIComponent(pf)}`); } catch (ex) { fail(ex); return; }
  let mode = "2d";
  const pts = [
    ...(m.entities ?? []).map((e) => ({ x: e.x, y: e.y, z: e.z, color: COLORS.other, size: 2, label: `#${e.id} (type ${e.type})` })),
    ...m.structures.map((x) => ({ x: x.x, y: x.y, z: x.z, color: COLORS[x.type] ?? COLORS.other, size: x.type === "BA" ? 5 : 4, label: `${x.name} (${x.type}, ${x.owner})` })),
    ...m.players.map((p) => ({ x: p.x, y: p.y, z: p.z, color: COLORS.player, size: 5, label: p.name, always: true })),
  ];
  mount(el, html`<div class="card"><div class="actions-row wrap"><button class="btn" id="mp-back">${t("pf_back")}</button><b>${pf}</b>
      <button class="btn small active" data-mode="2d">${t("map_2d")}</button><button class="btn small" data-mode="3d">${t("map_3d")}</button></div>
    <canvas class="map" id="mp-canvas"></canvas><div class="hint">${t("map_hint")}</div>
    <div class="legend">${TYPES.map((x) => html`<span style="color:${COLORS[x]}">● ${x}</span>`)} <span style="color:${COLORS.player}">● ${t("map_players")}</span>
      <span style="color:${COLORS.other}">● ${t("map_other")}</span></div></div>
    <div class="card"><h3>${t("map_structures")} (${m.structures.length})</h3><div class="table-wrap"><table><tbody>${m.structures.map((x) => html`<tr>
      <td><b>${x.name}</b> <span class="hint">${x.id}</span></td><td>${x.type}</td><td>${x.owner}</td><td class="hint">${x.x}, ${x.y}, ${x.z}</td></tr>`)}</tbody></table></div></div>`);
  $("#mp-back", el).addEventListener("click", () => renderPlayfields(el, s, me));
  const redraw = pointView($("#mp-canvas", el), pts, () => mode);
  $$("[data-mode]", el).forEach((b) => b.addEventListener("click", () => {
    mode = b.dataset.mode;
    $$("[data-mode]", el).forEach((x) => x.classList.toggle("active", x === b));
    redraw();
  }));
}

async function galaxyView(el, s, me) {
  let g;
  try { g = await api.get(`/api/servers/${s.id}/galaxy`); } catch (ex) { fail(ex); return; }
  const where = new Map(g.fromStructures.map((x) => [x.name, x]));
  mount(el, html`<div class="card"><div class="actions-row"><button class="btn" id="gx-back">${t("pf_back")}</button><b>${t("pf_galaxy")}</b>
      <span class="hint">${t("gal_systems", g.systems.length || g.fromStructures.length)}</span></div>
    ${g.systems.length ? html`<canvas class="map" id="gx-canvas"></canvas><div class="hint">${t("map_hint")}</div>` : html`<div class="hint">${t("gal_none")}</div>`}</div>
    <div class="card"><div class="table-wrap"><table><tbody>${(g.systems.length ? g.systems : g.fromStructures).map((x) => html`<tr>
      <td><b>${x.name}</b></td><td class="hint">${(x.playfields || []).map((p) => (typeof p === "string" ? p : p.name)).join(", ")}</td>
      <td>${where.get(x.name)?.structures ?? x.structures ?? ""}</td></tr>`)}</tbody></table></div></div>`);
  $("#gx-back", el).addEventListener("click", () => renderPlayfields(el, s, me));
  if (g.systems.length) {
    const pts = g.systems.map((x) => ({ x: x.x, y: x.y, z: x.z, color: where.has(x.name) ? "#ffb74d" : "#78909c", size: where.has(x.name) ? 4 : 2,
      label: `${x.name}${where.has(x.name) ? ` (${where.get(x.name).structures})` : ""}`, always: where.has(x.name) && where.size < 30 }));
    pointView($("#gx-canvas", el), pts, () => "3d");
  }
}
