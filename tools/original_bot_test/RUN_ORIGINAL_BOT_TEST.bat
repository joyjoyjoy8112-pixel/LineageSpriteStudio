@echo off
title ORIGINAL BOT WINDOW TEST
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ORIGINAL_BOT_WINDOW_TEST.ps1"
echo.
pause
