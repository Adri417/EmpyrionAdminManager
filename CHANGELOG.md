# Changelog

## 0.1.0 (2026-11-07) - Fondations / Foundations
- Moteur EAM : service Windows ou mode portable, base de données SQLite, journaux quotidiens.
- Interface unique (fenêtre de bureau + navigateur), français et anglais.
- Premier compte administrateur créé uniquement sur la machine d'EAM.
- Comptes et rôles : propriétaire, administrateur, game master, modérateur, lecture seule.
- Connexion sécurisée : mots de passe chiffrés (PBKDF2), blocage après 5 échecs, sessions coupées si le mot de passe ou le rôle change.
- Tableau de bord en direct : processeur, mémoire, disques, mémoire d'EAM.
- Accès à distance en HTTPS (désactivé par défaut), certificat auto-signé ou personnel.
- Scripts d'installation et de désinstallation du service Windows.
