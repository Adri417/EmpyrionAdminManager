# Changelog

## 0.9.25 (2026-11-11) - Liaison avec le mod stable, téléport vers un joueur
- Corrigé : sur un serveur avec beaucoup de structures, la liaison entre EAM et son mod en jeu se coupait toutes les 90 secondes (la liste des structures dépassait 1 Mo). Pendant chaque coupure, les actions passant par le mod échouaient (téléport, don d'objets, kick...). Les grandes réponses sont maintenant acceptées.
- Une action lancée pendant une reconnexion attend quelques secondes la liaison au lieu d'échouer. Si la liaison tombe pendant l'action, l'erreur s'affiche tout de suite au lieu d'attendre la fin du délai.
- Un message du mod qui ne peut pas être traité est ignoré et noté dans le journal, au lieu de couper la liaison. La cause d'une coupure est maintenant écrite dans le journal du serveur.
- Téléporter vers un joueur : choix du joueur dans une liste des joueurs connectés (plus besoin du Steam ID). Le nom du joueur est aussi accepté.
- Téléporter vers une structure : on choisit d'abord le playfield (avec le nombre de structures, celui du joueur en premier), puis la structure dans la liste (nom, type, propriétaire, numéro), avec une recherche.

## 0.9.24 (2026-11-10) - On reste dans le champ de texte
- Corrigé : pendant qu'un serveur tourne, chaque mise à jour en direct faisait sortir du champ de texte en cours de saisie (annonces, réglages, chat, console...). Les mises à jour en direct ne changent plus que la barre du haut et l'avertissement ; l'onglet ouvert n'est plus touché (champ, défilement et listes déroulantes restent comme ils sont).

## 0.9.23 (2026-11-09) - Annonces automatiques
- Nouvel onglet « Annonces » (administrateurs) : une liste de messages envoyés en jeu à tour de rôle, un toutes les N minutes (1 à 1440), puis retour au premier. Affichage au choix : chat, fenêtre à l'écran ou alerte rouge. Option pour n'annoncer que lorsque des joueurs sont connectés.

## 0.9.22 (2026-11-08) - Messages en double
- Corrigé : les messages d'EAM dans le chat (arrivées/départs, messages programmés, chat depuis EAM, compte à rebours) s'affichaient 2 fois en jeu. La commande console « say » est affichée deux fois par le jeu : le mod EAM écrit maintenant dans le chat par l'API de chat du jeu (une seule ligne). Repli sur « say » si cette API n'est pas disponible.
- Le mod EAM est mis à jour au prochain démarrage de chaque serveur.

## 0.9.21 (2026-11-08) - Inventaire visuel
- Inventaire des joueurs affiché comme en jeu : barre d'outils (9 cases) et sac (7 × 7), avec l'image de chaque item, la quantité dans le coin et le nom au survol. Mêmes grilles pour les copies de sacs à dos.
- Les images et les noms viennent du serveur lui-même (scénario de RE1, de RE2…) : images du scénario (SharedData\Content\Bundles\ItemIcons), puis du jeu, puis d'EAH si copié avec le serveur, ou eam\icons pour ajouter les siennes.
- Inventaire en direct quand le joueur est connecté (rafraîchi toutes les 5 s). Joueur déconnecté : dernière copie connue.
- Modification seulement quand le joueur est déconnecté : quantités, retrait, ajout d'items ; mise en place à sa prochaine connexion, après une copie de son inventaire actuel.
- Corrigé : numéros des items dans les inventaires = Id + 8192 (et non 4096) ; noms du scénario qui priment sur ceux du jeu ; blocs sans numéro lus dans blocksmap.dat de la sauvegarde.

## 0.9.20 (2026-11-08) - Alerte rouge et mise à jour automatique du jeu
- Compte à rebours de redémarrage/arrêt : alerte rouge avec alarme (en plus du chat) à 5, 3, 2 et 1 minute ; à 1 minute les joueurs sont déconnectés, puis le serveur sauvegarde et redémarre. Plus de message à 4 minutes.
- Nouveau mode de message « Alerte rouge (avec alarme) » dans le chat et le planificateur.
- Mises à jour du jeu (onglet Gameplay) : EAM regarde sur Steam toutes les heures. « Prévenir seulement » (par défaut) ou « Mettre à jour automatiquement » (compte à rebours, sauvegarde, mise à jour SteamCMD, redémarrage). Une version déjà essayée n'est jamais réessayée.
- Corrigé : la vérification des versions Steam ne donnait rien quand SteamCMD manquait (serveur copié depuis EAH) ; EAM l'installe maintenant tout seul.

## 0.9.19 (2026-11-08) - Kick à 1 minute du redémarrage
- Voir 0.9.18 : le compte à rebours envoie un message à 5, 4, 3 et 2 minutes, puis déconnecte tous les joueurs à 1 minute.

## 0.9.18 (2026-11-08) - Compte à rebours de redémarrage
- Avant un redémarrage ou un arrêt avec compte à rebours : un message chaque minute des 5 dernières minutes (5, 4, 3, 2) qui demande de se déconnecter (FR/EN), comme EAH. À 1 minute, tous les joueurs sont déconnectés (kick), puis encore une fois juste avant la sauvegarde pour ceux qui seraient revenus. Annuler l'arrêt annule aussi le kick.

## 0.9.17 (2026-11-08) - Chat qui remontait tout seul
- Corrigé pour de bon : quand un serveur tourne, chaque mise à jour en direct (toutes les quelques secondes) remettait le chat, la console et les journaux tout en haut. Les positions de défilement sont maintenant relevées avant de reconstruire la page. On peut lire les anciens messages à la molette sans être renvoyé ailleurs, et la liste suit les nouveaux messages seulement si on est déjà en bas.

## 0.9.16 (2026-11-08) - Correctif démarrage après import
- Corrigé : une clé ajoutée par EAM dans dedicated.yaml (mot de passe telnet...) était décalée de 4 espaces quand le fichier en utilise 2. Empyrion ne pouvait plus lire le fichier et lançait une partie neuve. EAM reprend maintenant le décalage du fichier et répare une clé mal décalée.
- Le mod de jeu d'EAH (EPF) est aussi mis de côté lors d'un import « copier », en plus de son plugin (EPM).

## 0.9.15 (2026-11-08) - Import « copier » plus sûr
- Le dossier de sauvegarde (SaveDirectory) de l'ancien serveur est redirigé vers la copie : la copie ne joue jamais sur la sauvegarde de l'original.
- Le plugin d'EAH copié avec le serveur est mis de côté (pas effacé), puisqu'EAM remplace EAH.
- Les tâches EAH « Wipe all » deviennent des wipes de POI (c'est ce qu'elles font dans EAH), « all » = tous les playfields.

## 0.9.14 (2026-11-08) - 100 % portable
- Tout reste dans le dossier d'EAM (`data` à côté des .exe) : réglages, comptes, journaux, certificats, clés, SteamCMD, mises à jour et serveurs par défaut. Plus rien dans C:\ProgramData, rien sur un autre disque sans votre choix.
- Au premier démarrage, les réglages et comptes d'une ancienne version sont recopiés depuis C:\ProgramData\EmpyrionAdminManager (sans les serveurs de jeu). L'ancien dossier n'est pas effacé.
- Le fichier portable.txt n'est plus nécessaire.

## 0.9.13 (2026-11-08) - Dossier des serveurs au choix
- Les nouveaux serveurs ne vont plus dans C:\ProgramData : par défaut à côté du programme EAM (ex. F:\EAM\servers), ou dans le dossier choisi dans Paramètres.
- Import « copier » : choix du dossier de destination, taille à copier et place libre affichées, refus clair s'il manque de la place.
- La mise à jour automatique ne touche jamais au dossier servers à côté du programme.
- Chat, console et journaux : ne remontent plus tout en haut à chaque mise à jour en direct. Ils suivent les nouveaux messages seulement si on est déjà en bas.

## 0.9.12 (2026-11-08) - Import EAH fidèle
- Tâches EAH : les listes de playfields séparées par « # » sont bien découpées, « all » = tous les playfields, chaque tâche garde son état (active ou non) comme dans EAH, deux tâches à la même heure ne fusionnent plus, les tâches mensuelles restent mensuelles.
- Planificateur : nouveau rythme « Tous les mois ».
- Nom du serveur : le nom EAM (qui sert aussi de nom de dossier, ex. RE2) est séparé du nom affiché dans la liste des serveurs du jeu. À l'import, ce nom est repris de la config du serveur.
- Import « copier » : un fichier que Windows interdit de lire n'arrête plus la copie. Il est laissé de côté et listé clairement.

## 0.9.11 (2026-11-08) - Messages d'erreur clairs
- Plus jamais « Une erreur est survenue » tout seul : EAM affiche la vraie cause (par exemple le fichier qui n'a pas pu être copié et pourquoi).
- Import « copier » : message clair quand Windows interdit à EAM de lire un fichier du serveur à copier.

## 0.9.10 (2026-11-08) - Accès derrière Apache, nginx ou IIS
- Nouveau réglage « Port pour le serveur web » : si un serveur web de la machine gère déjà HTTPS pour un domaine, il peut renvoyer vers EAM sans qu'EAM croie que les visiteurs sont sur la machine. Adresse réelle des visiteurs reprise pour la protection contre les essais de mot de passe.

## 0.9.9 (2026-11-08) - Import plus sûr
- Import EAH en mode « copier » : refusé si l'ancien serveur tourne encore, pour ne jamais copier une sauvegarde en cours d'écriture.

## 0.9.8 (2026-11-07) - Ce qui manquait encore par rapport à EAH et EWA
- Chat : recherche dans tout l'historique, filtre par canal, défilement auto activable, couleur par canal, message privé à un joueur connecté.
- Joueurs : bannir ou débannir par SteamID (même un joueur jamais venu), bouton « Débannir » dans la liste des bannis, liste des factions avec leurs membres en ligne, lien vers le profil Steam.
- Vue d'ensemble : dernières alertes des 24 dernières heures, et badge « Mise à jour du jeu disponible » (EAM regarde sur Steam toutes les 3 heures).
- Sécurité : playfields exclus des statistiques et du suivi des positions.

## 0.9.7 (2026-11-07) - Messages de connexion, nom de l'admin, bases plus solides
- Chat : option pour annoncer « X a rejoint le serveur » et « X a quitté le serveur » (textes modifiables, pseudo Steam). Pas d'annonce pour les joueurs déjà là quand EAM redémarre.
- Chat : les messages écrits dans EAM s'affichent en jeu avec le nom de l'admin (« Adrien: bonjour »). Option dans Communauté.
- Vue d'ensemble : un clic sur un joueur en ligne ouvre sa fiche.
- Expulsion : se fait par Steam ID et passe par le mod EAM (repli par telnet). La réponse du jeu est notée dans le journal du serveur, avec un nouvel essai si le joueur est encore là après 10 secondes.
- Bases de données : sous forte charge, les écritures d'EAM sur une même base passent chacune leur tour (plus d'erreur « database is locked » ni d'écriture perdue sous Windows) ; réglage SQLite recommandé (WAL + synchronous NORMAL), jamais de corruption même en cas de coupure.
- Tests anti-corruption : deux serveurs écrits en même temps par 18 tâches, et programme tué en pleine écriture, vérifiés à chaque envoi sous Windows.

## 0.9.6 (2026-11-07) - Correctif Windows
- Installation de scénario (Reforged Eden depuis le Workshop) : corrige l'erreur « accès refusé » quand Windows (antivirus, indexation, OneDrive) bloque un instant le dossier qui vient d'être copié. EAM réessaie, puis déplace fichier par fichier. Même protection pour les sauvegardes, les mods, les structures et le renommage d'un serveur.
- Si le remplacement d'un scénario échoue, l'ancienne version est remise en place.

## 0.9.5 (2026-11-07) - Import EAH, historique des sacs à dos, mise à jour automatique
- Import en un clic d'un serveur qui utilisait EAH : EAM lit seul ses bases chiffrées et ses réglages. Joueurs, notes et marqués, chat (90 jours), historiques (actions, kicks, bans, positions, intrusions), copies des sacs à dos, structures et leur historique, statistiques, réglages (MOTD, filtre, chatbot, Discord, limites, wipes), tâches planifiées (importées désactivées), modèles d'inventaire. Le serveur peut être copié dans EAM, utilisé tel quel, ou ses données ajoutées à un serveur EAM existant.
- Historique des sacs à dos : copie régulière du sac des joueurs connectés et à chaque mort, voir une copie, la rendre ou donner les objets en plus, inventaires à la mort de tous les joueurs, copies gardées, modèles d'inventaire.
- Mise à jour automatique : EAM cherche une nouvelle version sur GitHub toutes les heures, « Mise à jour disponible » en haut du menu, mise à jour en un clic sans toucher aux données ni arrêter les serveurs de jeu (copie de l'ancienne version gardée, retour arrière si la copie échoue).
- Joueurs : connectés en tête de liste et entourés en vert, mode dieu, notes des admins et joueurs marqués, jamais banni automatiquement, faction, ressources de plans, remise à zéro de la survie ou du temps de jeu, copies du fichier joueur, « prendre sa place », expulser tout le monde.
- Sauvegardes : sauvegarde des structures seules, sauvegardes gardées pour toujours, restauration partielle (structures ou fichier d'un joueur), suppression après N jours, espace disque minimum.
- Planificateur : suites d'actions avec délais, sauvegarde des structures, nettoyage du dossier Shared, contrôle des limites, nettoyage des joueurs inactifs et des copies de sacs à dos, wipe d'un système solaire entier avec exceptions.
- Monde : sanction des structures en trop (données à une faction puis détruites), nombre total de structures par joueur, structures publiques abandonnées, wipe seulement si le propriétaire n'est pas venu.
- Communauté : les joueurs peuvent repartir à zéro eux-mêmes (nombre gratuit puis délai).
- Mod EAM 0.9.5.

## 0.9.0 (2026-11-07) - Étapes 6 à 9 : monde, communauté, sécurité, outils
- Structures : liste complète (filtres par type, playfield, propriétaire, alimentation), fiche détaillée (blocs, appareils, objets), renommer, changer de propriétaire, déplacer (vers un playfield, une position ou un joueur), exporter, cloner, détruire (exportée d'abord), restaurer depuis la bibliothèque d'EAM, historique de chaque structure.
- Plans (.epb) : import, liste, apparition dans le jeu. Nettoyage du dossier Shared (dossiers de structures disparues mis de côté, jamais effacés).
- Limites par joueur ou faction (nombre, classe, blocs, par playfield), avertissement puis suppression après délai ; wipes automatiques (structures non visitées, playfields de départ) ; playfields réservés à certains joueurs ou factions.
- Playfields : liste, chargement, wipe ; carte 2D/3D des structures et joueurs ; carte de la galaxie.
- Chatbot des joueurs (préfixe au choix) : aide, infos, heure, prochain redémarrage et wipe, message privé, soin quotidien, kit de survie, ramener/aller à son vaisseau, détruire, redémarrage par les admins en jeu, commandes personnalisées.
- Alertes (mots surveillés, événements graves) avec son et notification, traduction des messages du chat, filtre d'insultes (avertissements puis bannissements progressifs), joueurs muets, message du jour, bienvenue, message à l'entrée d'un playfield.
- Pont Discord : chat global dans les deux sens, salons de faction, salon des admins pour les alertes.
- Sécurité : anti-triche (objets interdits, piles trop grandes, vie et nourriture, crédits qui sautent, bannissement automatique possible), journal des attaques de bases, historique « que s'est-il passé ici » (positions des joueurs et structures sur 14 jours), suivi d'un joueur, contrôle des pôles, téléportation d'urgence à la déconnexion, protection contre le flood telnet.
- Statistiques sur 7 jours : joueurs, FPS, mémoire, playfields, morts, cœurs posés et retirés, avec conseils.
- Let's Encrypt : certificat HTTPS gratuit, renouvelé tout seul.
- Gestionnaire de mods : installation (.zip ou .dll), activer, désactiver, supprimer (gardé de côté), édition des fichiers de réglages avec copie de l'ancien.
- Autominer : mineurs virtuels achetés avec des crédits, carburant, stock limité, 13 minerais par défaut.
- Événements : inscriptions des joueurs, départ avec kit et téléportation, inventaire mis de côté et rendu, retour à la position d'origine (même s'ils étaient déconnectés).
- Maintenance : compactage des bases, nouveau départ pour une nouvelle saison (sauvegarde avant), fichiers joueurs abîmés, convertisseur de numéros d'objets.
- Mod EAM 0.9.0 : structures, export/import, apparition, déplacements, statistiques du jeu.

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
