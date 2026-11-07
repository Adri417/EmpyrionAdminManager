// Step 7: chatbot, alerts, insult filter, mute, welcome messages, Discord.
import { api } from "./api.js";
import { t, extend } from "./i18n.js";
import { html, mount, $, $$, toast } from "./dom.js";
import { atLeast, fail, when, dialog, settingsForm } from "./ui.js";

extend({
  fr: {
    tab_community: "Communauté", cm_moderation: "Modération", cm_mutes: "Joueurs muets", cm_warnings: "Avertissements du filtre", cm_none: "Personne.",
    cm_mute: "Rendre muet", cm_unmute: "Fin du muet", cm_reset: "Effacer", cm_minutes: "Durée (minutes)", cm_player: "Joueur",
    cm_until: "Jusqu'à", cm_mute_hint: "Le jeu ne permet pas de bloquer le chat d'un joueur : EAM l'avertit à chaque message, puis l'expulse s'il continue.",
    cm_chatbot: "Chatbot des joueurs", cm_bot_on: "Activer le chatbot", cm_prefix: "Préfixe des commandes", cm_cooldown: "Délai entre deux commandes qui modifient le jeu (secondes)",
    cm_bot_hint: "Commandes fournies : ? (aide), info, time, nextrestart, nextwipe, w:<nom>:<message>, heal (1 fois par jour), survival, getshiphere:<id>, gotoship:<id>, destroy:<id>, restart et shutdown (admins en jeu), !mods.",
    cm_heal: "Autoriser heal (1 fois par jour)", cm_ships: "Autoriser getshiphere, gotoship et destroy (structures du joueur ou de sa faction)",
    cm_admin_cmds: "Autoriser restart et shutdown aux admins en jeu (rôle Admin)", cm_custom: "Commandes personnalisées", cm_cmd: "Commande", cm_desc: "Description",
    cm_resp: "Réponse ({PLAYERNAME} possible)", cm_survival: "Kit de survie (CB:survival)", cm_survival_on: "Activer le kit de survie", cm_kit: "Objets du kit",
    cm_item: "Numéro d'objet", cm_count: "Quantité", cm_credits: "Crédits donnés", cm_season: "Saison (changer ce nom permet de reprendre le kit, par exemple après un wipe)",
    cm_alerts: "Alertes", cm_alert_words: "Mots qui déclenchent une alerte (un par ligne)", cm_alert_hint: "Le message est surligné, signalé dans EAM et envoyé au salon Discord des admins.",
    cm_filter: "Filtre d'insultes", cm_filter_on: "Activer le filtre", cm_filter_words: "Mots interdits (un par ligne, les accents et majuscules sont ignorés)",
    cm_filter_global: "Seulement dans le chat global", cm_filter_warnings: "Avertissements avant bannissement", cm_filter_ban: "Premier bannissement (minutes, doublé à chaque récidive)",
    cm_muted_kick: "Expulser un joueur muet après ce nombre de messages (0 = jamais)",
    cm_messages: "Messages automatiques", cm_motd: "Message du jour (à chaque connexion, {PLAYERNAME} possible)", cm_welcome: "Bienvenue (première connexion uniquement)",
    cm_pf_msgs: "Message à l'entrée d'un playfield", cm_translate: "Langue de traduction des messages (fr, en, de…)",
    cm_discord: "Discord", cm_discord_on: "Activer le pont Discord", cm_token: "Jeton du bot (laisser vide pour garder l'actuel)",
    cm_discord_hint: "Créez une application sur discord.com/developers, ajoutez un bot, activez « Message Content Intent », invitez-le sur votre serveur Discord avec les droits lire et envoyer des messages. Copiez les numéros des salons (mode développeur de Discord, clic droit, Copier l'identifiant).",
    cm_public: "Salon public (chat global dans les deux sens)", cm_admin_ch: "Salon des admins (alertes, sens unique)", cm_fac_ch: "Salons de faction (dans les deux sens)",
    cm_faction: "Numéro de faction", cm_channel: "Numéro du salon", cm_test: "Envoyer un message de test", cm_status: "État : {0}",
    alert_title: "Alerte {0}", err_discord_error: "Discord a refusé la demande.", err_discord_token_missing: "Indiquez le jeton du bot.",
    err_invalid_prefix: "Préfixe invalide.", err_invalid_cooldown: "Délai invalide.", err_invalid_command: "Commande invalide (lettres et chiffres, réponse obligatoire).",
    err_invalid_kit: "Kit invalide.", err_invalid_filter: "Réglages du filtre invalides.", err_invalid_message: "Message trop long ou incomplet.",
    err_invalid_language: "Langue invalide.", err_invalid_discord_channel: "Numéro de salon invalide.", err_network_error: "Connexion impossible.",
  },
  en: {
    tab_community: "Community", cm_moderation: "Moderation", cm_mutes: "Muted players", cm_warnings: "Filter warnings", cm_none: "Nobody.",
    cm_mute: "Mute", cm_unmute: "Unmute", cm_reset: "Clear", cm_minutes: "Duration (minutes)", cm_player: "Player",
    cm_until: "Until", cm_mute_hint: "The game cannot block a player's chat: EAM warns them at every message, then kicks them if they go on.",
    cm_chatbot: "Players' chatbot", cm_bot_on: "Enable the chatbot", cm_prefix: "Command prefix", cm_cooldown: "Delay between two commands that change the game (seconds)",
    cm_bot_hint: "Built-in commands: ? (help), info, time, nextrestart, nextwipe, w:<name>:<message>, heal (once a day), survival, getshiphere:<id>, gotoship:<id>, destroy:<id>, restart and shutdown (in-game admins), !mods.",
    cm_heal: "Allow heal (once a day)", cm_ships: "Allow getshiphere, gotoship and destroy (structures of the player or their faction)",
    cm_admin_cmds: "Allow restart and shutdown for in-game admins (Admin role)", cm_custom: "Custom commands", cm_cmd: "Command", cm_desc: "Description",
    cm_resp: "Answer ({PLAYERNAME} allowed)", cm_survival: "Survival kit (CB:survival)", cm_survival_on: "Enable the survival kit", cm_kit: "Kit items",
    cm_item: "Item number", cm_count: "Count", cm_credits: "Credits given", cm_season: "Season (changing this name lets players claim the kit again, e.g. after a wipe)",
    cm_alerts: "Alerts", cm_alert_words: "Words that raise an alert (one per line)", cm_alert_hint: "The message is highlighted, reported in EAM and sent to the Discord admin channel.",
    cm_filter: "Insult filter", cm_filter_on: "Enable the filter", cm_filter_words: "Forbidden words (one per line, accents and case are ignored)",
    cm_filter_global: "Only in the global chat", cm_filter_warnings: "Warnings before a ban", cm_filter_ban: "First ban (minutes, doubled at each repeat)",
    cm_muted_kick: "Kick a muted player after this many messages (0 = never)",
    cm_messages: "Automatic messages", cm_motd: "Message of the day (every login, {PLAYERNAME} allowed)", cm_welcome: "Welcome (first login only)",
    cm_pf_msgs: "Message when entering a playfield", cm_translate: "Language messages are translated to (fr, en, de…)",
    cm_discord: "Discord", cm_discord_on: "Enable the Discord bridge", cm_token: "Bot token (leave empty to keep the current one)",
    cm_discord_hint: "Create an application on discord.com/developers, add a bot, enable \"Message Content Intent\", invite it to your Discord server with read and send message permissions. Copy the channel numbers (Discord developer mode, right click, Copy ID).",
    cm_public: "Public channel (global chat both ways)", cm_admin_ch: "Admin channel (alerts, one way)", cm_fac_ch: "Faction channels (both ways)",
    cm_faction: "Faction number", cm_channel: "Channel number", cm_test: "Send a test message", cm_status: "Status: {0}",
    alert_title: "Alert {0}", err_discord_error: "Discord refused the request.", err_discord_token_missing: "Enter the bot token.",
    err_invalid_prefix: "Invalid prefix.", err_invalid_cooldown: "Invalid delay.", err_invalid_command: "Invalid command (letters and digits, answer required).",
    err_invalid_kit: "Invalid kit.", err_invalid_filter: "Invalid filter settings.", err_invalid_message: "Message too long or incomplete.",
    err_invalid_language: "Invalid language.", err_invalid_discord_channel: "Invalid channel number.", err_network_error: "Connection failed.",
  },
});

