param()

$ErrorActionPreference = "Stop"
$PackageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ConfigPath = Join-Path $PackageRoot "config.json"

$cfg = ([IO.File]::ReadAllText($ConfigPath, [Text.Encoding]::UTF8) | ConvertFrom-Json)
$targets = @(
    [PSCustomObject]@{ Name="server"; Root=[string]$cfg.serverPath },
    [PSCustomObject]@{ Name="client"; Root=[string]$cfg.clientPath }
)

foreach ($t in $targets) {
    if (-not (Test-Path -LiteralPath $t.Root)) { continue }

    $backupBase = Join-Path $t.Root "_AUTOHUNT_PATCH_BACKUP"
    if (-not (Test-Path -LiteralPath $backupBase)) {
        Write-Host "[$($t.Name)] No backup directory." -ForegroundColor DarkGray
        continue
    }

    $latest = Get-ChildItem -LiteralPath $backupBase -Directory |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if (-not $latest) {
        Write-Host "[$($t.Name)] No backup found." -ForegroundColor DarkGray
        continue
    }

    $manifestPath = Join-Path $latest.FullName "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Missing manifest: $manifestPath"
    }

    $manifest = ([IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8) | ConvertFrom-Json)
    $dataRoot = Join-Path $latest.FullName "data"

    foreach ($f in $manifest.files) {
        $dest = Join-Path $t.Root ([string]$f.relativePath)
        if ([bool]$f.existed) {
            $src = Join-Path $dataRoot ([string]$f.relativePath)
            $parent = Split-Path -Parent $dest
            if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
            Copy-Item -LiteralPath $src -Destination $dest -Force
            Write-Host "Restored: $dest" -ForegroundColor Green
        } else {
            if (Test-Path -LiteralPath $dest) {
                Remove-Item -LiteralPath $dest -Force
                Write-Host "Removed new file: $dest" -ForegroundColor Yellow
            }
        }
    }

    Write-Host "[$($t.Name)] rollback complete: $($latest.Name)" -ForegroundColor Cyan
}
