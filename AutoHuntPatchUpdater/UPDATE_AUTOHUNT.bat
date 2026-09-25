@echo off
chcp 65001 >nul
title AUTOHUNT PATCH UPDATER
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Update-AutoHunt.ps1"
echo.
pause
