param()

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $Root "config.json"
$PatchScript = Join-Path $Root "tools\AUTOHUNT_INGAME_WINDOW_V2.ps1"

if (-not (Test-Path -LiteralPath $ConfigPath)) { throw "config.json not found." }
if (-not (Test-Path -LiteralPath $PatchScript)) { throw "Client patch script not found." }

$cfg = ([IO.File]::ReadAllText($ConfigPath,[Text.Encoding]::UTF8) | ConvertFrom-Json)
$client = [string]$cfg.clientPath
$server = [string]$cfg.serverPath

Write-Host "[AUTO] Server: $server" -ForegroundColor Cyan
Write-Host "[AUTO] Client: $client" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $client)) { throw "Client path not found: $client" }
if (-not (Test-Path -LiteralPath (Join-Path $client "UI.idx"))) { throw "UI.idx not found in client path." }
if (-not (Test-Path -LiteralPath (Join-Path $client "UI.pak"))) { throw "UI.pak not found in client path." }

if (Get-Process -Name "mjlin" -ErrorAction SilentlyContinue) {
    throw "Close the Lineage client before updating."
}

Write-Host "[AUTO] Applying client UI patch..." -ForegroundColor Yellow
& $PatchScript -ClientDir $client

if ($LASTEXITCODE -ne 0) { throw "Client patch failed." }

Write-Host "[AUTO] Client patch complete." -ForegroundColor Green
Write-Host "[AUTO] No server files changed in this test build." -ForegroundColor DarkGray
