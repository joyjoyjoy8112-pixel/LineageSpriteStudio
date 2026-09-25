@echo off
title AUTO DIAG20 V2
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0AUTO_DIAG20_PATCH_V2.ps1"
echo.
pause
