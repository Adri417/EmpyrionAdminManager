// 0.9.5: import a server that used EAH (or any Empyrion server already on this machine).
import { api } from "./api.js";
import { t, extend, errorText } from "./i18n.js";
import { html, raw, mount, $, $$, toast, formatBytes } from "./dom.js";

extend({
  fr: {
    imp_button: "Importer un serveur (EAH)", imp_title: "Importer un serveur existant",
    imp_intro: "Indiquez le dossier du serveur Empyrion (celui qui contient DedicatedServer) ou le dossier d'EAH. EAM retrouve tout seul les bases et réglages d'EAH.",
    imp_path: "Dossier du serveur ou d'EAH", imp_analyse: "Analyser", imp_found_game: "Serveur Empyrion trouvé", imp_found_eah: "Données EAH trouvées",
    imp_no_eah: "Pas de données EAH : seul le serveur sera ajouté.", imp_mode: "Comment ajouter le serveur ?",
    imp_copy: "Copier le serveur dans EAM (conseillé : l'original n'est pas touché)", imp_inplace: "Utiliser le dossier tel quel (pas de copie)",
    imp_existing: "Importer seulement les données EAH dans un serveur EAM existant", imp_name: "Nom du serveur dans EAM",
    imp_what: "Données à importer", imp_o_players: "Joueurs, notes, marqués", imp_o_chat: "Chat (90 derniers jours)",
    imp_o_history: "Historiques (actions, kicks, bans, positions, intrusions)", imp_o_inv: "Copies des sacs à dos", imp_o_structures: "Structures et leur historique",
    imp_o_stats: "Statistiques", imp_o_settings: "Réglages (MOTD, filtre, chatbot, Discord, limites, wipes)", imp_o_tasks: "Tâches planifiées (importées désactivées)",
    imp_o_templates: "Modèles d'inventaire", imp_inv_days: "Copies des sacs à dos : garder les derniers (jours)",
    imp_go: "Importer", imp_done: "Import terminé", imp_open: "Ouvrir le serveur", imp_running: "Import en cours…",
    imp_stop_eah: "Arrêtez EAH et l'ancien serveur avant de démarrer le serveur dans EAM : deux outils ne doivent jamais gérer le même serveur.",
    imp_c_players: "joueurs", imp_c_chat: "messages", imp_c_backpacks: "copies de sacs à dos", imp_c_activities: "actions", imp_c_structures: "structures",
    imp_c_statistics: "relevés", imp_c_autominers: "autominers", imp_c_playfields: "playfields", imp_c_tasks: "tâches", imp_c_chatbotCommands: "commandes du chatbot",
    imp_c_templates: "modèles", imp_c_notes: "notes", imp_c_history: "historique", imp_c_journal: "journal", imp_c_positions: "positions",
    imp_c_structureEvents: "historique des structures", imp_c_limits: "limites", imp_c_autoWipes: "wipes auto", imp_c_settings: "réglages",
  },
  en: {
    imp_button: "Import a server (EAH)", imp_title: "Import an existing server",
    imp_intro: "Give the folder of the Empyrion server (the one with DedicatedServer) or EAH's folder. EAM finds EAH's databases and settings by itself.",
    imp_path: "Server or EAH folder", imp_analyse: "Analyse", imp_found_game: "Empyrion server found", imp_found_eah: "EAH data found",
    imp_no_eah: "No EAH data: only the server will be added.", imp_mode: "How should the server be added?",
    imp_copy: "Copy the server into EAM (recommended: the original is not touched)", imp_inplace: "Use the folder as it is (no copy)",
    imp_existing: "Only import EAH's data into an existing EAM server", imp_name: "Server name in EAM",
    imp_what: "Data to import", imp_o_players: "Players, notes, marks", imp_o_chat: "Chat (last 90 days)",
    imp_o_history: "Histories (actions, kicks, bans, positions, intrusions)", imp_o_inv: "Backpack copies", imp_o_structures: "Structures and their history",
    imp_o_stats: "Statistics", imp_o_settings: "Settings (MOTD, filter, chatbot, Discord, limits, wipes)", imp_o_tasks: "Scheduled tasks (imported switched off)",
    imp_o_templates: "Inventory templates", imp_inv_days: "Backpack copies: keep the last (days)",
    imp_go: "Import", imp_done: "Import finished", imp_open: "Open the server", imp_running: "Importing…",
    imp_stop_eah: "Stop EAH and the old server before starting the server in EAM: two tools must never manage the same server.",
    imp_c_players: "players", imp_c_chat: "messages", imp_c_backpacks: "backpack copies", imp_c_activities: "actions", imp_c_structures: "structures",
    imp_c_statistics: "records", imp_c_autominers: "autominers", imp_c_playfields: "playfields", imp_c_tasks: "tasks", imp_c_chatbotCommands: "chatbot commands",
    imp_c_templates: "templates", imp_c_notes: "notes", imp_c_history: "history", imp_c_journal: "journal", imp_c_positions: "positions",
    imp_c_structureEvents: "structure history", imp_c_limits: "limits", imp_c_autoWipes: "auto wipes", imp_c_settings: "settings",
  },
});