export const communityTabs = (role) => (atLeast(role, "moderator") ? ["community"] : []);

let audio;
function beep() {
  try {
    audio ??= new AudioContext();
    const o = audio.createOscillator(), g = audio.createGain();
    o.frequency.value = 880; g.gain.value = 0.08;
    o.connect(g); g.connect(audio.destination);
    o.start(); o.stop(audio.currentTime + 0.25);
  } catch { /* no sound available */ }
}

/** Alerts from every server: shown as a notification, with a sound for the serious ones. */
export function communityEvent(msg) {
  if (msg.type !== "alert") return false;
  const e = msg.entry;
  if (e.severity >= 1) {
    toast(`${t("alert_title", msg.server)} · ${e.category}: ${e.name ? e.name + " — " : ""}${e.text}`, e.severity >= 2);
    if (e.severity >= 2 || e.category === "alert") beep();
  }
  document.dispatchEvent(new CustomEvent("eam:alert", { detail: msg }));
  return true;
}

export async function renderCommunity(tab, el, s, me) {
  mount(el, html`<div class="card" id="cm-mod"></div><div id="cm-form"></div>`);
  await moderation($("#cm-mod", el), s);
  if (!atLeast(me.role, "admin")) return;
  await settingsForm($("#cm-form", el), `/api/servers/${s.id}/features/community`, [
    { title: t("cm_chatbot"), hint: t("cm_bot_hint"), fields: [
      { k: "chatbotEnabled", type: "check", label: t("cm_bot_on") },
      { k: "prefix", type: "text", label: t("cm_prefix") },
      { k: "cooldownSeconds", type: "number", label: t("cm_cooldown") },
      { k: "healEnabled", type: "check", label: t("cm_heal") },
      { k: "shipCommandsEnabled", type: "check", label: t("cm_ships") },
      { k: "adminCommandsEnabled", type: "check", label: t("cm_admin_cmds") },
      { k: "commands", type: "rows", label: t("cm_custom"), cols: [
        { k: "command", label: t("cm_cmd"), type: "text" }, { k: "description", label: t("cm_desc"), type: "text" }, { k: "response", label: t("cm_resp"), type: "text" }] }] },
    { title: t("cm_survival"), open: false, fields: [
      { k: "survivalKitEnabled", type: "check", label: t("cm_survival_on") },
      { k: "survivalKit", type: "rows", label: t("cm_kit"), cols: [{ k: "id", label: t("cm_item"), type: "number" }, { k: "count", label: t("cm_count"), type: "number", default: 1 }] },
      { k: "survivalCredits", type: "number", label: t("cm_credits") },
      { k: "survivalSeason", type: "text", label: t("cm_season") }] },
    { title: t("cm_alerts"), hint: t("cm_alert_hint"), open: false, fields: [{ k: "alertWords", type: "lines", label: t("cm_alert_words") }] },
    { title: t("cm_filter"), open: false, fields: [
      { k: "filterEnabled", type: "check", label: t("cm_filter_on") },
      { k: "filterWords", type: "lines", label: t("cm_filter_words"), rows: 6 },
      { k: "filterGlobalOnly", type: "check", label: t("cm_filter_global") },
      { k: "filterWarnings", type: "number", label: t("cm_filter_warnings") },
      { k: "filterFirstBanMinutes", type: "number", label: t("cm_filter_ban") },
      { k: "mutedKickAfter", type: "number", label: t("cm_muted_kick") }] },
    { title: t("cm_messages"), open: false, fields: [
      { k: "motd", type: "textarea", label: t("cm_motd") },
      { k: "welcome", type: "textarea", label: t("cm_welcome") },
      { k: "playfieldMessages", type: "rows", label: t("cm_pf_msgs"), cols: [{ k: "playfield", label: t("col_playfield"), type: "text" }, { k: "text", label: t("p_text"), type: "text" }] },
      { k: "translateTo", type: "text", label: t("cm_translate") }] },
    { title: t("cm_discord"), hint: t("cm_discord_hint"), open: false, fields: [
      { k: "discordEnabled", type: "check", label: t("cm_discord_on") },
      { k: "discordToken", type: "password", label: t("cm_token") },
      { k: "discordPublicChannel", type: "text", label: t("cm_public") },
      { k: "discordAdminChannel", type: "text", label: t("cm_admin_ch") },
      { k: "discordFactionChannels", type: "rows", label: t("cm_fac_ch"), cols: [{ k: "factionId", label: t("cm_faction"), type: "number" }, { k: "channelId", label: t("cm_channel"), type: "text" }] }] },
  ]);
  const form = $("#cm-form form", el);
  const last = $$("details", form).pop();
  const box = document.createElement("div");
  box.className = "actions-row";
  mount(box, html`<button type="button" class="btn" id="dc-test">${t("cm_test")}</button><span class="hint" id="dc-status"></span>`);
  last.appendChild(box);
  $("#dc-test", el).addEventListener("click", async () => {
    try { await api.post(`/api/servers/${s.id}/discord/test`); toast(t("done")); } catch (ex) { fail(ex); }
  });
  try { const st = await api.get(`/api/servers/${s.id}/discord/status`); if (st.status) $("#dc-status", el).textContent = t("cm_status", st.status); } catch { /* ignore */ }
}

