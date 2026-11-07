// Step 9: mod manager, gameplay (Autominer, events) and maintenance tools.
import { api } from "./api.js";
import { t, extend, currentLanguage } from "./i18n.js";
import { html, raw, mount, $, $$, toast, formatBytes } from "./dom.js";
import { atLeast, fail, when, num, dialog, settingsForm } from "./ui.js";

extend({
  fr: {
    tab_mods: "Mods", tab_gameplay: "Gameplay", tab_maintenance: "Maintenance",
    md_title: "Mods du serveur", md_hint: "Les mods actifs sont dans game\\Content\\Mods. Désactiver un mod le range dans eam\\mods-disabled, le supprimer le garde dans eam\\mods-removed. Le serveur doit être arrêté pour installer, activer, désactiver ou supprimer.",
    md_install: "Installer un mod (.zip ou .dll)", md_none: "Aucun mod installé (hors mod EAM).", md_enabled: "Actif", md_disabled: "Désactivé",
    md_enable: "Activer", md_disable: "Désactiver", md_remove: "Supprimer", md_configs: "Fichiers de réglages", md_edit: "Modifier {0}",
    md_confirm_remove: "Supprimer le mod {0} ? Il sera gardé dans eam\\mods-removed.", md_installed: "Mod {0} installé.", md_running: "Arrêtez le serveur pour changer les mods.",
    md_saved_cfg: "Enregistré (copie précédente : .eam-backup). Redémarrez le serveur pour l'appliquer.",
    gp_autominer: "Autominer", gp_am_on: "Activer l'Autominer", gp_am_prefix: "Préfixe des commandes", gp_am_max: "Niveau maximum",
    gp_am_fuel: "Prix du carburant (crédits par heure)", gp_am_hours: "Stock maximum (heures de production)", gp_ores: "Minerais",
    gp_ore_key: "Nom court", gp_ore_item: "Numéro d'objet", gp_ore_rate: "Par heure (niveau 1)", gp_ore_price: "Prix (× niveau)",
    gp_am_hint: "Les joueurs achètent des mineurs virtuels avec leurs crédits : am:info, am:buy <minerai>, am:sell <minerai>, am:fuel <heures>, am:get <minerai>. Un mineur ne produit que s'il a du carburant.",
    gp_default_ores: "Remplir avec les 13 minerais habituels", gp_miners: "Mineurs des joueurs", gp_level: "Niveau", gp_stored: "En stock",
    gp_events: "Événements", gp_ev_on: "Activer les commandes d'événement", gp_ev_prefix: "Préfixe des commandes",
    gp_ev_hint: "Les joueurs voient les événements ouverts avec EV:list, s'inscrivent avec EV:join <n> et quittent avec EV:exit. Au départ, EAM peut garder leur inventaire, leur donner un kit et les téléporter ; à la fin, il rend l'inventaire et les ramène où ils étaient.",
    gp_new_event: "Nouvel événement", gp_ev_name: "Nom", gp_ev_pos: "Position d'arrivée (x, y, z)", gp_ev_keep: "Les joueurs gardent leur inventaire (sinon il est mis de côté puis rendu)",
    gp_ev_kit: "Kit donné au départ", gp_ev_players: "Joueurs", gp_ev_none: "Aucun événement.",
    ev_draft: "Brouillon", ev_open: "Inscriptions ouvertes", ev_running: "En cours", ev_ended: "Terminé",
    ev_open_btn: "Ouvrir les inscriptions", ev_start_btn: "Démarrer", ev_end_btn: "Terminer", ev_delete_btn: "Supprimer",
    ev_confirm_start: "Démarrer « {0} » ? Les joueurs inscrits et connectés seront téléportés.", ev_confirm_end: "Terminer « {0} » ? Les joueurs seront ramenés et leur inventaire rendu.",
    evs_registered: "inscrit", evs_in: "dans l'événement", evs_back: "revenu", evs_pending_return: "retour à la prochaine connexion",
    mt_info: "État", mt_save: "Sauvegarde", mt_save_size: "Taille de la sauvegarde", mt_cache: "Cache", mt_db: "Base EAM du serveur", mt_stopped_yes: "Serveur arrêté", mt_stopped_no: "Serveur en marche",
    mt_compact: "Compacter les bases EAM", mt_compacted: "Bases compactées.",
    mt_fresh: "Nouveau départ (nouvelle saison)", mt_fresh_hint: "Efface l'historique EAM de l'ancienne partie (joueurs, structures, journal, statistiques, mineurs, événements). Les réglages, tâches et sauvegardes restent. Une sauvegarde complète est faite avant. Le serveur doit être arrêté.",
    mt_keep_chat: "Garder le chat", mt_delete_save: "Supprimer aussi la partie et son cache (le jeu crée une nouvelle partie au prochain démarrage)",
    mt_type_name: "Tapez le nom du serveur pour confirmer : {0}", mt_fresh_btn: "Tout effacer", mt_fresh_done: "Nouveau départ fait.",
    mt_damaged: "Fichiers joueurs abîmés", mt_damaged_hint: "Fichiers .ply vides ou presque, souvent après un plantage : le joueur ne peut plus se connecter. Les déplacer (serveur arrêté) lui fait recommencer comme un nouveau joueur.",
    mt_damaged_none: "Aucun fichier abîmé.", mt_move: "Mettre de côté", mt_moved: "Déplacé dans {0}",
    mt_items: "Numéros d'objets", mt_items_hint: "Cherchez par nom ou par numéro. « Numéro de config » est celui utilisé dans les fichiers du scénario.",
    mt_search: "Rechercher", mt_id: "Numéro", mt_cfg: "Numéro de config", mt_block: "Bloc",
    err_server_must_be_stopped: "Arrêtez d'abord le serveur.", err_invalid_ore: "Minerai invalide (nom court unique, numéro et production positifs).",
    err_event_not_found: "Événement introuvable.", err_event_closed: "Cet événement n'accepte plus d'inscriptions.", err_event_running: "L'événement est en cours.",
    err_invalid_event: "Nom et playfield obligatoires.", err_unsafe_delete: "Suppression refusée (dossier hors du serveur).", err_invalid_file: "Fichier refusé (type ou taille).",
  },
  en: {
    tab_mods: "Mods", tab_gameplay: "Gameplay", tab_maintenance: "Maintenance",
    md_title: "Server mods", md_hint: "Active mods are in game\\Content\\Mods. Disabling a mod moves it to eam\\mods-disabled, removing it keeps it in eam\\mods-removed. The server must be stopped to install, enable, disable or remove.",
    md_install: "Install a mod (.zip or .dll)", md_none: "No mod installed (besides the EAM mod).", md_enabled: "Enabled", md_disabled: "Disabled",
    md_enable: "Enable", md_disable: "Disable", md_remove: "Remove", md_configs: "Settings files", md_edit: "Edit {0}",
    md_confirm_remove: "Remove mod {0}? It will be kept in eam\\mods-removed.", md_installed: "Mod {0} installed.", md_running: "Stop the server to change mods.",
    md_saved_cfg: "Saved (previous copy: .eam-backup). Restart the server to apply it.",
    gp_autominer: "Autominer", gp_am_on: "Enable the Autominer", gp_am_prefix: "Command prefix", gp_am_max: "Maximum level",
    gp_am_fuel: "Fuel price (credits per hour)", gp_am_hours: "Maximum storage (hours of production)", gp_ores: "Ores",
    gp_ore_key: "Short name", gp_ore_item: "Item number", gp_ore_rate: "Per hour (level 1)", gp_ore_price: "Price (× level)",
    gp_am_hint: "Players buy virtual miners with their credits: am:info, am:buy <ore>, am:sell <ore>, am:fuel <hours>, am:get <ore>. A miner only produces while it has fuel.",
    gp_default_ores: "Fill with the 13 usual ores", gp_miners: "Players' miners", gp_level: "Level", gp_stored: "Stored",
    gp_events: "Events", gp_ev_on: "Enable event commands", gp_ev_prefix: "Command prefix",
    gp_ev_hint: "Players see open events with EV:list, register with EV:join <n> and leave with EV:exit. At the start, EAM can keep their inventory aside, give a kit and teleport them; at the end, it gives the inventory back and brings them back where they were.",
    gp_new_event: "New event", gp_ev_name: "Name", gp_ev_pos: "Arrival position (x, y, z)", gp_ev_keep: "Players keep their inventory (otherwise it is set aside then given back)",
    gp_ev_kit: "Kit given at the start", gp_ev_players: "Players", gp_ev_none: "No event.",
    ev_draft: "Draft", ev_open: "Registration open", ev_running: "Running", ev_ended: "Ended",
    ev_open_btn: "Open registration", ev_start_btn: "Start", ev_end_btn: "End", ev_delete_btn: "Delete",
    ev_confirm_start: "Start \"{0}\"? Registered players who are online will be teleported.", ev_confirm_end: "End \"{0}\"? Players will be brought back and get their inventory back.",
    evs_registered: "registered", evs_in: "in the event", evs_back: "back", evs_pending_return: "back at next login",
    mt_info: "Status", mt_save: "Save game", mt_save_size: "Save size", mt_cache: "Cache", mt_db: "Server's EAM database", mt_stopped_yes: "Server stopped", mt_stopped_no: "Server running",
    mt_compact: "Compact EAM databases", mt_compacted: "Databases compacted.",
    mt_fresh: "Fresh start (new season)", mt_fresh_hint: "Clears EAM's history of the old game (players, structures, journal, statistics, miners, events). Settings, tasks and backups stay. A full backup is made first. The server must be stopped.",
    mt_keep_chat: "Keep the chat", mt_delete_save: "Also delete the game and its cache (the game creates a new one at next start)",
    mt_type_name: "Type the server's name to confirm: {0}", mt_fresh_btn: "Clear everything", mt_fresh_done: "Fresh start done.",
    mt_damaged: "Damaged player files", mt_damaged_hint: "Empty or almost empty .ply files, often after a crash: the player can no longer log in. Moving them aside (server stopped) makes them start as a new player.",
    mt_damaged_none: "No damaged file.", mt_move: "Move aside", mt_moved: "Moved to {0}",
    mt_items: "Item numbers", mt_items_hint: "Search by name or number. \"Config number\" is the one used in the scenario's files.",
    mt_search: "Search", mt_id: "Number", mt_cfg: "Config number", mt_block: "Block",
    err_server_must_be_stopped: "Stop the server first.", err_invalid_ore: "Invalid ore (unique short name, positive number and production).",
    err_event_not_found: "Event not found.", err_event_closed: "This event no longer accepts registrations.", err_event_running: "The event is running.",
    err_invalid_event: "Name and playfield required.", err_unsafe_delete: "Delete refused (folder outside the server).", err_invalid_file: "File refused (type or size).",
  },
});

