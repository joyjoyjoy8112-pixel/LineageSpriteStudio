@echo off
chcp 65001 >nul
title AUTOHUNT NEW WINDOW V4
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALL_AUTOHUNT_NEW_WINDOW_V4.ps1"
echo.
pause
