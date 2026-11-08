import { api } from "./api.js";
import { t, setLanguage, errorText } from "./i18n.js";
import { html, raw, mount, $, $$, toast, formatBytes, timeSpanSeconds } from "./dom.js";
import { initServers, serverEvent, loadServers, renderServerList, renderServerDetail, dashboardServers, setServersListener } from "./servers.js";
import { playerCount } from "./live.js";
import "./more.js";
import { initUpdateBanner, renderUpdateSettings } from "./updater.js";

const app = document.getElementById("app");
const state = { status: null, me: null, socket: null, system: null };

const ROLES = ["owner", "admin", "gamemaster", "moderator", "viewer"];
const atLeast = (role, required) => ROLES.indexOf(role) <= ROLES.indexOf(required);

const brand = () => html`
  <div class="brand">
    <img src="/favicon.svg" alt="">
    <div><div class="brand-name">Empyrion Admin Manager</div><div class="brand-sub">${t("tagline")}</div></div>
  </div>`;

// ---------------------------------------------------------------- start
async function start() {
  setLanguage("auto");
  try {
    state.status = await api.get("/api/status");
  } catch {
    mount(app, html`<div class="auth"><div class="auth-card">${brand()}<p class="error">${t("err_network")}</p></div></div>`);
    setTimeout(start, 3000);
    return;
  }
  app.classList.remove("boot");
  if (state.status.needsSetup) return showSetup();
  if (!state.status.authenticated) return showLogin();
  try {
    state.me = await api.get("/api/me");
  } catch {
    return showLogin();
  }
  showShell();
}

window.addEventListener("eam:unauthorized", () => {
  if (state.me) { state.me = null; closeSocket(); showLogin(); }
});

// ---------------------------------------------------------------- setup
function showSetup() {
  if (!state.status.setupAllowedHere) {
    mount(app, html`<div class="auth"><div class="auth-card">${brand()}
      <h1>${t("setup_title")}</h1><p class="lead">${t("setup_remote")}</p></div></div>`);
    return;
  }
  mount(app, html`
    <div class="auth"><form class="auth-card" id="f" autocomplete="off">
      ${brand()}
      <h1>${t("setup_title")}</h1>
      <p class="lead">${t("setup_lead")}</p>
      <label for="u">${t("username")}</label><input id="u" type="text" required autofocus maxlength="32">
      <label for="p">${t("password")}</label><input id="p" type="password" required autocomplete="new-password">
      <div class="hint">${t("password_hint")}</div>
      <label for="p2">${t("password_confirm")}</label><input id="p2" type="password" required autocomplete="new-password">
      <div class="error" id="err"></div>
      <button class="btn primary block" type="submit">${t("create_account")}</button>
    </form></div>`);
  $("#f").addEventListener("submit", async (e) => {
    e.preventDefault();
    const err = $("#err");
    if ($("#p").value !== $("#p2").value) { err.textContent = t("passwords_differ"); return; }
    try {
      state.me = await api.post("/api/setup", { username: $("#u").value, password: $("#p").value });
      showShell();
    } catch (ex) { err.textContent = errorText(ex.code, ex.body); }
  });
}

// ---------------------------------------------------------------- login
function showLogin() {
  mount(app, html`
    <div class="auth"><form class="auth-card" id="f">
      ${brand()}
      <h1>${t("login_title")}</h1>
      <p class="lead">${t("login_lead")}</p>
      <label for="u">${t("username")}</label><input id="u" type="text" required autofocus autocomplete="username">
      <label for="p">${t("password")}</label><input id="p" type="password" required autocomplete="current-password">
      <label class="check"><input id="r" type="checkbox" checked> ${t("remember")}</label>
      <div class="error" id="err"></div>
      <button class="btn primary block" type="submit">${t("sign_in")}</button>
    </form></div>`);
  $("#f").addEventListener("submit", async (e) => {
    e.preventDefault();
    const btn = $("button[type=submit]");
    btn.disabled = true;
    try {
      state.me = await api.post("/api/login", { username: $("#u").value, password: $("#p").value, remember: $("#r").checked });
      showShell();
    } catch (ex) {
      $("#err").textContent = errorText(ex.code, ex.body);
      btn.disabled = false;
    }
  });
}