export function extraTabs(role) {
  const tabs = [];
  if (atLeast(role, "gamemaster")) tabs.push("gameplay");
  if (atLeast(role, "admin")) tabs.push("mods", "maintenance");
  return tabs;
}

export async function renderExtra(tab, el, s, me) {
  if (tab === "mods") return mods(el, s, me);
  if (tab === "gameplay") return gameplay(el, s, me);
  if (tab === "maintenance") return maintenance(el, s, me);
}

const base = (s) => `/api/servers/${s.id}`;
const confirmDialog = (title, text, okLabel, action) =>
  dialog(title, html`<p>${text}</p>`, async () => { await action(); }, okLabel, true);

// ------------------------------------------------------------------ mods
async function mods(el, s, me) {
  let r;
  try { r = await api.get(`${base(s)}/mods`); } catch (ex) { fail(ex); return; }
  const owner = atLeast(me.role, "owner");
  const again = () => mods(el, s, me);
  mount(el, html`<div class="card"><h3>${t("md_title")}</h3><div class="hint">${t("md_hint")}</div>
    ${r.stopped ? "" : html`<div class="badge warn">${t("md_running")}</div>`}
    ${owner ? html`<div class="actions-row"><label class="btn ${r.stopped ? "" : "disabled"}">${t("md_install")}<input type="file" id="md-file" accept=".zip,.dll" hidden ${raw(r.stopped ? "" : "disabled")}></label></div>` : ""}
    ${r.mods.length ? html`<div class="table-wrap"><table><thead><tr><th>${t("col_name")}</th><th>${t("version")}</th><th></th><th></th><th></th></tr></thead><tbody>
      ${r.mods.map((m) => html`<tr><td><b>${m.name}</b>${m.author ? html`<div class="hint">${m.author}</div>` : ""}${m.description ? html`<div class="hint">${m.description}</div>` : ""}</td>
        <td>${m.version ?? "—"}</td><td>${formatBytes(m.size)}<div class="hint">${when(m.modifiedUtc)}</div></td>
        <td><span class="badge ${m.enabled ? "ok" : ""}">${m.enabled ? t("md_enabled") : t("md_disabled")}</span>${m.hasDll ? html` <span class="badge">DLL</span>` : ""}</td>
        <td class="actions">${m.configs.length ? html`<button class="btn small" data-cfg="${m.name}">${t("md_configs")} (${m.configs.length})</button>` : ""}
          <button class="btn small" data-toggle="${m.name}" data-on="${m.enabled ? "0" : "1"}" ${raw(r.stopped ? "" : "disabled")}>${m.enabled ? t("md_disable") : t("md_enable")}</button>
          ${owner ? html`<button class="btn small danger" data-remove="${m.name}" ${raw(r.stopped ? "" : "disabled")}>${t("md_remove")}</button>` : ""}</td></tr>`)}
    </tbody></table></div>` : html`<div class="hint">${t("md_none")}</div>`}</div><div id="md-editor"></div>`);
  $("#md-file", el)?.addEventListener("change", async (e) => {
    const file = e.target.files[0];
    if (!file) return;
    try { const res = await api.upload(`${base(s)}/mods`, file); toast(t("md_installed", res.name)); again(); } catch (ex) { fail(ex); }
  });
  $$("[data-toggle]", el).forEach((b) => b.addEventListener("click", async () => {
    try { await api.post(`${base(s)}/mods/toggle`, { name: b.dataset.toggle, enabled: b.dataset.on === "1" }); again(); } catch (ex) { fail(ex); }
  }));
  $$("[data-remove]", el).forEach((b) => b.addEventListener("click", () =>
    confirmDialog(t("md_remove"), t("md_confirm_remove", b.dataset.remove), t("md_remove"), async () => { await api.post(`${base(s)}/mods/remove`, { name: b.dataset.remove }); again(); })));
  $$("[data-cfg]", el).forEach((b) => b.addEventListener("click", () => {
    const m = r.mods.find((x) => x.name === b.dataset.cfg);
    configEditor($("#md-editor", el), s, m);
  }));
}

