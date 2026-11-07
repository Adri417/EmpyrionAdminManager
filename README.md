# Empyrion Admin Manager (EAM)

Outil libre (MIT) pour installer, administrer et surveiller un ou plusieurs serveurs dédiés
**Empyrion - Galactic Survival** depuis une application de bureau Windows ou un navigateur.

Free (MIT) tool to install, administer and monitor one or more **Empyrion - Galactic Survival**
dedicated servers from a Windows desktop app or a browser.

## Architecture

| Part | Folder | Role |
| --- | --- | --- |
| Engine | `src/Eam.Engine` | ASP.NET Core service (Windows service or portable). Serves the interface and the API, runs scheduled jobs. |
| Core | `src/Eam.Core` | Configuration, database, accounts, monitoring. No UI. |
| Desktop | `src/Eam.Desktop` | Small WinForms window hosting the same interface with WebView2. Starts the engine if needed. |
| Game mod | `src/Eam.Mod` | Loaded by each Empyrion dedicated server (.NET Framework 4.8). Talks to the engine on 127.0.0.1 only, authenticated with a per-server key. Installed automatically by the engine at every start. |
| Link | `src/Eam.Link` | Protocol shared by the engine and the mod (handshake, tiny JSON). |
| Interface | `src/Eam.Engine/wwwroot` | Plain HTML/CSS/JavaScript modules, no framework, no build step. Strict CSP. |
| Tests | `tests/Eam.Tests` | xUnit tests. |

Every function lives in the shared interface, so the desktop app and the web access always offer the same features.

## Build

```
dotnet test
tools/publish.ps1   # Windows, or tools/publish.sh on Linux with a Microsoft .NET 8 SDK
```

The release lands in `dist/EmpyrionAdminManager-<version>.zip`.

## Data

`%ProgramData%\EmpyrionAdminManager` (or `data\` next to the executables when `portable.txt` exists, or the
`EAM_DATA` environment variable): `eam.json` (settings), `eam.db` (accounts), `logs\`, `keys\`, `certificates\`,
and later `servers\<name>\` (one fully separate folder per game server) and `steamcmd\`.

## Roadmap

1. Foundations (done)
2. SteamCMD, server install/update, multi-server isolation, start/stop, console (done)
3. Game mod + live players/chat (done)
4. Scheduler and backups, every server and game option, scenarios from the Steam Workshop (done)
5. Players: details, inventory, credits, experience, items, teleport, kick/ban, roles, offline messages, history (done)
6. Structures and playfields
7. Chat bot, Discord
8. Anti-cheat, logs, statistics
9. Mod manager, events, autominer, remaining features
10. Migration and first public release

Empyrion - Galactic Survival is a trademark of Eleon Game Studios. This project is not affiliated with Eleon.