// ---------------------------------------------------------------- shell
const pages = {
  dashboard: { icon: "◎", label: "nav_dashboard", role: "viewer", render: renderDashboard },
  servers: { icon: "▣", label: "nav_servers", role: "viewer", render: renderServerList },
  users: { icon: "☺", label: "nav_users", role: "owner", render: renderUsers },
  settings: { icon: "⚙", label: "nav_settings", role: "owner", render: renderSettings },
  account: { icon: "●", label: "nav_account", role: "viewer", render: renderAccount },
};

function showShell() {
  const nav = Object.entries(pages)
    .filter(([, p]) => atLeast(state.me.role, p.role))
    .map(([id, p]) => html`<a href="#/${id}" data-page="${id}"><span class="ico">${p.icon}</span>${t(p.label)}${p.soon ? html`<span class="soon">${t("soon")}</span>` : ""}</a>`);
  mount(app, html`
    <div class="shell">
      <aside class="side">${brand()}<div id="upd"></div><nav class="nav">${nav}</nav>
        <div class="side-foot">v${state.status.version}</div></aside>
      <div class="main">
        <header class="top">
          <h2 id="title"></h2><div class="top-extra" id="top-extra"></div><div class="spacer"></div>
          <span class="live"><span class="dot" id="live-dot"></span><span id="live-text">${t("offline")}</span></span>
          <span class="user-chip"><b>${state.me.username}</b> · ${t("role_" + state.me.role)}</span>
          <button class="btn" id="logout">${t("sign_out")}</button>
        </header>
        <main class="content" id="page"></main>
      </div>
    </div>`);
  $("#logout").addEventListener("click", async () => {
    try { await api.post("/api/logout"); } catch {}
    state.me = null; closeSocket(); showLogin();
  });
  initServers(state.me);
  initUpdateBanner(state.me);
  openSocket();
  route();
}

window.addEventListener("hashchange", () => { if (state.me) route(); });

