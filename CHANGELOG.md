# Changelog

## 0.5.0 (2026-11-07) - Étapes 4 et 5 : sauvegardes, planificateur, options, scénarios, joueurs
- Sauvegardes de chaque serveur : partie (Saves\Games\<nom>), plans des joueurs, adminconfig.yaml, options et données EAM. Création à chaud, téléchargement, suppression, restauration (serveur arrêté, avec sauvegarde de sécurité avant), conservation des N dernières automatiques, copie dans un dossier Google Drive / OneDrive.
- Planificateur : redémarrage (avec avertissements et sauvegarde avant), arrêt, démarrage, sauvegarde, message, commande console, wipe de playfields, nettoyage des journaux, mise à jour du jeu. Heures fixes par jour de la semaine ou intervalle ; « Lancer maintenant ».
- Options : toutes les options de dedicated.yaml (actives ou en commentaire dans le modèle du jeu, avec leur aide), nom de la sauvegarde, scénario, mode, graine, édition du fichier complet, et toutes les options de jeu de la sauvegarde (gameoptions.yaml).
- Scénarios : liste des scénarios installés, installation et mise à jour de Reforged Eden 1 et 2 (ou de tout scénario) depuis le Steam Workshop, import depuis un dossier, suppression.
- Joueurs : tous les joueurs vus, fiche détaillée, inventaire (modifiable), crédits, expérience et points, objet à donner (catalogue traduit du scénario), téléportation, soin, plan terminé, message (remis à la prochaine connexion s'il est hors ligne), expulsion, ban/déban, liste des bannis, rôles (adminconfig.yaml), historique complet des actions et des connexions.
- Mod EAM 0.5.0 : nouvelles commandes (inventaire, crédits, expérience, téléportation, plans, bannis) avec réponse du jeu.

## 0.3.2 (2026-11-07) - Direct plus réactif, barre du haut
- Joueurs actualisés toutes les 5 secondes, factions toutes les 15 secondes et immédiatement quand un joueur change de faction (création, départ, suppression).
- Les écrans ne sont mis à jour que si quelque chose a vraiment changé (pas de trafic ni de redessin inutile).
- Page d'un serveur : nom, pastille d'état et boutons (Arrêter, Redémarrer, Forcer l'arrêt...) regroupés dans la barre du haut, plus de double en-tête.

## 0.3.1 (2026-11-07) - Corrections
- Le mod EAM ne se chargeait pas : le jeu attend Content\Mods\EAM\EAM.dll (même nom que le dossier). Le DLL s'appelle maintenant EAM.dll ; l'ancien EamMod.dll est retiré automatiquement.
- « Forcer l'arrêt » ou « Arrêter » n'est plus pris pour un plantage suivi d'un redémarrage automatique quand des processus du jeu mettent du temps à se fermer.

## 0.3.0 (2026-11-07) - Mod EAM et vue en direct / EAM mod and live view
- Mod EAM écrit à neuf, installé et mis à jour automatiquement dans chaque serveur à chaque démarrage (Content\Mods\EAM).
- Lien local uniquement (127.0.0.1), une clé secrète différente par serveur, aucun port ouvert sur Internet.
- Vue en direct : joueurs en ligne (faction, playfield, niveau, ping, temps de connexion), playfields actifs (joueurs, FPS, mémoire, structures), FPS du serveur.
- Processeur et mémoire utilisés par chaque serveur (tous ses processus).
- Chat en direct, historique conservé 90 jours dans la base du serveur, envoi d'un message dans le chat ou en fenêtre à l'écran des joueurs.
- Notifications quand un joueur se connecte ou se déconnecte.
- Base de données propre à chaque serveur (chat, joueurs vus, temps de jeu).

## 0.2.0 (2026-11-07) - Serveurs / Servers
- SteamCMD intégré : téléchargé automatiquement au premier besoin, dans le dossier d'EAM.
- Création d'un serveur avec le nom de son choix (nom EAM, dossier et nom dans la liste du jeu), renommable ensuite.
- Installation, mise à jour et vérification des fichiers d'Empyrion depuis EAM, avec progression en direct (branche stable ou expérimentale).
- Plusieurs serveurs séparés : un dossier par serveur, ports attribués et vérifiés automatiquement, configuration propre à EAM (dedicated-eam.yaml) jamais écrasée par Steam.
- Une seule opération à la fois par serveur (protection contre la corruption).
- Démarrer, arrêter avec avertissement des joueurs et compte à rebours (annulable), redémarrer, forcer l'arrêt.
- Surveillance : détection des plantages, redémarrage automatique, suspension après 3 plantages en 15 minutes.
- Console telnet, journal du jeu et journal EAM de chaque serveur.
- Tableau de bord : liste des serveurs et leur état en direct.

## 0.1.0 (2026-11-07) - Fondations / Foundations
- Moteur EAM : service Windows ou mode portable, base de données SQLite, journaux quotidiens.
- Interface unique (fenêtre de bureau + navigateur), français et anglais.
- Premier compte administrateur créé uniquement sur la machine d'EAM.
- Comptes et rôles : propriétaire, administrateur, game master, modérateur, lecture seule.
- Connexion sécurisée : mots de passe chiffrés (PBKDF2), blocage après 5 échecs, sessions coupées si le mot de passe ou le rôle change.
- Tableau de bord en direct : processeur, mémoire, disques, mémoire d'EAM.
- Accès à distance en HTTPS (désactivé par défaut), certificat auto-signé ou personnel.
- Scripts d'installation et de désinstallation du service Windows.
