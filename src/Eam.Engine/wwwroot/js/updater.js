// 0.9.5: "update available" banner in the side menu, and the update settings.
import { api } from "./api.js";
import { t, extend, errorText } from "./i18n.js";
import { html, raw, mount, $, toast } from "./dom.js";

extend({
  fr: {
    up_available: "Mise à jour disponible", up_version: "Version {0}", up_apply: "Mettre à jour", up_notes: "Nouveautés",
    up_confirm: "Installer la version {0} maintenant ?\n\nVos serveurs, joueurs et réglages sont gardés. Les serveurs de jeu continuent de tourner. EAM redémarre tout seul (environ 1 minute). Si la fenêtre se ferme sans revenir, rouvrez-la.",
    up_running: "Mise à jour en cours… La page se recharge toute seule.", up_done: "EAM est à jour (version {0}).",
    up_title: "Mises à jour", up_check: "Chercher une nouvelle version toutes les heures (GitHub)", up_check_now: "Vérifier maintenant",
    up_current: "Version installée", up_latest: "Dernière version publiée", up_checked: "Dernière vérification", up_uptodate: "EAM est à jour.",
    up_hint: "La mise à jour remplace seulement les fichiers du programme. Le dossier de données (serveurs, bases, réglages) n'est jamais touché. Une copie de l'ancienne version est gardée dans data\\temp\\update\\previous.",
    up_windows: "La mise à jour automatique ne marche que sous Windows.",
    err_no_update: "Aucune nouvelle version.", err_update_running: "Une mise à jour est déjà en cours.", err_update_windows_only: "La mise à jour automatique ne marche que sous Windows.",
    err_update_servers_busy: "Un serveur est en cours d'installation, de mise à jour ou de sauvegarde. Réessayez quand c'est fini.",
    err_update_bad_package: "Le fichier téléchargé n'est pas une version d'EAM.", err_update_failed: "La mise à jour a échoué.", err_update_check_failed: "Impossible de joindre GitHub.",
    err_invalid_repository: "Dépôt GitHub invalide.",
  },
  en: {
    up_available: "Update available", up_version: "Version {0}", up_apply: "Update", up_notes: "What's new",
    up_confirm: "Install version {0} now?\n\nYour servers, players and settings are kept. Game servers keep running. EAM restarts by itself (about 1 minute). If the window closes and does not come back, open it again.",
    up_running: "Updating… The page reloads by itself.", up_done: "EAM is up to date (version {0}).",
    up_title: "Updates", up_check: "Look for a new version every hour (GitHub)", up_check_now: "Check now",
    up_current: "Installed version", up_latest: "Latest published version", up_checked: "Last check", up_uptodate: "EAM is up to date.",
    up_hint: "The update only replaces the program files. The data folder (servers, databases, settings) is never touched. A copy of the old version is kept in data\\temp\\update\\previous.",
    up_windows: "Automatic update only works on Windows.",
    err_no_update: "No new version.", err_update_running: "An update is already running.", err_update_windows_only: "Automatic update only works on Windows.",
    err_update_servers_busy: "A server is being installed, updated or backed up. Try again when it is done.",
    err_update_bad_package: "The downloaded file is not an EAM version.", err_update_failed: "The update failed.", err_update_check_failed: "Could not reach GitHub.",
    err_invalid_repository: "Invalid GitHub repository.",
  },
});

let timer = null;
let canApply = false;

const fail = (ex) => toast(errorText(ex.code) + (ex.body?.detail ? ` (${ex.body.detail})` : ""), true);

/** Shows the banner in the side menu when GitHub has a newer version. Checked again every 15 minutes. */
export function initUpdateBanner(me) {
  canApply = me.role === "owner";
  clearInterval(timer);
  const load = async () => {
    const box = $("#upd");
    if (!box) { clearInterval(timer); return; }
    let s;
    try { s = await api.get("/api/update"); } catch { return; }
    drawBanner(box, s);
  };
  load();
  timer = setInterval(load, 15 * 60 * 1000);
}

function drawBanner(box, s) {
  if (s.state === "installing") { mount(box, html`<div class="update-banner"><b>${t("up_running")}</b></div>`); return; }
  if (!s.available) { mount(box, html``); return; }
  mount(box, html`<div class="update-banner"><b>${t("up_available")}</b>${t("up_version", s.latest)}
    ${s.url ? html` · <a href="${s.url}" target="_blank" rel="noopener">${t("up_notes")}</a>` : ""}
    ${canApply && s.canApply ? html`<button class="btn primary small" id="upd-go">${t("up_apply")}</button>` : ""}</div>`);
  $("#upd-go", box)?.addEventListener("click", () => apply(s.latest));
}

async function apply(version) {
  if (!confirm(t("up_confirm", version))) return;
  const box = $("#upd");
  try { await api.post("/api/update/apply"); } catch (ex) { fail(ex); return; }
  if (box) mount(box, html`<div class="update-banner"><b>${t("up_running")}</b></div>`);
  toast(t("up_running"));
  // Wait for the engine to come back with the new version, then reload the page.
  const started = Date.now();
  const wait = setInterval(async () => {
    try {
      const st = await fetch("/api/status", { cache: "no-store" }).then((r) => r.json());
      if (st.version === version) { clearInterval(wait); toast(t("up_done", version)); setTimeout(() => location.reload(), 1500); }
    } catch {}
    if (Date.now() - started > 10 * 60 * 1000) clearInterval(wait);
  }, 3000);
}

/** "Updates" card on the settings page. */
export async function renderUpdateSettings(container) {
  let s;
  try { s = await api.get("/api/update"); } catch (ex) { fail(ex); return; }
  const draw = () => {
    mount(container, html`<div class="card"><h3>${t("up_title")}</h3>
      <dl class="kv"><dt>${t("up_current")}</dt><dd>${s.current}</dd>
        <dt>${t("up_latest")}</dt><dd>${s.latest ?? "—"}</dd>
        <dt>${t("up_checked")}</dt><dd>${s.checkedUtc ? new Date(s.checkedUtc).toLocaleString() : "—"}</dd></dl>
      ${s.error ? html`<div class="error">${errorText(s.error)}</div>` : ""}
      ${s.available ? html`<div class="update-banner"><b>${t("up_available")}</b>${t("up_version", s.latest)}
          ${s.canApply ? html`<button class="btn primary small" id="ups-go">${t("up_apply")}</button>` : html`<div class="hint">${t("up_windows")}</div>`}</div>`
        : s.latest ? html`<div class="hint">${t("up_uptodate")}</div>` : ""}
      <label class="check"><input type="checkbox" id="ups-check" ${raw(s.check ? "checked" : "")}> ${t("up_check")}</label>
      <div class="hint">${t("up_hint")}</div>
      <button class="btn" id="ups-now">${t("up_check_now")}</button></div>`);
    $("#ups-go", container)?.addEventListener("click", () => apply(s.latest));
    $("#ups-check", container).addEventListener("change", async (e) => {
      try { s = await api.put("/api/update/settings", { check: e.target.checked }); toast(t("saved")); } catch (ex) { fail(ex); }
    });
    $("#ups-now", container).addEventListener("click", async () => {
      try { s = await api.post("/api/update/check"); draw(); const b = $("#upd"); if (b) drawBanner(b, s); } catch (ex) { fail(ex); }
    });
  };
  draw();
}
