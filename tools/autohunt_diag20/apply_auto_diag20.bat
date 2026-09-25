@echo off
chcp 65001 >nul
title AUTO 1~20 29999 UI Number Patcher
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0AUTO_DIAG20_PATCH.ps1"
echo.
pause
