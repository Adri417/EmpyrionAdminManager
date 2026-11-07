# Changelog

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
