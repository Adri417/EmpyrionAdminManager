// Step 8: journal and alerts, anti-cheat, attacks, poles, emergency teleport, flood protection,
// "what happened here", statistics over 7 days.
import { api } from "./api.js";
import { t, extend } from "./i18n.js";
import { html, raw, mount, $, $$, toast } from "./dom.js";
import { atLeast, fail, when, settingsForm, lineChart } from "./ui.js";

const CATS = ["alert", "cheat", "attack", "game", "console", "filter", "mute", "chatbot", "limit", "autowipe", "reserved", "pole", "emergency", "flood", "structure", "playfield", "maintenance", "event", "autominer"];

extend({
  fr: {
    tab_security: "Sécurité", tab_history: "Historique", tab_stats: "Statistiques",
    se_journal: "Journal et alertes", se_all: "Toutes les catégories", se_min: "Importance", sev_0: "Tout", sev_1: "Avertissements", sev_2: "Graves",
    se_more: "Plus ancien", se_scan: "Analyser les inventaires maintenant", se_scan_none: "Rien de suspect.", se_scan_found: "{0} joueur(s) suspect(s), voir le journal.",
    se_anticheat: "Anti-triche", se_ac_on: "Activer l'analyse automatique", se_ac_every: "Analyser toutes les (minutes)",
    se_ac_hint: "EAM lit l'inventaire des joueurs connectés et note ce qui est suspect : peut-être, élevé, extrême. Les admins peuvent être exemptés.",
    se_rules: "Objets surveillés", se_item: "Numéro d'objet", se_max: "Quantité max (0 = dès 1)", se_level: "Niveau", lvl_1: "Peut-être", lvl_2: "Élevé", lvl_3: "Extrême",
    se_maxstack: "Pile impossible au-delà de (0 = pas de contrôle)", se_white: "Objets jamais suspects (numéros)", se_credits: "Gain de crédits suspect entre deux analyses (0 = pas de contrôle)",
    se_autoban: "Bannir automatiquement les cas « extrême »", se_ban_dur: "Durée du bannissement", se_exempt: "Ne pas analyser les admins, modérateurs et maîtres du jeu",
    se_attacks: "Journal des attaques", se_att_on: "Noter qui approche des bases des autres", se_radius: "Distance au cœur de la base (mètres)", se_warn_owner: "Prévenir les propriétaires connectés",
    se_poles: "Contrôle des pôles", se_poles_on: "Activer", se_poles_pf: "Playfields concernés (* possible)", se_pole_limit: "Le pôle commence à |z| supérieur à",
    se_pole_alert: "Alerte après (secondes)", se_pole_move: "Déplacement hors du pôle après (secondes)", se_poles_hint: "À régler selon la taille des planètes de votre scénario. Désactivé par défaut.",
    se_emergency: "Téléportation d'urgence", se_em_on: "Activer", se_em_fail: "Connexions ratées avant téléportation", se_em_window: "Sur une période de (minutes)",
    se_em_pf: "Playfield de secours (vide = planète de départ du joueur)", se_em_hint: "Quand un joueur n'arrive plus à se connecter (playfield planté), EAM l'envoie ailleurs à la connexion suivante.",
    se_flood: "Protection « telnet flood »", se_flood_on: "Redémarrer le serveur quand le journal du jeu boucle sur ce texte", se_flood_text: "Texte", se_flood_n: "Nombre de fois par minute",
    se_pos: "Relever la position des joueurs toutes les (secondes)",
    hi_title: "Que s'est-il passé ici ?", hi_pf: "Playfield", hi_radius: "Rayon (m)", hi_from: "Du", hi_to: "Au", hi_search: "Chercher",
    hi_hint: "Joueurs et structures passés dans la zone (positions gardées 14 jours), avec ce qui est arrivé aux structures.",
    hi_who: "Présents dans la zone", hi_events: "Événements des structures", hi_journal: "Journal du playfield", hi_closest: "au plus près", hi_none: "Rien trouvé.",
    hi_player: "Joueur", hi_structure: "Structure", hi_track: "Parcours d'un joueur", hi_track_go: "Voir le parcours",
    stt_title: "Statistiques du serveur (7 jours)", stt_cpu: "Processeur (%)", stt_mem: "Mémoire (Mo)", stt_players: "Joueurs", stt_pf: "Playfields chargés",
    stt_fps: "FPS", stt_pf_table: "Playfields", stt_avg_fps: "FPS moyen", stt_min_fps: "FPS min", stt_max_mem: "Mémoire max", stt_max_players: "Joueurs max",
    stt_hint: "Relevé toutes les 10 minutes quand le serveur est en ligne.", stt_tips: "Conseils",
    tip_cpu: "Le processeur est très chargé en moyenne : limitez le nombre de playfields ou de structures.",
    tip_fps: "Certains playfields tournent sous 15 FPS en moyenne : regardez leurs structures (limites, nettoyage).",
    tip_playfields: "Plus de 15 playfields chargés en même temps : pensez à les décharger plus tôt dans les options du serveur.",
    err_invalid_scan: "Intervalle invalide.", err_invalid_cheat_rule: "Règle anti-triche invalide.", err_invalid_radius: "Distance invalide.",
    err_invalid_poles: "Réglages des pôles invalides.", err_invalid_emergency: "Réglages de la téléportation d'urgence invalides.", err_invalid_flood: "Réglages anti-flood invalides.",
  },
  en: {
    tab_security: "Security", tab_history: "History", tab_stats: "Statistics",
    se_journal: "Journal and alerts", se_all: "All categories", se_min: "Importance", sev_0: "All", sev_1: "Warnings", sev_2: "Serious",
    se_more: "Older", se_scan: "Scan inventories now", se_scan_none: "Nothing suspicious.", se_scan_found: "{0} suspicious player(s), see the journal.",
    se_anticheat: "Anti-cheat", se_ac_on: "Enable automatic scans", se_ac_every: "Scan every (minutes)",
    se_ac_hint: "EAM reads the inventory of online players and rates what looks suspicious: maybe, high, extreme. Admins can be exempted.",
    se_rules: "Watched items", se_item: "Item number", se_max: "Max quantity (0 = from 1)", se_level: "Level", lvl_1: "Maybe", lvl_2: "High", lvl_3: "Extreme",
    se_maxstack: "Impossible stack above (0 = not checked)", se_white: "Never suspicious items (numbers)", se_credits: "Suspicious credit gain between two scans (0 = not checked)",
    se_autoban: "Automatically ban \"extreme\" cases", se_ban_dur: "Ban duration", se_exempt: "Do not scan admins, moderators and game masters",
    se_attacks: "Attack log", se_att_on: "Record who approaches other players' bases", se_radius: "Distance to the base core (metres)", se_warn_owner: "Warn the owners online",
    se_poles: "Pole control", se_poles_on: "Enable", se_poles_pf: "Playfields (* allowed)", se_pole_limit: "The pole starts at |z| above",
    se_pole_alert: "Alert after (seconds)", se_pole_move: "Move out of the pole after (seconds)", se_poles_hint: "Set it for the planet sizes of your scenario. Off by default.",
    se_emergency: "Emergency teleport", se_em_on: "Enable", se_em_fail: "Failed logins before the teleport", se_em_window: "Within (minutes)",
    se_em_pf: "Fallback playfield (empty = the player's start planet)", se_em_hint: "When a player cannot log in any more (crashed playfield), EAM sends them elsewhere at the next login.",
    se_flood: "\"Telnet flood\" protection", se_flood_on: "Restart the server when the game log loops on this text", se_flood_text: "Text", se_flood_n: "Times per minute",
    se_pos: "Record player positions every (seconds)",
    hi_title: "What happened here?", hi_pf: "Playfield", hi_radius: "Radius (m)", hi_from: "From", hi_to: "To", hi_search: "Search",
    hi_hint: "Players and structures seen in the area (positions kept 14 days), with what happened to the structures.",
    hi_who: "Seen in the area", hi_events: "Structure events", hi_journal: "Playfield journal", hi_closest: "closest", hi_none: "Nothing found.",
    hi_player: "Player", hi_structure: "Structure", hi_track: "Player track", hi_track_go: "Show the track",
    stt_title: "Server statistics (7 days)", stt_cpu: "CPU (%)", stt_mem: "Memory (MB)", stt_players: "Players", stt_pf: "Loaded playfields",
    stt_fps: "FPS", stt_pf_table: "Playfields", stt_avg_fps: "Average FPS", stt_min_fps: "Min FPS", stt_max_mem: "Max memory", stt_max_players: "Max players",
    stt_hint: "Recorded every 10 minutes while the server is online.", stt_tips: "Advice",
    tip_cpu: "The CPU is very busy on average: limit the number of playfields or structures.",
    tip_fps: "Some playfields run under 15 FPS on average: look at their structures (limits, cleanup).",
    tip_playfields: "More than 15 playfields loaded at once: unload them sooner in the server options.",
    err_invalid_scan: "Invalid interval.", err_invalid_cheat_rule: "Invalid anti-cheat rule.", err_invalid_radius: "Invalid distance.",
    err_invalid_poles: "Invalid pole settings.", err_invalid_emergency: "Invalid emergency teleport settings.", err_invalid_flood: "Invalid flood settings.",
  },
});

