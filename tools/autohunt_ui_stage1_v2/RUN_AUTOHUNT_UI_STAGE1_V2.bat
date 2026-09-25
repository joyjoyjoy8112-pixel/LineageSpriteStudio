@echo off
title AUTOHUNT UI STAGE1 V2
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0AUTOHUNT_UI_STAGE1_V2.ps1"
echo.
pause
