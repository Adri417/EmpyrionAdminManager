// Vita Somnium Life: every key of server_config.json, the database connection, and the game's MySQL data.
import { api } from "./api.js";
import { t, extend, errorText } from "./i18n.js";
import { html, raw, mount, $, $$, toast } from "./dom.js";
import { dialog, fail, atLeast } from "./ui.js";

extend({
  fr: {
    vo_title: "En ligne : {0} / {1}", vo_none: "Personne en ligne.", vo_no_answer: "Le serveur ne répond pas encore sur son port de requête Steam.",
    tab_vsl_players: "Joueurs", tab_vsl_data: "Base de données", tab_vsl_config: "Configuration",
    vc_title: "server_config.json", vc_hint: "Toutes les clés du fichier de configuration du serveur. Le nom et les ports se changent avec « Renommer » et « Réglages ». S'applique au prochain démarrage.",
    vc_managed: "géré par EAM", vc_lines: "une valeur par ligne", vc_json: "JSON", vc_saved: "Configuration enregistrée. Redémarrez le serveur pour l'appliquer.",
    vc_bad_json: "JSON invalide pour la clé", vc_add: "Ajouter une clé", vc_key: "Nom de la clé", vc_value: "Valeur",
    vd_title: "Connexion à la base (databaseinfo.json)", vd_pw_keep: "Laisser vide pour garder le mot de passe actuel.", vd_saved: "Connexion enregistrée. Redémarrez le serveur pour l'appliquer.",
    vb_table: "Table", vb_search: "Rechercher…", vb_rows: "{0} lignes", vb_prev: "Précédent", vb_next: "Suivant", vb_edit: "Modifier la ligne",
    vb_hint: "Données du jeu, lues directement dans sa base MySQL. Cliquez une ligne pour la modifier (argent, rôle admin, accès police ou médecin…). Chaque modification est notée dans le journal EAM avec l'ancienne valeur.",
    vb_online_warn: "Un joueur connecté peut être réécrit par le jeu à sa déconnexion : modifiez de préférence un joueur hors ligne.",
    vb_no_key: "Cette table n'a pas de clé primaire : lecture seule.", vb_none: "Aucune table. Le serveur crée ses tables à son premier démarrage.",
    vb_no_change: "Aucun changement.", vb_changed: "Ligne modifiée.", vb_null: "peut être vide",
    err_vsl_no_database: "Aucune base configurée (databaseinfo.json).", err_vsl_db_connect: "Connexion à la base MySQL impossible.",
    err_vsl_bad_table: "Table inconnue.", err_vsl_no_primary_key: "Table sans clé primaire : lecture seule.", err_vsl_bad_key: "Ligne mal identifiée.",
    err_vsl_bad_column: "Colonne inconnue.", err_vsl_row_not_found: "Ligne introuvable (supprimée entre-temps ?).", err_vsl_db_refused: "La base a refusé la modification.", err_not_vsl: "Ce serveur n'est pas un serveur Vita Somnium Life.",
  },
  en: {
    vo_title: "Online: {0} / {1}", vo_none: "Nobody online.", vo_no_answer: "The server does not answer on its Steam query port yet.",
    tab_vsl_players: "Players", tab_vsl_data: "Database", tab_vsl_config: "Configuration",
    vc_title: "server_config.json", vc_hint: "Every key of the server configuration file. Name and ports are changed with \"Rename\" and \"Settings\". Applies at the next start.",
    vc_managed: "managed by EAM", vc_lines: "one value per line", vc_json: "JSON", vc_saved: "Configuration saved. Restart the server to apply it.",
    vc_bad_json: "Invalid JSON for key", vc_add: "Add a key", vc_key: "Key name", vc_value: "Value",
    vd_title: "Database connection (databaseinfo.json)", vd_pw_keep: "Leave empty to keep the current password.", vd_saved: "Connection saved. Restart the server to apply it.",
    vb_table: "Table", vb_search: "Search…", vb_rows: "{0} rows", vb_prev: "Previous", vb_next: "Next", vb_edit: "Edit row",
    vb_hint: "Game data, read directly from its MySQL database. Click a row to edit it (money, admin role, police or medic access…). Every change is written to the EAM log with the old value.",
    vb_online_warn: "An online player can be overwritten by the game when they disconnect: prefer editing offline players.",
    vb_no_key: "This table has no primary key: read only.", vb_none: "No table. The server creates its tables at its first start.",
    vb_no_change: "No change.", vb_changed: "Row changed.", vb_null: "can be empty",
    err_vsl_no_database: "No database configured (databaseinfo.json).", err_vsl_db_connect: "Cannot connect to the MySQL database.",
    err_vsl_bad_table: "Unknown table.", err_vsl_no_primary_key: "Table without primary key: read only.", err_vsl_bad_key: "Row not identified.",
    err_vsl_bad_column: "Unknown column.", err_vsl_row_not_found: "Row not found (deleted meanwhile?).", err_vsl_db_refused: "The database refused the change.", err_not_vsl: "This is not a Vita Somnium Life server.",
  },
});

