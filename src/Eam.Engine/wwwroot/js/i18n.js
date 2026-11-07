// Interface texts. Add a language by copying the "en" block.
const dict = {
  fr: {
    tagline: "Administration de serveurs Empyrion",
    setup_title: "Bienvenue",
    setup_lead: "Créez le compte administrateur principal. Il aura tous les droits.",
    setup_remote: "La création du premier compte se fait uniquement sur la machine où EAM est installé.",
    username: "Nom d'utilisateur", password: "Mot de passe", password_confirm: "Confirmer le mot de passe",
    password_hint: "10 caractères minimum.", create_account: "Créer le compte",
    login_title: "Connexion", login_lead: "Connectez-vous pour gérer vos serveurs.",
    remember: "Rester connecté", sign_in: "Se connecter", sign_out: "Déconnexion",
    nav_dashboard: "Tableau de bord", nav_servers: "Serveurs", nav_users: "Comptes", nav_settings: "Paramètres", nav_account: "Mon compte",
    soon: "bientôt", live: "En direct", offline: "Hors ligne",
    machine: "Machine", cpu: "Processeur", memory: "Mémoire", disks: "Disques", eam_process: "EAM",
    uptime: "Allumée depuis", eam_uptime: "EAM lancé depuis", system: "Système", cores: "cœurs", free: "libres",
    servers_empty_title: "Aucun serveur pour l'instant",
    servers_empty_text: "L'installation et la gestion des serveurs Empyrion arrivent à l'étape 2.",
    users_title: "Comptes", add_user: "Ajouter un compte", role: "Rôle", steam_id: "Steam ID (optionnel)",
    last_login: "Dernière connexion", never: "jamais", edit: "Modifier", delete: "Supprimer", save: "Enregistrer", cancel: "Annuler",
    new_password_opt: "Nouveau mot de passe (laisser vide pour ne pas changer)", confirm_delete: "Supprimer le compte {0} ?",
    role_owner: "Propriétaire", role_admin: "Administrateur", role_gamemaster: "Game Master", role_moderator: "Modérateur", role_viewer: "Lecture seule",
    role_owner_d: "Tout, y compris les comptes et les paramètres d'EAM.",
    role_admin_d: "Gère les serveurs : démarrage, sauvegardes, configuration.",
    role_gamemaster_d: "Joueurs, structures, chat, téléportations.",
    role_moderator_d: "Chat, kick, bannissements temporaires.",
    role_viewer_d: "Voit tout, ne modifie rien.",
    settings_title: "Paramètres", local_port: "Port de l'interface locale",
    remote_access: "Accès à distance (HTTPS)", remote_enable: "Autoriser l'accès depuis d'autres machines",
    remote_port: "Port HTTPS", cert_path: "Certificat .pfx (vide = certificat auto-signé)", cert_password: "Mot de passe du certificat",
    language: "Langue", lang_auto: "Automatique", session_days: "Durée de connexion (jours)",
    data_folder: "Dossier des données", version: "Version",
    saved: "Enregistré.", restart_needed: "Enregistré. Redémarrez le moteur EAM pour appliquer les changements réseau.",
    remote_warn: "Pensez à ouvrir ce port dans le pare-feu Windows et sur votre box seulement si vous en avez besoin.",
    account_title: "Mon compte", change_password: "Changer le mot de passe", current_password: "Mot de passe actuel", new_password: "Nouveau mot de passe",
    passwords_differ: "Les mots de passe ne correspondent pas.",
    err_invalid_credentials: "Nom d'utilisateur ou mot de passe incorrect.",
    err_too_many_attempts: "Trop d'essais. Réessayez dans {0} min.",
    err_password_too_short: "Le mot de passe doit faire au moins 10 caractères.",
    err_invalid_username: "Nom d'utilisateur : 3 à 32 caractères (lettres, chiffres, . _ -).",
    err_username_taken: "Ce nom d'utilisateur existe déjà.",
    err_invalid_steam_id: "Le Steam ID doit faire 17 chiffres.",
    err_last_owner: "Il doit rester au moins un propriétaire.",
    err_invalid_current_password: "Mot de passe actuel incorrect.",
    err_setup_local_only: "La création du premier compte doit se faire sur la machine d'EAM.",
    err_certificate_not_found: "Fichier de certificat introuvable.",
    err_invalid_settings: "Paramètres invalides.",
    err_network: "Le moteur EAM ne répond pas.",
    err_generic: "Une erreur est survenue.",
    days: "j", hours: "h", minutes: "min",
  },
  en: {
    tagline: "Empyrion server administration",
    setup_title: "Welcome",
    setup_lead: "Create the main administrator account. It will have every permission.",
    setup_remote: "The first account can only be created on the machine where EAM is installed.",
    username: "Username", password: "Password", password_confirm: "Confirm password",
    password_hint: "At least 10 characters.", create_account: "Create account",
    login_title: "Sign in", login_lead: "Sign in to manage your servers.",
    remember: "Stay signed in", sign_in: "Sign in", sign_out: "Sign out",
    nav_dashboard: "Dashboard", nav_servers: "Servers", nav_users: "Accounts", nav_settings: "Settings", nav_account: "My account",
    soon: "soon", live: "Live", offline: "Offline",
    machine: "Machine", cpu: "Processor", memory: "Memory", disks: "Disks", eam_process: "EAM",
    uptime: "Machine up for", eam_uptime: "EAM up for", system: "System", cores: "cores", free: "free",
    servers_empty_title: "No server yet",
    servers_empty_text: "Installing and managing Empyrion servers comes in step 2.",
    users_title: "Accounts", add_user: "Add account", role: "Role", steam_id: "Steam ID (optional)",
    last_login: "Last sign-in", never: "never", edit: "Edit", delete: "Delete", save: "Save", cancel: "Cancel",
    new_password_opt: "New password (leave empty to keep it)", confirm_delete: "Delete account {0}?",
    role_owner: "Owner", role_admin: "Administrator", role_gamemaster: "Game Master", role_moderator: "Moderator", role_viewer: "Read only",
    role_owner_d: "Everything, including accounts and EAM settings.",
    role_admin_d: "Runs the servers: start, backups, configuration.",
    role_gamemaster_d: "Players, structures, chat, warps.",
    role_moderator_d: "Chat, kick, temporary bans.",
    role_viewer_d: "Sees everything, changes nothing.",
    settings_title: "Settings", local_port: "Local interface port",
    remote_access: "Remote access (HTTPS)", remote_enable: "Allow access from other machines",
    remote_port: "HTTPS port", cert_path: ".pfx certificate (empty = self-signed)", cert_password: "Certificate password",
    language: "Language", lang_auto: "Automatic", session_days: "Sign-in duration (days)",
    data_folder: "Data folder", version: "Version",
    saved: "Saved.", restart_needed: "Saved. Restart the EAM engine to apply network changes.",
    remote_warn: "Only open this port in Windows Firewall and on your router if you need it.",
    account_title: "My account", change_password: "Change password", current_password: "Current password", new_password: "New password",
    passwords_differ: "Passwords do not match.",
    err_invalid_credentials: "Wrong username or password.",
    err_too_many_attempts: "Too many attempts. Try again in {0} min.",
    err_password_too_short: "The password must be at least 10 characters.",
    err_invalid_username: "Username: 3 to 32 characters (letters, digits, . _ -).",
    err_username_taken: "This username already exists.",
    err_invalid_steam_id: "The Steam ID must be 17 digits.",
    err_last_owner: "At least one owner must remain.",
    err_invalid_current_password: "Current password is wrong.",
    err_setup_local_only: "The first account must be created on the EAM machine.",
    err_certificate_not_found: "Certificate file not found.",
    err_invalid_settings: "Invalid settings.",
    err_network: "The EAM engine is not responding.",
    err_generic: "Something went wrong.",
    days: "d", hours: "h", minutes: "min",
  },
};

let lang = "fr";

export function setLanguage(pref) {
  if (pref === "fr" || pref === "en") lang = pref;
  else lang = (navigator.language || "en").toLowerCase().startsWith("fr") ? "fr" : "en";
  document.documentElement.lang = lang;
}

export function t(key, ...args) {
  let s = dict[lang][key] ?? dict.en[key] ?? key;
  args.forEach((a, i) => { s = s.replace(`{${i}}`, a); });
  return s;
}

export function errorText(code, extra) {
  if (code === "too_many_attempts") return t("err_too_many_attempts", Math.ceil((extra?.retryAfterSeconds ?? 900) / 60));
  const key = "err_" + code;
  return dict[lang][key] ? t(key) : t("err_generic");
}

export const currentLanguage = () => lang;