export const securityTabs = (role) => [...(atLeast(role, "moderator") ? ["security", "history"] : []), "stats"];

const sec = { listener: null };
document.addEventListener("eam:alert", (e) => sec.listener?.(e.detail));

export function renderSecurity(tab, el, s, me) {
  sec.listener = null;
  if (tab === "security") return renderJournal(el, s, me);
  if (tab === "history") return renderHistory(el, s);
  if (tab === "stats") return renderStats(el, s);
}

async function renderJournal(el, s, me) {
  const f = { category: "", severity: 0 };
  let rows = [];
  mount(el, html`<div class="card"><h3>${t("se_journal")}</h3>
      <div class="actions-row wrap"><select id="j-cat"><option value="">${t("se_all")}</option>${CATS.map((c) => html`<option>${c}</option>`)}</select>
        <select id="j-sev">${[0, 1, 2].map((v) => html`<option value="${v}">${t("sev_" + v)}</option>`)}</select>
        <button class="btn" id="j-scan">${t("se_scan")}</button></div>
      <div class="history" id="j-list"></div><button class="btn small" id="j-more">${t("se_more")}</button></div>
    <div id="se-form"></div>`);
  const line = (j) => html`<div class="sev-${j.severity}"><span class="chat-time">${when(j.utc)}</span> <span class="badge">${j.category}</span>
    ${j.name ? html`<b>${j.name}</b> ` : ""}${j.playfield ? html`<span class="hint">${j.playfield}</span> ` : ""}${j.text}</div>`;
  const draw = () => mount($("#j-list", el), rows.length ? html`${rows.map(line)}` : html`<div class="hint">—</div>`);
  const load = async (more) => {
    const before = more && rows.length ? `&before=${rows[rows.length - 1].id}` : "";
    try {
      const got = await api.get(`/api/servers/${s.id}/journal?category=${f.category}&severity=${f.severity}${before}`);
      rows = more ? [...rows, ...got] : got;
    } catch (ex) { fail(ex); }
    draw();
  };
  $("#j-cat", el).addEventListener("change", (e) => { f.category = e.target.value; load(false); });
  $("#j-sev", el).addEventListener("change", (e) => { f.severity = +e.target.value; load(false); });
  $("#j-more", el).addEventListener("click", () => load(true));
  $("#j-scan", el).addEventListener("click", async (e) => {
    e.target.disabled = true;
    try { const r = await api.post(`/api/servers/${s.id}/anticheat/scan`); toast(r.length ? t("se_scan_found", r.length) : t("se_scan_none")); load(false); } catch (ex) { fail(ex); }
    e.target.disabled = false;
  });
  sec.listener = (msg) => {
    if (msg.id !== s.id || !el.isConnected) return;
    const j = msg.entry;
    if ((f.category && j.category !== f.category) || j.severity < f.severity) return;
    rows.unshift(j);
    draw();
  };
  await load(false);
  if (!atLeast(me.role, "admin")) return;
  await settingsForm($("#se-form", el), `/api/servers/${s.id}/features/security`, [
    { title: t("se_anticheat"), hint: t("se_ac_hint"), fields: [
      { k: "antiCheatEnabled", type: "check", label: t("se_ac_on") },
      { k: "scanMinutes", type: "number", label: t("se_ac_every") },
      { k: "cheatRules", type: "rows", label: t("se_rules"), cols: [{ k: "itemId", label: t("se_item"), type: "number" }, { k: "max", label: t("se_max"), type: "number" },
        { k: "level", label: t("se_level"), type: "select", options: [[1, t("lvl_1")], [2, t("lvl_2")], [3, t("lvl_3")]], default: 2 }] },
      { k: "maxStack", type: "number", label: t("se_maxstack") },
      { k: "whitelist", type: "ints", label: t("se_white") },
      { k: "creditsJump", type: "number", label: t("se_credits") },
      { k: "autoBanExtreme", type: "check", label: t("se_autoban") },
      { k: "autoBanDuration", type: "text", label: t("se_ban_dur"), hint: t("ban_hint") },
      { k: "exemptAdmins", type: "check", label: t("se_exempt") }] },
    { title: t("ih_title"), hint: t("ih_settings_hint"), open: false, fields: [
      { k: "inventoryHistoryEnabled", type: "check", label: t("ih_on") },
      { k: "inventoryMinutes", type: "number", label: t("ih_minutes") },
      { k: "inventoryDays", type: "number", label: t("ih_days") }] },
    { title: t("se_attacks"), open: false, fields: [
      { k: "attackLogEnabled", type: "check", label: t("se_att_on") },
      { k: "attackRadius", type: "number", label: t("se_radius") },
      { k: "attackWarnOwners", type: "check", label: t("se_warn_owner") }] },
    { title: t("se_poles"), hint: t("se_poles_hint"), open: false, fields: [
      { k: "polesEnabled", type: "check", label: t("se_poles_on") },
      { k: "polePlayfields", type: "text", label: t("se_poles_pf") },
      { k: "poleLimit", type: "number", label: t("se_pole_limit") },
      { k: "poleAlertSeconds", type: "number", label: t("se_pole_alert") },
      { k: "poleMoveSeconds", type: "number", label: t("se_pole_move") }] },
    { title: t("se_emergency"), hint: t("se_em_hint"), open: false, fields: [
      { k: "emergencyEnabled", type: "check", label: t("se_em_on") },
      { k: "emergencyFailures", type: "number", label: t("se_em_fail") },
      { k: "emergencyWindowMinutes", type: "number", label: t("se_em_window") },
      { k: "emergencyPlayfield", type: "text", label: t("se_em_pf") },
      { k: "emergencyX", type: "number", label: "X" }, { k: "emergencyY", type: "number", label: "Y" }, { k: "emergencyZ", type: "number", label: "Z" }] },
    { title: t("se_flood"), open: false, fields: [
      { k: "floodEnabled", type: "check", label: t("se_flood_on") },
      { k: "floodText", type: "text", label: t("se_flood_text") },
      { k: "floodPerMinute", type: "number", label: t("se_flood_n") },
      { k: "positionSeconds", type: "number", label: t("se_pos") }] },
  ]);
}

