@echo off
title SPARC - Sistema de Provisionamento Claro
taskkill /F /IM NetworkDevice.UI.exe 2>nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\launch_with_update.ps1" -ForceRebuild

