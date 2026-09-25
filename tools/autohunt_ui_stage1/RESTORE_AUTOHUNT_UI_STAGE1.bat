@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
set "latest="
for /f "delims=" %%D in ('dir /b /ad /o-d "AUTOHUNT_STAGE1_BACKUP_*" 2^>nul') do (
  if not defined latest set "latest=%%D"
)
if not defined latest (
  echo No AUTOHUNT_STAGE1_BACKUP folder found.
  pause
  exit /b 1
)
echo Restoring from !latest!
copy /y "!latest!\UI.idx" ".\UI.idx" >nul
copy /y "!latest!\UI.pak" ".\UI.pak" >nul
echo Restore complete.
pause
