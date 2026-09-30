@echo off
title SPARC - Painel de Administracao e Homologacao
taskkill /F /IM NetworkDevice.UI.exe 2>nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\launch_with_update.ps1" -AppArgs "--admin"