function configEditor(box, s, mod) {
  mount(box, html`<div class="card"><h3>${mod.name}</h3><div class="actions-row"><select id="md-cfg-file">${mod.configs.map((c) => html`<option>${c}</option>`)}</select></div>
    <textarea id="md-cfg-text" rows="22" spellcheck="false" class="mono"></textarea>
    <div class="actions-row"><button class="btn primary" id="md-cfg-save">${t("save")}</button></div></div>`);
  const load = async () => {
    try {
      const r = await api.get(`${base(s)}/mods/config?name=${encodeURIComponent(mod.name)}&file=${encodeURIComponent($("#md-cfg-file", box).value)}`);
      $("#md-cfg-text", box).value = r.text;
    } catch (ex) { fail(ex); }
  };
  $("#md-cfg-file", box).addEventListener("change", load);
  $("#md-cfg-save", box).addEventListener("click", async () => {
    try { await api.put(`${base(s)}/mods/config`, { name: mod.name, file: $("#md-cfg-file", box).value, text: $("#md-cfg-text", box).value }); toast(t("md_saved_cfg")); }
    catch (ex) { fail(ex); }
  });
  load();
  box.scrollIntoView({ behavior: "smooth" });
}

// ------------------------------------------------------------------ gameplay
async function gameplay(el, s, me) {
  mount(el, html`<div id="gp-events"></div><div class="card" id="gp-miners"></div><div id="gp-form"></div>`);
  await Promise.all([events($("#gp-events", el), s), miners($("#gp-miners", el), s)]);
  if (!atLeast(me.role, "admin")) return;
  const url = `${base(s)}/features/gameplay`;
  await settingsForm($("#gp-form", el), url, [
    { title: t("gp_autominer"), hint: t("gp_am_hint"), fields: [
      { k: "autominerEnabled", type: "check", label: t("gp_am_on") },
      { k: "autominerPrefix", type: "text", label: t("gp_am_prefix") },
      { k: "autominerMaxLevel", type: "number", label: t("gp_am_max") },
      { k: "fuelPricePerHour", type: "number", label: t("gp_am_fuel") },
      { k: "maxStoredHours", type: "number", label: t("gp_am_hours") },
      { k: "ores", type: "rows", label: t("gp_ores"), cols: [
        { k: "key", label: t("gp_ore_key"), type: "text" }, { k: "itemId", label: t("gp_ore_item"), type: "number" },
        { k: "perHour", label: t("gp_ore_rate"), type: "number", default: 10 }, { k: "price", label: t("gp_ore_price"), type: "number", default: 10000 }] }] },
    { title: t("gp_events"), hint: t("gp_ev_hint"), open: false, fields: [
      { k: "eventsEnabled", type: "check", label: t("gp_ev_on") },
      { k: "eventsPrefix", type: "text", label: t("gp_ev_prefix") }] },
  ]);
  const first = $("#gp-form details", el);
  const b = document.createElement("div");
  b.className = "actions-row";
  mount(b, html`<button type="button" class="btn" id="gp-ores">${t("gp_default_ores")}</button>`);
  first?.appendChild(b);
  $("#gp-ores", el)?.addEventListener("click", async () => {
    try {
      const ores = await api.get(`${base(s)}/autominer/default-ores`);
      const current = await api.get(url);
      await api.put(url, { ...current, ores });
      toast(t("saved"));
      gameplay(el, s, me);
    } catch (ex) { fail(ex); }
  });
}