export const vslTabs = (role) => (atLeast(role, "admin") ? ["vsl_players", "vsl_data", "vsl_config"] : []);
export const isVslTab = (tab) => tab.startsWith("vsl_");

export function renderVsl(tab, el, s, me) {
  if (tab === "vsl_config") return renderConfig(el, s, me);
  return renderData(el, s, tab === "vsl_players");
}

// ------------------------------------------------------------------ online players (Steam query)

export async function renderOnline(el, s) {
  if (!el) return;
  let o;
  try { o = await api.get(`/api/servers/${s.id}/vsl/online`); } catch { return; }
  if (!el.isConnected) return;
  if (!o.answered) { mount(el, html`<div class="card"><div class="hint">${t("vo_no_answer")}</div></div>`); return; }
  const dur = (sec) => { const m = Math.floor(sec / 60); return m >= 60 ? `${Math.floor(m / 60)} h ${m % 60} min` : `${m} min`; };
  mount(el, html`<div class="card"><h3>${t("vo_title", o.count ?? 0, o.max ?? "?")}</h3>
    ${o.players?.length ? html`<div class="table-wrap"><table><tbody>${o.players.map((p) => html`<tr><td>${p.name}</td><td>${dur(p.seconds)}</td></tr>`)}</tbody></table></div>`
      : html`<div class="hint">${t("vo_none")}</div>`}</div>`);
}

// ------------------------------------------------------------------ configuration

const kind = (v) => (typeof v === "boolean" ? "bool" : typeof v === "number" ? "number" : typeof v === "string" ? "text"
  : Array.isArray(v) && v.every((x) => typeof x === "string") ? "lines" : "json");