const localInput = (d) => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);

async function renderHistory(el, s) {
  let pfs = [], players = [];
  try { [pfs, players] = await Promise.all([api.get(`/api/servers/${s.id}/playfields`), api.get(`/api/servers/${s.id}/players`)]); } catch (ex) { fail(ex); }
  const now = new Date();
  mount(el, html`<form class="card" id="hi-form"><h3>${t("hi_title")}</h3><div class="hint">${t("hi_hint")}</div>
      <label>${t("hi_pf")}</label><input name="pf" list="hi-pfs" required><datalist id="hi-pfs">${pfs.map((p) => html`<option value="${p.name}">`)}</datalist>
      <div class="form-row three"><div><label>X</label><input name="x" type="number" step="any" value="0"></div><div><label>Y</label><input name="y" type="number" step="any" value="0"></div>
        <div><label>Z</label><input name="z" type="number" step="any" value="0"></div></div>
      <div class="form-row three"><div><label>${t("hi_radius")}</label><input name="r" type="number" value="300"></div>
        <div><label>${t("hi_from")}</label><input name="from" type="datetime-local" value="${localInput(new Date(now - 86400000))}"></div>
        <div><label>${t("hi_to")}</label><input name="to" type="datetime-local" value="${localInput(now)}"></div></div>
      <button class="btn primary">${t("hi_search")}</button></form>
    <div id="hi-out"></div>
    <form class="card" id="tr-form"><h3>${t("hi_track")}</h3><select name="steam">${players.map((p) => html`<option value="${p.steamId}">${p.name}</option>`)}</select>
      <button class="btn">${t("hi_track_go")}</button><div id="tr-out"></div></form>`);
  $("#hi-form", el).addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = e.target;
    const q = new URLSearchParams({ pf: f.pf.value, x: f.x.value, y: f.y.value, z: f.z.value, radius: f.r.value,
      from: new Date(f.from.value).toISOString(), to: new Date(f.to.value).toISOString() });
    try {
      const r = await api.get(`/api/servers/${s.id}/history/near?${q}`);
      mount($("#hi-out", el), html`<div class="card"><h3>${t("hi_who")}</h3>${r.summary.length ? html`<div class="table-wrap"><table><tbody>${r.summary.map((x) => html`<tr>
          <td>${x.kind === "p" ? t("hi_player") : t("hi_structure")}</td><td><b>${x.name}</b> <span class="hint">${x.reference}</span></td>
          <td>${t("hi_closest")} ${x.closest} m</td><td class="hint">${when(x.first)} → ${when(x.last)}</td></tr>`)}</tbody></table></div>` : html`<div class="hint">${t("hi_none")}</div>`}
        ${r.events.length ? html`<h4>${t("hi_events")}</h4><div class="history">${r.events.map((h) => html`<div><span class="chat-time">${when(h.utc)}</span> ${h.structureId} <b>${h.kind}</b> ${h.old ?? ""} ${h.new ? "→ " + h.new : ""} ${h.by ? `(${h.by})` : ""}</div>`)}</div>` : ""}
        ${r.journal.length ? html`<h4>${t("hi_journal")}</h4><div class="history">${r.journal.map((j) => html`<div class="sev-${j.severity}"><span class="chat-time">${when(j.utc)}</span> <span class="badge">${j.category}</span> ${j.name ?? ""} ${j.text}</div>`)}</div>` : ""}</div>`);
    } catch (ex) { fail(ex); }
  });
  $("#tr-form", el).addEventListener("submit", async (e) => {
    e.preventDefault();
    const q = new URLSearchParams({ reference: e.target.steam.value, from: new Date(Date.now() - 2 * 86400000).toISOString(), to: new Date().toISOString() });
    try {
      const rows = await api.get(`/api/servers/${s.id}/history/track?${q}`);
      mount($("#tr-out", el), rows.length ? html`<div class="history">${rows.map((p) => html`<div><span class="chat-time">${when(p.utc)}</span> ${p.playfield} (${Math.round(p.x)}, ${Math.round(p.y)}, ${Math.round(p.z)})</div>`)}</div>` : html`<div class="hint">${t("hi_none")}</div>`);
    } catch (ex) { fail(ex); }
  });
}

