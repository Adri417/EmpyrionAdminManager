// Shared helpers for the screens of steps 6 to 9: dialogs, settings forms, small charts.
import { api } from "./api.js";
import { t, errorText } from "./i18n.js";
import { html, raw, mount, $, $$, toast } from "./dom.js";

export const ROLES = ["owner", "admin", "gamemaster", "moderator", "viewer"];
export const atLeast = (role, required) => ROLES.indexOf(role) <= ROLES.indexOf(required);
export const fail = (ex) => toast(errorText(ex.code) + (ex.body?.detail ? ` (${ex.body.detail})` : ""), true);
export const when = (utc) => (utc ? new Date(utc).toLocaleString([], { dateStyle: "short", timeStyle: "short" }) : "—");
export const num = (v) => (v == null ? "—" : Math.round(v).toLocaleString());

/** Modal form. `submit(form)` returns false to keep it open. */
export function dialog(title, fields, submit, okLabel = t("save"), danger = false, wide = false) {
  const dlg = document.createElement("dialog");
  if (wide) dlg.className = "wide";
  mount(dlg, html`<form method="dialog" class="dlg"><h3>${title}</h3>${fields}<div class="error" id="derr"></div>
    <div class="dialog-actions"><button class="btn" value="cancel" formnovalidate>${submit ? t("cancel") : "OK"}</button>
    ${submit ? html`<button class="btn ${danger ? "danger" : "primary"}" value="ok">${okLabel}</button>` : ""}</div></form>`);
  document.body.appendChild(dlg);
  const f = $("form", dlg);
  f.addEventListener("submit", async (e) => {
    if (e.submitter?.value === "cancel" || !submit) { dlg.close(); return; }
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

// ------------------------------------------------------------------ settings forms
// A field: { k, type, label, hint, options:[[value,label]], cols:[...] }.
// Types: check, number, text, password, textarea, select, lines (array of strings, one per line),
// ints (array of numbers, comma separated), rows (array of objects, one table row each), note.

function cell(c, v, name) {
  if (c.type === "check") return html`<input type="checkbox" name="${name}" ${raw(v ? "checked" : "")}>`;
  if (c.type === "select") return html`<select name="${name}">${c.options.map(([o, l]) => html`<option value="${o}" ${raw(String(v) === String(o) ? "selected" : "")}>${l}</option>`)}</select>`;
  if (c.type === "ints" || c.type === "csv") return html`<input name="${name}" value="${(v ?? []).join(", ")}" placeholder="${c.placeholder ?? ""}">`;
  if (c.type === "number") return html`<input type="number" step="any" name="${name}" value="${v ?? 0}" class="small">`;
  return html`<input name="${name}" value="${v ?? ""}" placeholder="${c.placeholder ?? ""}">`;
}

function read(c, el) {
  if (!el) return undefined;
  if (c.type === "check") return el.checked;
  if (c.type === "number") return el.value === "" ? 0 : +el.value;
  if (c.type === "ints") return el.value.split(/[,;\s]+/).filter(Boolean).map(Number).filter((n) => !Number.isNaN(n));
  if (c.type === "csv") return el.value.split(/[,;]+/).map((x) => x.trim()).filter(Boolean);
  if (c.type === "lines") return el.value.split("\n").map((x) => x.trim()).filter(Boolean);
  return el.value;
}

function rowsTable(f, list) {
  return html`<div class="table-wrap"><table class="rows" data-rows="${f.k}"><thead><tr>${f.cols.map((c) => html`<th>${c.label}</th>`)}<th></th></tr></thead>
    <tbody>${list.map((row, i) => html`<tr data-i="${i}">${f.cols.map((c) => html`<td>${cell(c, row[c.k], `${f.k}.${c.k}`)}</td>`)}
      <td><button type="button" class="btn small danger" data-del-row>✕</button></td></tr>`)}</tbody></table></div>
    <button type="button" class="btn small" data-add-row="${f.k}">+ ${t("add")}</button>`;
}

function field(f, data) {
  const v = data[f.k];
  const hint = f.hint ? html`<div class="hint">${f.hint}</div>` : "";
  switch (f.type) {
    case "note": return html`<div class="hint">${f.label}</div>`;
    case "check": return html`<label class="check"><input type="checkbox" name="${f.k}" ${raw(v ? "checked" : "")}> ${f.label}</label>${hint}`;
    case "textarea": return html`<label>${f.label}</label><textarea name="${f.k}" rows="${f.rows ?? 3}">${v ?? ""}</textarea>${hint}`;
    case "lines": return html`<label>${f.label}</label><textarea name="${f.k}" rows="${f.rows ?? 4}">${(v ?? []).join("\n")}</textarea>${hint}`;
    case "password": return html`<label>${f.label}</label><input type="password" name="${f.k}" autocomplete="off" placeholder="${data["has" + f.k[0].toUpperCase() + f.k.slice(1)] ? "••••••••" : ""}">${hint}`;
    case "rows": return html`<label>${f.label}</label><div data-rows-wrap="${f.k}">${rowsTable(f, v ?? [])}</div>${hint}`;
    case "select": return html`<label>${f.label}</label>${cell(f, v, f.k)}${hint}`;
    case "number": return html`<label>${f.label}</label><input type="number" step="any" name="${f.k}" value="${v ?? 0}">${hint}`;
    default: return html`<label>${f.label}</label>${cell(f, v, f.k)}${hint}`;
  }
}

/** Collects the values of a rows table. */
function readRows(f, form) {
  return $$(`table[data-rows="${f.k}"] tbody tr`, form).map((tr) => {
    const o = {};
    f.cols.forEach((c) => { o[c.k] = read(c, $(`[name="${f.k}.${c.k}"]`, tr)); });
    return o;
  });
}

/**
 * Settings page made of sections. Loads `url`, shows the fields, saves with PUT.
 * `sections`: [{ title, hint, fields }]
 */
export async function settingsForm(el, url, sections, after) {
  let data;
  try { data = await api.get(url); } catch (ex) { fail(ex); return; }
  const fields = sections.flatMap((s) => s.fields);
  mount(el, html`<form class="settings-form">${sections.map((s) => html`<details class="card" ${raw(s.open === false ? "" : "open")}><summary><b>${s.title}</b></summary>
      ${s.hint ? html`<div class="hint">${s.hint}</div>` : ""}${s.fields.map((f) => field(f, data))}</details>`)}
    <div class="sticky-save"><button class="btn primary">${t("save")}</button></div></form>`);
  const form = $("form", el);
  const wire = () => {
    $$("[data-del-row]", form).forEach((b) => { b.onclick = () => b.closest("tr").remove(); });
    $$("[data-add-row]", form).forEach((b) => {
      b.onclick = () => {
        const f = fields.find((x) => x.k === b.dataset.addRow);
        const current = readRows(f, form);
        current.push(Object.fromEntries(f.cols.map((c) => [c.k, c.default ?? (c.type === "check" ? false : c.type === "number" ? 0 : c.type === "ints" || c.type === "csv" ? [] : c.type === "select" ? c.options[0][0] : "")])));
        mount($(`[data-rows-wrap="${f.k}"]`, form), rowsTable(f, current));
        wire();
      };
    });
  };
  wire();
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const next = { ...data };
    for (const f of fields) {
      if (f.type === "note") continue;
      next[f.k] = f.type === "rows" ? readRows(f, form) : read(f, form.elements[f.k]);
    }
    Object.keys(next).filter((k) => k.startsWith("has")).forEach((k) => delete next[k]);
    try { await api.put(url, next); toast(t("saved")); data = { ...data, ...next }; after?.(next); } catch (ex) { fail(ex); }
  });
  return data;
}

// ------------------------------------------------------------------ small line chart (SVG)
/** points: [{ x: Date, y: number }], one series per entry of `series`. */
export function lineChart(series, { height = 160, unit = "" } = {}) {
  const w = 600;
  const all = series.flatMap((s) => s.points);
  if (!all.length) return html`<div class="hint">—</div>`;
  const x0 = Math.min(...all.map((p) => +p.x)), x1 = Math.max(...all.map((p) => +p.x)) || x0 + 1;
  const ymax = Math.max(1, ...all.map((p) => p.y)) * 1.1;
  const X = (x) => 40 + ((+x - x0) / Math.max(1, x1 - x0)) * (w - 50);
  const Y = (y) => height - 20 - (y / ymax) * (height - 30);
  const ticks = [0, 0.5, 1].map((f) => ymax * f);
  const days = [];
  for (let d = new Date(x0); +d <= x1; d = new Date(+d + 86400000)) days.push(d);
  return html`<svg class="chart" viewBox="0 0 ${w} ${height}" preserveAspectRatio="none" role="img">
    ${ticks.map((v) => html`<line x1="40" x2="${w - 10}" y1="${Y(v)}" y2="${Y(v)}" class="grid-line"/><text x="2" y="${Y(v) + 4}" class="axis">${Math.round(v)}${unit}</text>`)}
    ${days.length < 15 ? days.map((d) => html`<text x="${X(d)}" y="${height - 4}" class="axis">${d.toLocaleDateString([], { weekday: "short" })}</text>`) : ""}
    ${series.map((s, i) => html`<polyline class="serie s${i}" fill="none" points="${s.points.map((p) => `${X(p.x).toFixed(1)},${Y(p.y).toFixed(1)}`).join(" ")}"/>`)}
  </svg><div class="legend">${series.map((s, i) => html`<span class="s${i}">■ ${s.name}</span>`)}</div>`;
}
