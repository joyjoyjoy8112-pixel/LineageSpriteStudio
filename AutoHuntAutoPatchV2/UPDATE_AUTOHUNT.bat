@echo off
chcp 65001 >nul
title AUTOHUNT AUTO PATCH V2
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0APPLY_AUTOHUNT_PATCH.ps1"
echo.
pause