async function miners(box, s) {
  let list = [];
  try { list = await api.get(`${base(s)}/autominer`); } catch (ex) { fail(ex); }
  mount(box, html`<h3>${t("gp_miners")}</h3>${list.length ? html`<div class="table-wrap"><table><thead><tr><th>${t("cm_player")}</th><th>${t("gp_ore_key")}</th>
    <th>${t("gp_level")}</th><th>${t("gp_stored")}</th><th></th></tr></thead><tbody>${list.map((m) => html`<tr><td>${m.name || m.steamId}</td><td>${m.ore}</td>
    <td>${m.level}</td><td>${num(m.stored)}</td><td class="hint">${when(m.updatedUtc)}</td></tr>`)}</tbody></table></div>` : html`<div class="hint">${t("cm_none")}</div>`}`);
}

function eventFields(ev = {}) {
  const kit = ev.kit ?? [];
  return html`<label>${t("gp_ev_name")}</label><input name="name" required maxlength="60" value="${ev.name ?? ""}">
    <label>${t("col_playfield")}</label><input name="pf" required value="${ev.playfield ?? ""}">
    <label>${t("gp_ev_pos")}</label><div class="form-row"><input name="x" type="number" step="any" value="${ev.x ?? 0}"><input name="y" type="number" step="any" value="${ev.y ?? 0}"><input name="z" type="number" step="any" value="${ev.z ?? 0}"></div>
    <label class="check"><input type="checkbox" name="keep" ${raw(ev.keepInventory ? "checked" : "")}> ${t("gp_ev_keep")}</label>
    <label>${t("gp_ev_kit")}</label><textarea name="kit" rows="4" placeholder="4097 x 10">${kit.map((k) => `${k.id} x ${k.count}`).join("\n")}</textarea>
    <div class="hint">${currentLanguage() === "fr" ? "Une ligne par objet : numéro x quantité." : "One line per item: number x count."}</div>`;
}

