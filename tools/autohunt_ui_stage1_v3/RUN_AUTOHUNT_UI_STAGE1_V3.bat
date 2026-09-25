@echo off
title AUTOHUNT UI STAGE1 V3
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0AUTOHUNT_UI_STAGE1_V3.ps1"
echo.
pause