async function renderConfig(el, s, me) {
  let data;
  try { data = await api.get(`/api/servers/${s.id}/vsl/config`); } catch (ex) { fail(ex); return; }
  const cfg = data.config || {};
  const managed = new Set(data.managed || []);
  const field = (k, v) => {
    const kd = kind(v);
    const ro = managed.has(k);
    const input = kd === "bool" ? html`<label class="check"><input type="checkbox" data-k="${k}" data-kind="bool" ${raw(v ? "checked" : "")} ${raw(ro ? "disabled" : "")}> ${k}</label>`
      : kd === "number" ? html`<input type="number" step="any" data-k="${k}" data-kind="number" value="${v}" ${raw(ro ? "disabled" : "")}>`
      : kd === "text" ? html`<input type="text" data-k="${k}" data-kind="text" value="${v}" ${raw(ro ? "disabled" : "")}>`
      : kd === "lines" ? html`<textarea rows="3" data-k="${k}" data-kind="lines" ${raw(ro ? "disabled" : "")}>${v.join("\n")}</textarea>`
      : html`<textarea rows="4" class="mono" data-k="${k}" data-kind="json" ${raw(ro ? "disabled" : "")}>${JSON.stringify(v, null, 2)}</textarea>`;
    return html`<div class="vc-field">${kd === "bool" ? "" : html`<label>${k}${ro ? html` <span class="hint">(${t("vc_managed")})</span>` : ""}${kd === "lines" ? html` <span class="hint">(${t("vc_lines")})</span>` : kd === "json" ? html` <span class="hint">(${t("vc_json")})</span>` : ""}</label>`}${input}</div>`;
  };
  const db = data.database;
  const owner = atLeast(me.role, "owner");
  mount(el, html`<form class="card" id="vc"><h3>${t("vc_title")}</h3><div class="hint">${t("vc_hint")}</div>
      ${Object.entries(cfg).map(([k, v]) => field(k, v))}
      <details><summary>${t("vc_add")}</summary><div class="form-row"><div><label>${t("vc_key")}</label><input id="vc-nk" maxlength="100"></div>
        <div><label>${t("vc_value")}</label><input id="vc-nv" placeholder='"texte", 12, true, ["a","b"]'></div></div></details>
      <div class="actions-row"><button class="btn primary">${t("save")}</button></div></form>
    ${owner ? html`<form class="card" id="vd"><h3>${t("vd_title")}</h3>
      <div class="form-row"><div><label>${t("vsl_db_host")}</label><input name="host" value="${db?.host ?? "127.0.0.1"}"></div>
        <div><label>${t("vsl_db_port")}</label><input name="port" type="number" value="${db?.port ?? 3306}"></div></div>
      <div class="form-row"><div><label>${t("vsl_db_name")}</label><input name="database" maxlength="64" value="${db?.database ?? ""}" required></div>
        <div><label>${t("vsl_db_user")}</label><input name="username" maxlength="64" value="${db?.username ?? ""}"></div></div>
      <label>${t("vsl_db_password")}</label><input name="password" type="password" maxlength="128" autocomplete="new-password">
      <div class="hint">${t("vd_pw_keep")}</div>
      <div class="actions-row"><button class="btn primary">${t("save")}</button></div></form>` : ""}`);

  $("#vc", el).addEventListener("submit", async (e) => {
    e.preventDefault();
    const out = {};
    for (const i of $$("[data-k]", el)) {
      if (i.disabled) continue;
      const k = i.dataset.k, kd = i.dataset.kind;
      let v;
      if (kd === "bool") v = i.checked;
      else if (kd === "number") v = i.value === "" ? 0 : Number(i.value);
      else if (kd === "text") v = i.value;
      else if (kd === "lines") v = i.value.split("\n").map((x) => x.trim()).filter(Boolean);
      else { try { v = JSON.parse(i.value || "null"); } catch { toast(`${t("vc_bad_json")} ${k}`, true); return; } }
      if (JSON.stringify(v) !== JSON.stringify(cfg[k])) out[k] = v;
    }
    const nk = $("#vc-nk", el).value.trim();
    if (nk) {
      const raw0 = $("#vc-nv", el).value.trim();
      try { out[nk] = raw0 === "" ? "" : JSON.parse(raw0); } catch { out[nk] = raw0; }
    }
    if (!Object.keys(out).length) { toast(t("vb_no_change")); return; }
    try { await api.put(`/api/servers/${s.id}/vsl/config`, out); toast(t("vc_saved")); renderConfig(el, s, me); } catch (ex) { fail(ex); }
  });
  $("#vd", el)?.addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = e.target;
    try {
      await api.put(`/api/servers/${s.id}/vsl/database`, { host: f.host.value, port: +f.port.value, database: f.database.value, username: f.username.value, password: f.password.value || null });
      toast(t("vd_saved")); f.password.value = "";
    } catch (ex) { fail(ex); }
  });
}

// ------------------------------------------------------------------ game data (MySQL)

const PAGE = 50;
const guessPlayers = (tables) => {
  const score = (n) => { n = n.toLowerCase(); return /^(players?|characters?|accounts?|users?)$/.test(n) ? 3 : /player|character/.test(n) ? 2 : /account|user/.test(n) ? 1 : 0; };
  return [...tables].sort((a, b) => score(b.name) - score(a.name))[0]?.name;
};