const counts = (c) => Object.entries(c || {}).filter(([, v]) => v).map(([k, v]) => `${Number(v).toLocaleString()} ${t("imp_c_" + k) === "imp_c_" + k ? k : t("imp_c_" + k)}`).join(" · ");

export function importDialog(servers) {
  const dlg = document.createElement("dialog");
  dlg.className = "wide";
  mount(dlg, html`<form method="dialog" class="dlg" id="imf"><h3>${t("imp_title")}</h3>
    <div class="hint">${t("imp_intro")}</div>
    <label>${t("imp_path")}</label>
    <div class="actions-row"><input name="path" required placeholder="F:\\steamcmd\\steamapps\\common\\Empyrion - Dedicated Server" autofocus>
      <button type="button" class="btn" id="im-an">${t("imp_analyse")}</button></div>
    <div id="im-res"></div><div class="error" id="derr"></div>
    <div class="dialog-actions"><button class="btn" value="cancel" formnovalidate>${t("cancel")}</button>
      <button class="btn primary" value="ok" id="im-go" disabled>${t("imp_go")}</button></div></form>`);
  document.body.appendChild(dlg);
  const f = $("form", dlg);
  let preview = null;
  const err = (ex) => { $("#derr", dlg).textContent = errorText(ex.code) + (ex.body?.detail ? ` (${ex.body.detail})` : ""); };
  $("#im-an", dlg).addEventListener("click", async () => {
    $("#derr", dlg).textContent = "";
    try { preview = await api.post("/api/import/preview", { path: f.path.value }); } catch (ex) { err(ex); return; }
    const p = preview;
    mount($("#im-res", dlg), html`
      ${p.gameDir ? html`<div><span class="state ok">${t("imp_found_game")}</span> <span class="hint">${p.gameDir} · ${formatBytes(p.size)}${p.port ? ` · port ${p.port}` : ""}</span></div>` : ""}
      ${p.configDir ? html`<div><span class="state ok">${t("imp_found_eah")}</span> <span class="hint">${p.configDir}</span></div><div class="hint">${counts(p.counts)}</div>`
        : html`<div class="hint">${t("imp_no_eah")}</div>`}
      <label>${t("imp_mode")}</label>
      ${p.gameDir && !p.existingServerId ? html`<label class="check"><input type="radio" name="mode" value="copy" checked> ${t("imp_copy")}</label>
        <label class="check"><input type="radio" name="mode" value="inplace"> ${t("imp_inplace")}</label>` : ""}
      ${p.configDir && servers.length ? html`<label class="check"><input type="radio" name="mode" value="existing" ${raw(!p.gameDir || p.existingServerId ? "checked" : "")}> ${t("imp_existing")}</label>
        <select name="server">${servers.map((s) => html`<option value="${s.id}" ${raw(s.id === p.existingServerId ? "selected" : "")}>${s.name}</option>`)}</select>` : ""}
      <div data-new><label>${t("imp_name")}</label><input name="name" maxlength="60" value="${p.name || p.eah?.serverName || ""}">
        <label>${t("rn_display")}</label><input name="displayName" maxlength="100" value="${p.name || p.eah?.serverName || ""}">
        <div class="hint">${t("rn_display_hint")}</div>
        <div data-copy><label>${t("imp_folder")}</label><input name="folder" value="${p.serversFolder || ""}">
          <div class="hint">${t("imp_folder_hint", formatBytes(p.size || 0), p.freeBytes != null ? formatBytes(p.freeBytes) : "?")}</div></div></div>
      ${p.configDir ? html`<details><summary>${t("imp_what")}</summary>
        ${[["players", "imp_o_players"], ["chat", "imp_o_chat"], ["history", "imp_o_history"], ["inventories", "imp_o_inv"], ["structures", "imp_o_structures"],
          ["statistics", "imp_o_stats"], ["settings", "imp_o_settings"], ["tasks", "imp_o_tasks"], ["templates", "imp_o_templates"]]
          .map(([k, l]) => html`<label class="check"><input type="checkbox" name="o_${k}" checked> ${t(l)}</label>`)}
        <label>${t("imp_inv_days")}</label><input name="invDays" type="number" min="1" max="365" value="30"></details>` : ""}
      <div class="hint">${t("imp_stop_eah")}</div>`);
    const sync = () => {
      const mode = $$("[name=mode]", f).find((r) => r.checked)?.value;
      $("[data-new]", f).hidden = mode === "existing";
      if ($("[data-copy]", f)) $("[data-copy]", f).hidden = mode !== "copy";
      if (f.server) f.server.hidden = mode !== "existing";
    };
    $$("[name=mode]", f).forEach((r) => r.addEventListener("change", sync));
    sync();
    $("#im-go", dlg).disabled = !$$("[name=mode]", f).length;
  });
  f.addEventListener("submit", async (e) => {
    if (e.submitter?.value === "cancel") { dlg.close(); return; }
    e.preventDefault();
    if (!preview) return;
    const mode = $$("[name=mode]", f).find((r) => r.checked)?.value;
    const opt = (k) => f["o_" + k]?.checked ?? true;
    const btn = $("#im-go", dlg);
    btn.disabled = true;
    btn.textContent = t("imp_running");
    try {
      const r = await api.post("/api/import", {
        path: f.path.value, mode, serverId: mode === "existing" ? +f.server.value : null, name: f.name?.value || null, displayName: f.displayName?.value || null, folder: mode === "copy" ? (f.folder?.value || null) : null,
        options: { players: opt("players"), chat: opt("chat"), history: opt("history"), inventories: opt("inventories"), structures: opt("structures"),
          statistics: opt("statistics"), settings: opt("settings"), tasks: opt("tasks"), templates: opt("templates"), inventoryDays: +(f.invDays?.value || 30) },
      });
      mount($("#im-res", dlg), html`<h4>${t("imp_done")} · ${r.name}</h4><div>${counts(r.counts)}</div>
        ${(r.notes || []).map((n) => html`<div class="hint">• ${n}</div>`)}<div class="hint">${t("imp_stop_eah")}</div>
        <a class="btn primary" href="#/server/${r.serverId}">${t("imp_open")}</a>`);
      btn.hidden = true;
      $("a.btn", dlg).addEventListener("click", () => dlg.close());
      toast(t("imp_done"));
    } catch (ex) { err(ex); btn.disabled = false; btn.textContent = t("imp_go"); }
  });
  dlg.addEventListener("close", () => dlg.remove());
  dlg.showModal();
}
