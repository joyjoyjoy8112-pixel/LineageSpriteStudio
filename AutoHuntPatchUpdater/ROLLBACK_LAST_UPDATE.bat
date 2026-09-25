@echo off
chcp 65001 >nul
title AUTOHUNT PATCH ROLLBACK
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Rollback-AutoHunt.ps1"
echo.
pause