function readEvent(f) {
  const kit = f.kit.value.split("\n").map((l) => l.match(/(\d+)\s*[x×*,; ]\s*(\d+)/i) || l.match(/^\s*(\d+)\s*$/)).filter(Boolean)
    .map((m) => ({ id: +m[1], count: +(m[2] ?? 1) }));
  return { name: f.name.value.trim(), playfield: f.pf.value.trim(), x: +f.x.value, y: +f.y.value, z: +f.z.value, keepInventory: f.keep.checked, kit };
}

async function events(box, s) {
  let list = [];
  try { list = await api.get(`${base(s)}/events`); } catch (ex) { fail(ex); }
  const again = () => events(box, s);
  const act = async (e, action) => { await api.post(`${base(s)}/events/${e.id}/action`, { action }); again(); };
  mount(box, html`<div class="card"><h3>${t("gp_events")}</h3><div class="actions-row"><button class="btn primary" id="ev-new">${t("gp_new_event")}</button></div>
    ${list.length ? list.map((e) => html`<div class="card inner"><div class="actions-row"><b>#${e.id} ${e.name}</b><span class="badge ${e.state === "running" ? "ok" : e.state === "open" ? "warn" : ""}">${t("ev_" + e.state)}</span>
      <span class="hint">${e.playfield} (${Math.round(e.x)}, ${Math.round(e.y)}, ${Math.round(e.z)})</span><span class="spacer"></span>
      ${e.state === "draft" || e.state === "ended" ? html`<button class="btn small" data-ev-edit="${e.id}">${t("edit")}</button>` : ""}
      ${e.state === "draft" || e.state === "ended" ? html`<button class="btn small" data-ev="${e.id}" data-a="open">${t("ev_open_btn")}</button>` : ""}
      ${e.state === "open" || e.state === "draft" ? html`<button class="btn small primary" data-ev="${e.id}" data-a="start">${t("ev_start_btn")}</button>` : ""}
      ${e.state === "running" ? html`<button class="btn small primary" data-ev="${e.id}" data-a="end">${t("ev_end_btn")}</button>` : ""}
      ${e.state !== "running" ? html`<button class="btn small danger" data-ev="${e.id}" data-a="delete">${t("ev_delete_btn")}</button>` : ""}</div>
      ${e.players.length ? html`<div class="hint">${t("gp_ev_players")} : ${e.players.map((p) => `${p.name || p.steamId} (${t("evs_" + p.state)})`).join(", ")}</div>` : ""}
      ${e.kit?.length ? html`<div class="hint">${t("gp_ev_kit")} : ${e.kit.map((k) => `${k.id} × ${k.count}`).join(", ")}</div>` : ""}</div>`)
    : html`<div class="hint">${t("gp_ev_none")}</div>`}</div>`);
  $("#ev-new", box).addEventListener("click", () => dialog(t("gp_new_event"), eventFields(), async (f) => { await api.post(`${base(s)}/events`, readEvent(f)); again(); }));
  $$("[data-ev-edit]", box).forEach((b) => b.addEventListener("click", () => {
    const e = list.find((x) => x.id === +b.dataset.evEdit);
    dialog(e.name, eventFields(e), async (f) => { await api.put(`${base(s)}/events/${e.id}`, readEvent(f)); again(); });
  }));
  $$("[data-ev]", box).forEach((b) => b.addEventListener("click", async () => {
    const e = list.find((x) => x.id === +b.dataset.ev);
    const a = b.dataset.a;
    if (a === "start") return confirmDialog(t("ev_start_btn"), t("ev_confirm_start", e.name), t("ev_start_btn"), () => act(e, a));
    if (a === "end") return confirmDialog(t("ev_end_btn"), t("ev_confirm_end", e.name), t("ev_end_btn"), () => act(e, a));
    if (a === "delete") return confirmDialog(t("ev_delete_btn"), `${t("confirm_delete")} ${e.name}`, t("delete"), () => act(e, a));
    try { await act(e, a); } catch (ex) { fail(ex); }
  }));
}

