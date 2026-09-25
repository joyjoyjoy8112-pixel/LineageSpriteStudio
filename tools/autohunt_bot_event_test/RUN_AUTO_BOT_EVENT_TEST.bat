@echo off
title AUTO BOT EVENT TEST
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0AUTO_BOT_EVENT_TEST.ps1"
echo.
pause
