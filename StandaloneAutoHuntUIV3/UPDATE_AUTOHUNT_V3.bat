@echo off
chcp 65001 >nul
title AUTOHUNT STANDALONE UI V3
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0AUTOHUNT_STANDALONE_UI_V3.ps1"
echo.
pause
