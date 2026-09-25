param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$PackageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $PackageRoot "config.json"
$PayloadRoot = Join-Path $PackageRoot "payload"
$ServerPayload = Join-Path $PayloadRoot "server"
$ClientPayload = Join-Path $PayloadRoot "client"
$LogRoot = Join-Path $PackageRoot "logs"
New-Item -ItemType Directory -Force -Path $LogRoot | Out-Null

function Log([string]$text, [ConsoleColor]$color = [ConsoleColor]::Gray) {
    $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $text
    Write-Host $line -ForegroundColor $color
    Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8
}

if (-not (Test-Path -LiteralPath $ConfigPath)) {
    throw "config.json not found."
}

$cfgText = [IO.File]::ReadAllText($ConfigPath, [Text.Encoding]::UTF8)
$cfg = $cfgText | ConvertFrom-Json
$ServerDir = [string]$cfg.serverPath
$ClientDir = [string]$cfg.clientPath
$PatchVersion = [string]$cfg.patchVersion
$PatchName = [string]$cfg.patchName

$script:LogFile = Join-Path $LogRoot ("update_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".log")

Log "$PatchName updater started." Cyan
Log "Version: $PatchVersion" Cyan
Log "Server: $ServerDir"
Log "Client: $ClientDir"

if (-not (Test-Path -LiteralPath $ServerDir)) {
    throw "Server path not found: $ServerDir"
}
if (-not (Test-Path -LiteralPath $ClientDir)) {
    throw "Client path not found: $ClientDir"
}

# Prevent client UI files from being updated while the game is running.
$mj = Get-Process -Name "mjlin" -ErrorAction SilentlyContinue
if ($mj -and -not $Force) {
    throw "mjlin is running. Close the game/launcher and run the updater again."
}

# Warn if a Java process appears to be launched from the configured server directory.
try {
    $serverEsc = [Regex]::Escape($ServerDir)
    $serverJava = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { ($_.Name -match "^(java|javaw)\.exe$") -and $_.CommandLine -and ($_.CommandLine -match $serverEsc) }
    if ($serverJava -and -not $Force) {
        throw "A Java server process is using the configured server folder. Stop the server and run again."
    }
} catch {
    if ($_.Exception.Message -like "A Java server process*") { throw }
}

function Get-PayloadFiles([string]$payloadDir) {
    if (-not (Test-Path -LiteralPath $payloadDir)) { return @() }
    return @(Get-ChildItem -LiteralPath $payloadDir -File -Recurse -Force |
        Where-Object { $_.Name -notin @(".keep", ".gitkeep", ".placeholder") })
}

function New-Backup([string]$targetRoot, [string]$kind, [array]$files, [string]$stamp) {
    $backupRoot = Join-Path $targetRoot ("_AUTOHUNT_PATCH_BACKUP\" + $stamp)
    $dataRoot = Join-Path $backupRoot "data"
    New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null

    $manifest = New-Object System.Collections.ArrayList

    foreach ($file in $files) {
        $payloadBase = if ($kind -eq "server") { $ServerPayload } else { $ClientPayload }
        $rel = $file.FullName.Substring($payloadBase.Length).TrimStart("\")
        $dest = Join-Path $targetRoot $rel
        $entry = [ordered]@{
            relativePath = $rel
            existed = [bool](Test-Path -LiteralPath $dest)
            hashBefore = $null
            hashAfter = $null
        }

        if ($entry.existed) {
            $backupFile = Join-Path $dataRoot $rel
            $backupParent = Split-Path -Parent $backupFile
            if ($backupParent) { New-Item -ItemType Directory -Force -Path $backupParent | Out-Null }
            Copy-Item -LiteralPath $dest -Destination $backupFile -Force
            $entry.hashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $dest).Hash
        }

        [void]$manifest.Add([PSCustomObject]$entry)
    }

    return [PSCustomObject]@{
        backupRoot = $backupRoot
        dataRoot = $dataRoot
        manifest = $manifest
    }
}

function Apply-Payload([string]$targetRoot, [string]$payloadDir, [array]$files, $backupInfo) {
    foreach ($file in $files) {
        $rel = $file.FullName.Substring($payloadDir.Length).TrimStart("\")
        $dest = Join-Path $targetRoot $rel
        $parent = Split-Path -Parent $dest
        if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }

        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force

        $srcHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash
        $dstHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $dest).Hash
        if ($srcHash -ne $dstHash) {
            throw "Hash verification failed: $dest"
        }

        $m = $backupInfo.manifest | Where-Object { $_.relativePath -eq $rel } | Select-Object -First 1
        if ($m) { $m.hashAfter = $dstHash }
        Log "Updated: $dest" Green
    }

    $manifestPath = Join-Path $backupInfo.backupRoot "manifest.json"
    $meta = [ordered]@{
        patchName = $PatchName
        patchVersion = $PatchVersion
        appliedAt = (Get-Date).ToString("o")
        targetRoot = $targetRoot
        files = $backupInfo.manifest
    }
    [IO.File]::WriteAllText($manifestPath, ($meta | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
}

$serverFiles = Get-PayloadFiles $ServerPayload
$clientFiles = Get-PayloadFiles $ClientPayload

if ($serverFiles.Count -eq 0 -and $clientFiles.Count -eq 0) {
    throw "No patch files were found in payload\server or payload\client."
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"

try {
    if ($serverFiles.Count -gt 0) {
        Log "Backing up server files..." Yellow
        $serverBackup = New-Backup $ServerDir "server" $serverFiles $stamp
        Apply-Payload $ServerDir $ServerPayload $serverFiles $serverBackup
    } else {
        Log "No server payload files in this patch." DarkGray
    }

    if ($clientFiles.Count -gt 0) {
        Log "Backing up client files..." Yellow
        $clientBackup = New-Backup $ClientDir "client" $clientFiles $stamp
        Apply-Payload $ClientDir $ClientPayload $clientFiles $clientBackup
    } else {
        Log "No client payload files in this patch." DarkGray
    }

    $versionInfo = [ordered]@{
        patchName = $PatchName
        patchVersion = $PatchVersion
        appliedAt = (Get-Date).ToString("o")
    } | ConvertTo-Json

    if ($serverFiles.Count -gt 0) {
        [IO.File]::WriteAllText((Join-Path $ServerDir ".autohunt_patch_version.json"), $versionInfo, (New-Object Text.UTF8Encoding($false)))
    }
    if ($clientFiles.Count -gt 0) {
        [IO.File]::WriteAllText((Join-Path $ClientDir ".autohunt_patch_version.json"), $versionInfo, (New-Object Text.UTF8Encoding($false)))
    }

    Log "PATCH COMPLETE" Green
    Log "Backup timestamp: $stamp" Green
} catch {
    Log ("ERROR: " + $_.Exception.Message) Red
    throw
}