async function moderation(box, s) {
  let m = { mutes: [], warnings: [] }, players = [];
  try { [m, players] = await Promise.all([api.get(`/api/servers/${s.id}/moderation`), api.get(`/api/servers/${s.id}/players`)]); } catch (ex) { fail(ex); }
  mount(box, html`<h3>${t("cm_moderation")}</h3>
    <div class="actions-row"><button class="btn" id="cm-mute">${t("cm_mute")}</button></div><div class="hint">${t("cm_mute_hint")}</div>
    <h4>${t("cm_mutes")}</h4>${m.mutes.length ? html`<div class="table-wrap"><table><tbody>${m.mutes.map((x) => html`<tr><td><b>${x.name || x.steamId}</b></td>
      <td>${t("cm_until")} ${when(x.value)}</td><td><button class="btn small" data-unmute="${x.steamId}">${t("cm_unmute")}</button></td></tr>`)}</tbody></table></div>` : html`<div class="hint">${t("cm_none")}</div>`}
    <h4>${t("cm_warnings")}</h4>${m.warnings.length ? html`<div class="table-wrap"><table><tbody>${m.warnings.map((x) => html`<tr><td><b>${x.name || x.steamId}</b></td>
      <td>${x.count}</td><td class="hint">${when(x.lastUtc)}</td><td><button class="btn small" data-reset="${x.steamId}">${t("cm_reset")}</button></td></tr>`)}</tbody></table></div>` : html`<div class="hint">${t("cm_none")}</div>`}`);
  const again = () => moderation(box, s);
  $("#cm-mute", box).addEventListener("click", () => dialog(t("cm_mute"), html`<label>${t("cm_player")}</label><select name="steam">${players.map((p) =>
    html`<option value="${p.steamId}">${p.name}${p.online ? " ●" : ""}</option>`)}</select><label>${t("cm_minutes")}</label><input name="min" type="number" value="60" min="1">`,
  async (f) => { await api.post(`/api/servers/${s.id}/moderation/mute`, { steamId: f.steam.value, minutes: +f.min.value }); again(); }, t("cm_mute")));
  $$("[data-unmute]", box).forEach((b) => b.addEventListener("click", async () => {
    try { await api.post(`/api/servers/${s.id}/moderation/mute`, { steamId: b.dataset.unmute, minutes: 0 }); again(); } catch (ex) { fail(ex); }
  }));
  $$("[data-reset]", box).forEach((b) => b.addEventListener("click", async () => {
    try { await api.post(`/api/servers/${s.id}/moderation/reset`, { steamId: b.dataset.reset }); again(); } catch (ex) { fail(ex); }
  }));
}