async function renderStats(el, s) {
  let st;
  try { st = await api.get(`/api/servers/${s.id}/stats?days=7`); } catch (ex) { fail(ex); return; }
  const pts = (k) => st.server.map((r) => ({ x: new Date(r.utc), y: r[k] }));
  mount(el, html`<div class="card"><h3>${t("stt_title")}</h3><div class="hint">${t("stt_hint")}</div>
      ${st.tips.length ? html`<h4>${t("stt_tips")}</h4>${st.tips.map((k) => html`<div class="sev-1">${t(k)}</div>`)}` : ""}
      <h4>${t("stt_players")} · ${t("stt_pf")}</h4>${lineChart([{ name: t("stt_players"), points: pts("players") }, { name: t("stt_pf"), points: pts("playfields") }])}
      <h4>${t("stt_cpu")}</h4>${lineChart([{ name: t("stt_cpu"), points: pts("cpu") }], { unit: "%" })}
      <h4>${t("stt_mem")}</h4>${lineChart([{ name: t("stt_mem"), points: pts("mem") }])}
      <h4>${t("stt_fps")}</h4>${lineChart([{ name: "FPS", points: pts("fps") }])}</div>
    <div class="card"><h3>${t("stt_pf_table")}</h3><div class="table-wrap"><table><thead><tr><th>${t("col_playfield")}</th><th>${t("stt_avg_fps")}</th><th>${t("stt_min_fps")}</th>
      <th>${t("stt_max_mem")}</th><th>${t("stt_max_players")}</th><th>${t("tab_structures")}</th></tr></thead>
      <tbody>${st.playfields.map((p) => html`<tr><td><b>${p.name}</b></td><td>${p.avgFps}</td><td>${p.minFps}</td><td>${Math.round(p.maxMem)} Mo</td><td>${p.maxPlayers}</td><td>${p.structures}</td></tr>`)}</tbody></table></div></div>`);
  void raw; void $$;
}