// ------------------------------------------------------------------ maintenance
async function maintenance(el, s, me) {
  let info;
  try { info = await api.get(`${base(s)}/maintenance`); } catch (ex) { fail(ex); return; }
  const again = () => maintenance(el, s, me);
  mount(el, html`<div class="card"><h3>${t("mt_info")}</h3>
      <dl class="kv"><dt>${t("mt_save")}</dt><dd>${info.game}${info.saveExists ? "" : " (—)"}</dd><dt>${t("mt_save_size")}</dt><dd>${formatBytes(info.saveSize)}</dd>
      <dt>${t("mt_cache")}</dt><dd>${formatBytes(info.cacheSize)}</dd><dt>${t("mt_db")}</dt><dd>${formatBytes(info.dbSize)}</dd></dl>
      <span class="badge ${info.stopped ? "ok" : "warn"}">${info.stopped ? t("mt_stopped_yes") : t("mt_stopped_no")}</span>
      <div class="actions-row"><button class="btn" id="mt-compact">${t("mt_compact")}</button></div></div>
    <div class="card"><h3>${t("mt_items")}</h3><div class="hint">${t("mt_items_hint")}</div>
      <form class="actions-row" id="mt-isearch"><input name="q" placeholder="${t("mt_search")}…"><button class="btn">${t("mt_search")}</button></form><div id="mt-items"></div></div>
    <div class="card"><h3>${t("mt_damaged")}</h3><div class="hint">${t("mt_damaged_hint")}</div><div id="mt-damaged"></div></div>
    ${atLeast(me.role, "owner") ? html`<div class="card"><h3>${t("mt_fresh")}</h3><div class="hint">${t("mt_fresh_hint")}</div>
      <div class="actions-row"><button class="btn danger" id="mt-fresh" ${raw(info.stopped ? "" : "disabled")}>${t("mt_fresh")}</button></div></div>` : ""}`);

  $("#mt-compact", el).addEventListener("click", async (e) => {
    e.target.disabled = true;
    try { await api.post(`${base(s)}/maintenance/compact`); toast(t("mt_compacted")); again(); } catch (ex) { fail(ex); }
    e.target.disabled = false;
  });

  $("#mt-isearch", el).addEventListener("submit", async (e) => {
    e.preventDefault();
    try {
      const list = await api.get(`${base(s)}/maintenance/items?q=${encodeURIComponent(e.target.q.value)}&lang=${currentLanguage()}`);
      mount($("#mt-items", el), html`<div class="table-wrap"><table><thead><tr><th>${t("mt_id")}</th><th>${t("mt_cfg")}</th><th>${t("col_name")}</th><th></th></tr></thead>
        <tbody>${list.map((i) => html`<tr><td>${i.id}</td><td>${i.configId}</td><td><b>${i.label || i.name}</b><div class="hint">${i.name}</div></td><td>${i.isBlock ? t("mt_block") : ""}</td></tr>`)}</tbody></table></div>`);
    } catch (ex) { fail(ex); }
  });

  try {
    const dmg = await api.get(`${base(s)}/maintenance/damaged-players`);
    mount($("#mt-damaged", el), dmg.length ? html`<div class="table-wrap"><table><tbody>${dmg.map((d) => html`<tr><td><b>${d.name || "?"}</b><div class="hint">${d.file}</div></td>
      <td>${formatBytes(d.size)}</td><td class="hint">${when(d.modifiedUtc)}</td><td><button class="btn small" data-move="${d.file}" ${raw(info.stopped ? "" : "disabled")}>${t("mt_move")}</button></td></tr>`)}</tbody></table></div>`
      : html`<div class="hint">${t("mt_damaged_none")}</div>`);
    $$("[data-move]", el).forEach((b) => b.addEventListener("click", async () => {
      try { const r = await api.post(`${base(s)}/maintenance/damaged-players/move`, { file: b.dataset.move }); toast(t("mt_moved", r.target)); again(); } catch (ex) { fail(ex); }
    }));
  } catch (ex) { fail(ex); }

  $("#mt-fresh", el)?.addEventListener("click", () => dialog(t("mt_fresh"), html`<div class="hint">${t("mt_fresh_hint")}</div>
      <label class="check"><input type="checkbox" name="chat" checked> ${t("mt_keep_chat")}</label>
      <label class="check"><input type="checkbox" name="save"> ${t("mt_delete_save")}</label>
      <label>${t("mt_type_name", s.name)}</label><input name="confirm" autocomplete="off" required>`,
  async (f) => {
    await api.post(`${base(s)}/maintenance/fresh-start`, { confirm: f.confirm.value, keepChat: f.chat.checked, deleteSave: f.save.checked });
    toast(t("mt_fresh_done"));
    again();
  }, t("mt_fresh_btn"), true));
}
