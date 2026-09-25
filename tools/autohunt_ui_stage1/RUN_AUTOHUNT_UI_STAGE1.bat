@echo off
title AUTOHUNT UI STAGE1
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0AUTOHUNT_UI_STAGE1.ps1"
echo.
pause
