@echo off
rem Removes the EAM Windows service. Data and game servers are kept.
rem Supprime le service Windows EAM. Les donnees et les serveurs de jeu sont conserves.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0tools\service.ps1\" uninstall'"
