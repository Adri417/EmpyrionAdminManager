@echo off
rem Installs the EAM engine as a Windows service (starts with Windows, restarts after a crash).
rem Installe le moteur EAM comme service Windows (demarre avec Windows, redemarre apres un plantage).
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0tools\service.ps1\" install'"
