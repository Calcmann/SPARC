@echo off
chcp 65001 >nul
title SPARC - Configurar Senha de Teste FortiGate 40F
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0configurar_senha_teste_40f.ps1" %*
pause
