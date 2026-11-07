import { currentLanguage } from "./i18n.js";

// Tiny safe templating: every ${value} is HTML-escaped unless wrapped in raw().
const RAW = Symbol("raw");
export const raw = (s) => ({ [RAW]: String(s) });

const esc = (v) => String(v ?? "").replace(/[&<>"']/g, (c) =>
  ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);

function part(v) {
  if (v && v[RAW] !== undefined) return v[RAW];
  if (Array.isArray(v)) return v.map(part).join("");
  return esc(v);
}

export function html(strings, ...values) {
  let out = strings[0];
  values.forEach((v, i) => { out += part(v) + strings[i + 1]; });
  return raw(out);
}

export function mount(el, view) { el.innerHTML = view[RAW]; return el; }
export const $ = (sel, root = document) => root.querySelector(sel);
export const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];

export function toast(message, isError = false) {
  const el = document.createElement("div");
  el.className = "toast" + (isError ? " err" : "");
  el.textContent = message;
  document.body.appendChild(el);
  setTimeout(() => el.remove(), isError ? 6000 : 3000);
}

export function formatBytes(n) {
  if (!n && n !== 0) return "—";
  const u = currentLanguage() === "fr" ? ["o", "Ko", "Mo", "Go", "To"] : ["B", "KB", "MB", "GB", "TB"];
  let i = 0;
  while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
  return `${n.toFixed(i >= 3 ? 1 : 0)} ${u[i]}`;
}

// .NET TimeSpan JSON ("d.hh:mm:ss.fffffff" or "hh:mm:ss") -> seconds
export function timeSpanSeconds(ts) {
  if (typeof ts !== "string") return 0;
  const m = ts.match(/^(?:(\d+)\.)?(\d+):(\d+):(\d+)/);
  if (!m) return 0;
  return (+(m[1] || 0)) * 86400 + (+m[2]) * 3600 + (+m[3]) * 60 + (+m[4]);
}