async function renderData(el, s, players) {
  let info;
  mount(el, html`<div class="card"><div class="hint">…</div></div>`);
  try { info = await api.get(`/api/servers/${s.id}/vsl/db/tables`); } catch (ex) { mount(el, html`<div class="card"><div class="error">${errorOf(ex)}</div></div>`); return; }
  const tables = info.tables || [];
  if (!tables.length) { mount(el, html`<div class="card"><div class="hint">${t("vb_none")}</div></div>`); return; }
  const key = `eam.vsl.${s.id}.${players ? "p" : "d"}`;
  let table = (() => { try { return localStorage.getItem(key); } catch { return null; } })();
  if (!tables.some((x) => x.name === table)) table = players ? guessPlayers(tables) : tables[0].name;
  let q = "", offset = 0, page = null;

  mount(el, html`${players ? html`<div id="vb-online"></div>` : ""}<div class="card">
    <div class="hint">${t("vb_hint")}</div><div class="hint">${t("vb_online_warn")}</div>
    <div class="actions-row"><label>${t("vb_table")}</label><select id="vb-t">${tables.map((x) => html`<option value="${x.name}" ${raw(x.name === table ? "selected" : "")}>${x.name} (${x.rows})</option>`)}</select>
      <input id="vb-q" type="search" placeholder="${t("vb_search")}" autocomplete="off"></div>
    <div class="hint" id="vb-info"></div>
    <div class="table-wrap"><table><thead id="vb-h"></thead><tbody id="vb-b"></tbody></table></div>
    <div class="actions-row"><button class="btn small" id="vb-prev">${t("vb_prev")}</button><button class="btn small" id="vb-next">${t("vb_next")}</button></div></div>`);

  const load = async () => {
    try { page = await api.get(`/api/servers/${s.id}/vsl/db/rows?table=${encodeURIComponent(table)}&q=${encodeURIComponent(q)}&offset=${offset}&limit=${PAGE}`); }
    catch (ex) { fail(ex); return; }
    const cols = page.columns;
    const editable = cols.some((c) => c.key);
    $("#vb-info", el).textContent = t("vb_rows", page.total) + (editable ? "" : " · " + t("vb_no_key"));
    mount($("#vb-h", el), html`<tr>${cols.map((c) => html`<th title="${c.type}">${c.name}${c.key ? " 🔑" : ""}</th>`)}</tr>`);
    mount($("#vb-b", el), html`${page.rows.map((r, i) => html`<tr data-i="${i}" class="${editable ? "clickable" : ""}">${r.map((v) => html`<td>${v == null ? "—" : String(v).length > 60 ? String(v).slice(0, 60) + "…" : v}</td>`)}</tr>`)}`);
    $("#vb-prev", el).disabled = offset === 0;
    $("#vb-next", el).disabled = offset + PAGE >= page.total;
    if (editable) $$("#vb-b tr", el).forEach((tr) => tr.addEventListener("click", () => editRow(s, table, cols, page.rows[+tr.dataset.i], load)));
  };
  $("#vb-t", el).addEventListener("change", (e) => { table = e.target.value; offset = 0; try { localStorage.setItem(key, table); } catch { /* private mode */ } load(); });
  let timer;
  $("#vb-q", el).addEventListener("input", (e) => { clearTimeout(timer); timer = setTimeout(() => { q = e.target.value; offset = 0; load(); }, 300); });
  $("#vb-prev", el).addEventListener("click", () => { offset = Math.max(0, offset - PAGE); load(); });
  $("#vb-next", el).addEventListener("click", () => { offset += PAGE; load(); });
  if (players && s.state === "Running") renderOnline($("#vb-online", el), s);
  load();
}

const errorOf = (ex) => `${errorText(ex.code)}${ex.body?.detail ? ` (${ex.body.detail})` : ""}`;
const isBoolCol = (c) => /^tinyint\(1\)/i.test(c.type) || /^bit\(1\)/i.test(c.type);
const isNumCol = (c) => /^(tinyint|smallint|mediumint|int|bigint|decimal|float|double)/i.test(c.type);

function editRow(s, table, cols, row, reload) {
  const fields = cols.map((c, i) => {
    const v = row[i];
    const name = `c${i}`;
    if (c.key) return html`<label>${c.name} 🔑</label><input value="${v ?? ""}" disabled>`;
    if (isBoolCol(c)) return html`<label class="check"><input type="checkbox" name="${name}" ${raw(+v ? "checked" : "")}> ${c.name}</label>`;
    const long = v != null && String(v).length > 80;
    return html`<label>${c.name} <span class="hint">${c.type}${c.nullable ? " · " + t("vb_null") : ""}</span></label>
      ${long ? html`<textarea name="${name}" rows="4">${v}</textarea>` : html`<input name="${name}" ${raw(isNumCol(c) ? 'type="number" step="any"' : "")} value="${v ?? ""}">`}`;
  });
  dialog(`${t("vb_edit")} · ${table}`, html`${fields}`, async (f) => {
    const set = {};
    cols.forEach((c, i) => {
      if (c.key) return;
      const inp = f[`c${i}`];
      if (!inp || inp.disabled) return;
      const old = row[i];
      let v;
      if (isBoolCol(c)) v = inp.checked ? 1 : 0;
      else if (inp.value === "" && c.nullable) v = null;
      else v = isNumCol(c) && inp.value !== "" ? Number(inp.value) : inp.value;
      const same = (old == null && v == null) || (old != null && v != null && String(old) === String(v));
      if (!same) set[c.name] = v;
    });
    if (!Object.keys(set).length) { toast(t("vb_no_change")); return true; }
    const keyObj = {};
    cols.forEach((c, i) => { if (c.key) keyObj[c.name] = row[i]; });
    await api.put(`/api/servers/${s.id}/vsl/db/rows?table=${encodeURIComponent(table)}`, { key: keyObj, set });
    toast(t("vb_changed"));
    reload();
  }, t("save"), false, true);
}