function route() {
  setServersListener(null);
  $("#top-extra").replaceChildren();
  const page = $("#page");
  page.replaceChildren();
  const detail = location.hash.match(/^#\/server\/(\d+)/);
  if (detail) {
    $$(".nav a").forEach((a) => a.classList.toggle("active", a.dataset.page === "servers"));
    $("#title").textContent = t("nav_servers");
    renderServerDetail(page, +detail[1], (name) => { $("#title").textContent = name; });
    return;
  }
  let id = (location.hash.match(/^#\/(\w+)/) || [])[1] || "dashboard";
  if (!pages[id] || !atLeast(state.me.role, pages[id].role)) id = "dashboard";
  $$(".nav a").forEach((a) => a.classList.toggle("active", a.dataset.page === id));
  $("#title").textContent = t(pages[id].label);
  pages[id].render(page);
}

// ---------------------------------------------------------------- live socket
function openSocket() {
  closeSocket();
  const ws = new WebSocket(`${location.protocol === "https:" ? "wss" : "ws"}://${location.host}/ws`);
  state.socket = ws;
  ws.onopen = () => setLive(true);
  ws.onmessage = (e) => {
    const msg = JSON.parse(e.data);
    if (msg.type === "system") { state.system = msg.data; updateDashboard(); }
    else serverEvent(msg);
  };
  ws.onclose = () => {
    setLive(false);
    if (state.socket === ws && state.me) setTimeout(() => { if (state.socket === ws && state.me) openSocket(); }, 3000);
  };
}
function closeSocket() {
  const ws = state.socket;
  state.socket = null;
  if (ws) ws.close();
}
function setLive(on) {
  const d = $("#live-dot"), txt = $("#live-text");
  if (!d) return;
  d.classList.toggle("on", on);
  txt.textContent = on ? t("live") : t("offline");
}

// ---------------------------------------------------------------- dashboard
function renderDashboard(page) {
  mount(page, html`
    <div class="grid">
      <div class="card"><h3>${t("cpu")}</h3><div class="stat" id="cpu">—</div><div class="bar"><i id="cpu-bar"></i></div></div>
      <div class="card"><h3>${t("memory")}</h3><div class="stat" id="mem">—</div><div class="bar"><i id="mem-bar"></i></div></div>
      <div class="card"><h3>${t("eam_process")}</h3><div class="stat" id="eam">—</div><div class="hint" id="eam-up"></div></div>
    </div>
    <div class="grid">
      <div class="card"><h3>${t("disks")}</h3><div id="disks"></div></div>
      <div class="card"><h3>${t("machine")}</h3><dl class="kv" id="machine"></dl></div>
    </div>
    <div class="card"><h3>${t("nav_servers")}</h3><div id="dash-servers"></div></div>`);
  setServersListener(drawDashServers);
  loadServers();
  if (state.system) updateDashboard();
  else api.get("/api/system").then((s) => { state.system = s; updateDashboard(); }).catch(() => {});
}

function level(el, pct) {
  el.style.width = `${Math.min(100, Math.max(0, pct))}%`;
  el.classList.toggle("warn", pct >= 75 && pct < 90);
  el.classList.toggle("bad", pct >= 90);
}

function duration(sec) {
  const d = Math.floor(sec / 86400), h = Math.floor((sec % 86400) / 3600), m = Math.floor((sec % 3600) / 60);
  return d ? `${d} ${t("days")} ${h} ${t("hours")}` : h ? `${h} ${t("hours")} ${m} ${t("minutes")}` : `${m} ${t("minutes")}`;
}

function updateDashboard() {
  const s = state.system;
  if (!s || !$("#cpu")) return;
  mount($("#cpu"), html`${s.cpuPercent.toFixed(0)}<small>%</small>`);
  level($("#cpu-bar"), s.cpuPercent);
  const memPct = s.memoryTotalBytes ? (100 * s.memoryUsedBytes) / s.memoryTotalBytes : 0;
  mount($("#mem"), html`${formatBytes(s.memoryUsedBytes)}<small>/ ${formatBytes(s.memoryTotalBytes)}</small>`);
  level($("#mem-bar"), memPct);
  $("#eam").textContent = formatBytes(s.eamMemoryBytes);
  $("#eam-up").textContent = `${t("eam_uptime")} ${duration(timeSpanSeconds(s.eamUptime))}`;
  mount($("#disks"), html`${s.disks.map((d) => html`
    <div class="kv"><dt>${d.name}</dt><dd>${formatBytes(d.freeBytes)} ${t("free")} / ${formatBytes(d.totalBytes)}</dd></div>
    <div class="bar"><i data-pct="${(100 * (d.totalBytes - d.freeBytes)) / d.totalBytes}"></i></div>`)}`);
  $$("#disks .bar > i").forEach((i) => level(i, +i.dataset.pct));
  mount($("#machine"), html`
    <dt>${t("machine")}</dt><dd>${s.machineName}</dd>
    <dt>${t("system")}</dt><dd>${s.osDescription}</dd>
    <dt>${t("cpu")}</dt><dd>${s.processorCount} ${t("cores")}</dd>
    <dt>${t("uptime")}</dt><dd>${duration(timeSpanSeconds(s.machineUptime))}</dd>`);
}

// ---------------------------------------------------------------- servers summary
function drawDashServers() {
  const el = $("#dash-servers");
  if (!el) return;
  const list = dashboardServers();
  if (!list.length) {
    mount(el, html`<div class="empty"><div class="big">▣</div><b>${t("servers_empty_title")}</b><div>${t("no_server_yet")}</div></div>`);
    return;
  }
  const cls = (st) => st === "Running" ? "ok" : st === "Crashed" ? "bad" : ["Stopped", "NotInstalled"].includes(st) ? "" : "busy";
  mount(el, html`<table><tbody>${list.map((s) => html`<tr>
    <td><a href="#/server/${s.id}"><b>${s.name}</b></a></td>
    <td><span class="state ${cls(s.state)}">${t("state_" + s.state)}</span></td>
    <td>${s.state === "Running" && playerCount(s.id) != null ? `${playerCount(s.id)} ${t("col_players").toLowerCase()}` : ""}</td>
    <td class="hint">${s.gamePort}-${s.gamePort + 3}</td></tr>`)}</tbody></table>`);
}

// ---------------------------------------------------------------- users
async function renderUsers(page) {
  let users = [];
  try { users = await api.get("/api/users"); } catch (ex) { toast(errorText(ex.code), true); }
  mount(page, html`
    <div><button class="btn primary" id="add">+ ${t("add_user")}</button></div>
    <div class="card"><table>
      <thead><tr><th>${t("username")}</th><th>${t("role")}</th><th>Steam ID</th><th>${t("last_login")}</th><th></th></tr></thead>
      <tbody>${users.map((u) => html`<tr>
        <td><b>${u.username}</b></td>
        <td><span class="badge ${u.role}">${t("role_" + u.role)}</span></td>
        <td>${u.steamId || "—"}</td>
        <td>${u.lastLoginUtc ? new Date(u.lastLoginUtc).toLocaleString() : t("never")}</td>
        <td class="actions"><button class="btn" data-edit="${u.id}">${t("edit")}</button>
          ${u.id !== state.me.id ? html`<button class="btn danger" data-del="${u.id}">${t("delete")}</button>` : ""}</td></tr>`)}
      </tbody></table></div>`);
  $("#add").addEventListener("click", () => userDialog(null, () => renderUsers(page)));
  $$("[data-edit]", page).forEach((b) => b.addEventListener("click", () =>
    userDialog(users.find((u) => u.id === +b.dataset.edit), () => renderUsers(page))));
  $$("[data-del]", page).forEach((b) => b.addEventListener("click", async () => {
    const u = users.find((x) => x.id === +b.dataset.del);
    if (!confirm(t("confirm_delete", u.username))) return;
    try { await api.del(`/api/users/${u.id}`); renderUsers(page); } catch (ex) { toast(errorText(ex.code), true); }
  }));
}

function userDialog(user, done) {
  const dlg = document.createElement("dialog");
  const roleOptions = ROLES.map((r) => html`<option value="${r}" ${raw(user?.role === r || (!user && r === "moderator") ? "selected" : "")}>${t("role_" + r)}</option>`);
  mount(dlg, html`<form method="dialog" id="uf">
    <h3>${user ? user.username : t("add_user")}</h3>
    ${user ? "" : html`<label>${t("username")}</label><input name="username" type="text" required maxlength="32">`}
    <label>${t("role")}</label><select name="role">${roleOptions}</select>
    <div class="hint" id="role-d"></div>
    <label>${t("steam_id")}</label><input name="steamId" type="text" maxlength="17" value="${user?.steamId || ""}">
    <label>${user ? t("new_password_opt") : t("password")}</label>
    <input name="password" type="password" autocomplete="new-password" ${raw(user ? "" : "required")}>
    <div class="error" id="uerr"></div>
    <div class="dialog-actions"><button class="btn" value="cancel" formnovalidate>${t("cancel")}</button>
      <button class="btn primary" value="ok">${t("save")}</button></div></form>`);
  document.body.appendChild(dlg);
  const f = $("#uf", dlg);
  const desc = () => { $("#role-d", dlg).textContent = t("role_" + f.role.value + "_d"); };
  f.role.addEventListener("change", desc); desc();
  f.addEventListener("submit", async (e) => {
    if (e.submitter?.value === "cancel") { dlg.close(); return; }
    e.preventDefault();
    try {
      if (user) await api.put(`/api/users/${user.id}`, { role: f.role.value, steamId: f.steamId.value, newPassword: f.password.value || null });
      else await api.post("/api/users", { username: f.username.value, password: f.password.value, role: f.role.value, steamId: f.steamId.value });
      dlg.close(); toast(t("saved")); done();
    } catch (ex) { $("#uerr", dlg).textContent = errorText(ex.code); }
  });
  dlg.addEventListener("close", () => dlg.remove());
  dlg.showModal();
}

// ---------------------------------------------------------------- settings
async function renderSettings(page) {
  let s;
  try { s = await api.get("/api/settings"); } catch (ex) { toast(errorText(ex.code), true); return; }
  mount(page, html`<form class="card" id="sf">
    <dl class="kv"><dt>${t("version")}</dt><dd>${s.version}</dd><dt>${t("data_folder")}</dt><dd>${s.dataFolder}</dd></dl>
    <div class="form-row">
      <div><label>${t("local_port")}</label><input name="localPort" type="number" min="1" max="65535" value="${s.localPort}"></div>
      <div><label>${t("language")}</label><select name="language">
        ${["auto", "fr", "en"].map((l) => html`<option value="${l}" ${raw(s.language === l ? "selected" : "")}>${l === "auto" ? t("lang_auto") : l === "fr" ? "Français" : "English"}</option>`)}
      </select></div>
    </div>
    <label>${t("session_days")}</label><input name="sessionDays" type="number" min="1" max="365" value="${s.sessionDays}">
    <h3 class="section">${t("remote_access")}</h3>
    <label class="check"><input name="remoteEnabled" type="checkbox" ${raw(s.remoteEnabled ? "checked" : "")}> ${t("remote_enable")}</label>
    <div class="form-row">
      <div><label>${t("remote_port")}</label><input name="remotePort" type="number" min="1" max="65535" value="${s.remotePort}"></div>
      <div><label>${t("cert_password")}</label><input name="certificatePassword" type="password" placeholder="${s.hasCertificatePassword ? "••••••" : ""}"></div>
    </div>
    <label>${t("cert_path")}</label><input name="certificatePath" type="text" value="${s.certificatePath}">
    <div class="hint">${t("remote_warn")}</div>
    <h3 class="section">${t("proxy_title")}</h3>
    <label>${t("proxy_port")}</label><input name="proxyPort" type="number" min="0" max="65535" value="${s.proxyPort ?? 0}">
    <div class="hint">${t("proxy_hint")}</div>
    <h3 class="section">Let's Encrypt</h3>
    <label class="check"><input name="leEnabled" type="checkbox" ${raw(s.letsEncrypt?.enabled ? "checked" : "")}> ${t("le_enable")}</label>
    <div class="form-row">
      <div><label>${t("le_domain")}</label><input name="leDomain" type="text" value="${s.letsEncrypt?.domain ?? ""}" placeholder="eam.example.com"></div>
      <div><label>${t("le_email")}</label><input name="leEmail" type="text" inputmode="email" value="${s.letsEncrypt?.email ?? ""}"></div>
    </div>
    <label>${t("le_port")}</label><input name="lePort" type="number" min="1" max="65535" value="${s.letsEncrypt?.httpPort ?? 80}">
    <label class="check"><input name="leTerms" type="checkbox" ${raw(s.letsEncrypt?.acceptTerms ? "checked" : "")}> ${t("le_terms")}</label>
    <div class="hint">${t("le_hint")}</div>
    ${s.letsEncryptStatus?.expiresUtc ? html`<div class="hint">${t("le_valid", new Date(s.letsEncryptStatus.expiresUtc).toLocaleDateString())}</div>` : ""}
    ${s.letsEncryptStatus?.error ? html`<div class="error">${t("le_error", s.letsEncryptStatus.error)}</div>` : ""}
    <button class="btn primary block" type="submit">${t("save")}</button></form><div id="upd-settings"></div>`);
  renderUpdateSettings($("#upd-settings", page));
  $("#sf").addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = e.target;
    try {
      const r = await api.put("/api/settings", {
        localPort: +f.localPort.value, remoteEnabled: f.remoteEnabled.checked, remotePort: +f.remotePort.value, proxyPort: +f.proxyPort.value,
        certificatePath: f.certificatePath.value, certificatePassword: f.certificatePassword.value,
        language: f.language.value, sessionDays: +f.sessionDays.value,
        letsEncrypt: { enabled: f.leEnabled.checked, domain: f.leDomain.value.trim(), email: f.leEmail.value.trim(), httpPort: +f.lePort.value, acceptTerms: f.leTerms.checked },
      });
      setLanguage(f.language.value);
      toast(r.restartRequired ? t("restart_needed") : t("saved"));
    } catch (ex) { toast(ex.body?.detail || errorText(ex.code), true); }
  });
}

// ---------------------------------------------------------------- my account
function renderAccount(page) {
  mount(page, html`<form class="card" id="pf">
    <dl class="kv"><dt>${t("username")}</dt><dd>${state.me.username}</dd><dt>${t("role")}</dt><dd>${t("role_" + state.me.role)}</dd></dl>
    <h3 class="section">${t("change_password")}</h3>
    <label>${t("current_password")}</label><input name="cur" type="password" required autocomplete="current-password">
    <label>${t("new_password")}</label><input name="p1" type="password" required autocomplete="new-password">
    <div class="hint">${t("password_hint")}</div>
    <label>${t("password_confirm")}</label><input name="p2" type="password" required autocomplete="new-password">
    <div class="error" id="perr"></div>
    <button class="btn primary block" type="submit">${t("save")}</button></form>`);
  $("#pf").addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = e.target;
    if (f.p1.value !== f.p2.value) { $("#perr").textContent = t("passwords_differ"); return; }
    try {
      await api.post("/api/me/password", { currentPassword: f.cur.value, newPassword: f.p1.value });
      f.reset(); $("#perr").textContent = ""; toast(t("saved"));
    } catch (ex) { $("#perr").textContent = errorText(ex.code); }
  });
}

start();
