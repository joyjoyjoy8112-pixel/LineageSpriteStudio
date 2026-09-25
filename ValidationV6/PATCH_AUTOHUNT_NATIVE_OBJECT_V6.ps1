param(
    [string]$ClientRoot = "",
    [switch]$VerifyOnly,
    [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Info([string]$s) { Write-Host ("[INFO] " + $s) }
function Ok([string]$s)   { Write-Host ("[OK]   " + $s) -ForegroundColor Green }
function Warn([string]$s) { Write-Host ("[WARN] " + $s) -ForegroundColor Yellow }

function Find-ClientRoot([string]$StartDir) {
    $p = [IO.Path]::GetFullPath($StartDir)
    for ($i = 0; $i -lt 8; $i++) {
        if ((Test-Path -LiteralPath (Join-Path $p "UI.idx") -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $p "UI.pak") -PathType Leaf)) {
            return $p
        }
        $parent = [IO.Directory]::GetParent($p)
        if ($null -eq $parent) { break }
        $p = $parent.FullName
    }
    return $null
}

function Read-U32([byte[]]$Bytes, [int]$Offset) {
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Put-U32([byte[]]$Bytes, [int]$Offset, [uint32]$Value) {
    $b = [BitConverter]::GetBytes($Value)
    [Array]::Copy($b, 0, $Bytes, $Offset, 4)
}

function Test-BytesEqual([byte[]]$A, [byte[]]$B) {
    if ($null -eq $A -or $null -eq $B) { return $false }
    if ($A.Length -ne $B.Length) { return $false }
    for ($i = 0; $i -lt $A.Length; $i++) {
        if ($A[$i] -ne $B[$i]) { return $false }
    }
    return $true
}

function Assert-ExtIndex([byte[]]$Idx) {
    if ($Idx.Length -lt 8) { throw "UI.idx is too small." }
    if ($Idx[0] -ne 0x5F -or $Idx[1] -ne 0x45 -or $Idx[2] -ne 0x58 -or $Idx[3] -ne 0x54) {
        throw "UI.idx magic is not _EXT."
    }
    $count = Read-U32 $Idx 4
    if ((8L + ([int64]$count * 128L)) -gt $Idx.Length) {
        throw "UI.idx record table is truncated."
    }
}

function Find-Record([byte[]]$Idx, [string]$TargetName) {
    Assert-ExtIndex $Idx
    $count = Read-U32 $Idx 4
    $hit = $null
    $hitCount = 0

    for ($i = 0; $i -lt $count; $i++) {
        $rec = 8 + ($i * 128)
        $start = $rec + 16
        $end = $start
        while ($end -lt ($rec + 128) -and $Idx[$end] -ne 0) { $end++ }
        if ($end -le $start) { continue }
        $name = [Text.Encoding]::ASCII.GetString($Idx, $start, $end - $start)
        if ($name -ieq $TargetName) {
            $hitCount++
            $hit = [ordered]@{
                Index = $i
                RecordOffset = $rec
                Offset = (Read-U32 $Idx $rec)
                Size = (Read-U32 $Idx ($rec + 4))
            }
        }
    }

    if ($hitCount -ne 1) {
        throw ("Expected exactly one " + $TargetName + " record, found " + $hitCount + ".")
    }
    return $hit
}

[byte[]]$Key = @(
    0xDC,0x84,0x01,0x21,0x2A,0x40,0x20,0x0A,
    0xDD,0x25,0xB9,0xA7,0x0D,0xB9,0xC9,0x4E
)

[byte[]]$IV = @(
    0x3E,0x09,0x78,0xAA,0xC4,0xD5,0x30,0x63,
    0x30,0x0C,0x5F,0x9A,0x80,0x7F,0x22,0x46
)

function New-Aes() {
    $a = [Security.Cryptography.Aes]::Create()
    $a.Mode = [Security.Cryptography.CipherMode]::ECB
    $a.Padding = [Security.Cryptography.PaddingMode]::None
    $a.Key = $Key
    return $a
}

function Decrypt-Xml([byte[]]$Data) {
    if ($Data.Length -lt 4) { throw "Encrypted XML is too short." }
    [byte[]]$Out = New-Object byte[] $Data.Length
    [Array]::Copy($Data, 0, $Out, 0, $Data.Length)
    $Out[0] = 0x3C

    [byte[]]$Prev = New-Object byte[] 16
    [Array]::Copy($IV, 0, $Prev, 0, 16)
    $a = New-Aes
    $d = $a.CreateDecryptor()

    try {
        $p = 4
        while (($p + 16) -le $Data.Length) {
            [byte[]]$c = New-Object byte[] 16
            [Array]::Copy($Data, $p, $c, 0, 16)
            [byte[]]$t = New-Object byte[] 16
            [void]$d.TransformBlock($c, 0, 16, $t, 0)
            for ($j = 0; $j -lt 16; $j++) { $Out[$p + $j] = $t[$j] -bxor $Prev[$j] }
            [Array]::Copy($c, 0, $Prev, 0, 16)
            $p += 16
        }
        if ($p -lt $Data.Length) {
            for ($j = 0; $j -lt ($Data.Length - $p); $j++) {
                $Out[$p + $j] = $Data[$p + $j] -bxor $Prev[$j]
            }
        }
    }
    finally {
        $d.Dispose()
        $a.Dispose()
    }
    return $Out
}

function Encrypt-Xml([byte[]]$Plain) {
    if ($Plain.Length -lt 4) { throw "Plain XML is too short." }
    [byte[]]$Out = New-Object byte[] $Plain.Length
    [Array]::Copy($Plain, 0, $Out, 0, $Plain.Length)
    $Out[0] = 0x58

    [byte[]]$Prev = New-Object byte[] 16
    [Array]::Copy($IV, 0, $Prev, 0, 16)
    $a = New-Aes
    $e = $a.CreateEncryptor()

    try {
        $p = 4
        while (($p + 16) -le $Plain.Length) {
            [byte[]]$x = New-Object byte[] 16
            for ($j = 0; $j -lt 16; $j++) { $x[$j] = $Plain[$p + $j] -bxor $Prev[$j] }
            [byte[]]$c = New-Object byte[] 16
            [void]$e.TransformBlock($x, 0, 16, $c, 0)
            [Array]::Copy($c, 0, $Out, $p, 16)
            [Array]::Copy($c, 0, $Prev, 0, 16)
            $p += 16
        }
        if ($p -lt $Plain.Length) {
            for ($j = 0; $j -lt ($Plain.Length - $p); $j++) {
                $Out[$p + $j] = $Plain[$p + $j] -bxor $Prev[$j]
            }
        }
    }
    finally {
        $e.Dispose()
        $a.Dispose()
    }
    return $Out
}

function Read-Pak([string]$Path, [uint32]$Offset, [uint32]$Size) {
    $fi = Get-Item -LiteralPath $Path
    if (([int64]$Offset + [int64]$Size) -gt $fi.Length) { throw "UI.pak record is outside the file." }
    $fs = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        [void]$fs.Seek([int64]$Offset, [IO.SeekOrigin]::Begin)
        [byte[]]$buf = New-Object byte[] ([int]$Size)
        $got = $fs.Read($buf, 0, [int]$Size)
        if ($got -ne [int]$Size) { throw "Short UI.pak read." }
        return $buf
    }
    finally { $fs.Dispose() }
}

function Append-Pak([string]$Path, [byte[]]$Blob) {
    $fs = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        [void]$fs.Seek(0, [IO.SeekOrigin]::End)
        if ($fs.Position -gt [uint32]::MaxValue) { throw "UI.pak is larger than 4GB." }
        $off = [uint32]$fs.Position
        $fs.Write($Blob, 0, $Blob.Length)
        $fs.Flush()
        return $off
    }
    finally { $fs.Dispose() }
}

function Load-Xml([string]$Text) {
    $d = New-Object Xml.XmlDocument
    $d.PreserveWhitespace = $true
    $d.LoadXml($Text)
    return $d
}

function Convert-ToPledgeNoticeRoot([string]$Text) {
    $doc = Load-Xml $Text
    $oldRoot = $doc.DocumentElement.Name

    if ($oldRoot -eq "PledgeNoticeUI") { return $Text }
    if ($oldRoot -ne "Control") {
        throw ("Expected current AutoHuntUI.xml root Control, got " + $oldRoot + ".")
    }

    $open = $Text.IndexOf("<Control>", [StringComparison]::Ordinal)
    $close = $Text.LastIndexOf("</Control>", [StringComparison]::Ordinal)
    if ($open -lt 0 -or $close -le $open) {
        throw "Could not locate the plain Control root tags."
    }

    $Text = $Text.Substring(0, $open) + "<PledgeNoticeUI>" +
            $Text.Substring($open + 9, $close - ($open + 9)) +
            "</PledgeNoticeUI>" + $Text.Substring($close + 10)

    $check = Load-Xml $Text
    if ($check.DocumentElement.Name -ne "PledgeNoticeUI") {
        throw "Root conversion verification failed."
    }

    $w = $check.SelectSingleNode("/PledgeNoticeUI/Window[@Name='AutoHuntSettingsWindow']")
    if ($null -eq $w) { throw "AutoHuntSettingsWindow is missing after root conversion." }
    if ($w.GetAttribute("Activate") -ne "1") { throw "AutoHuntSettingsWindow Activate is not 1." }
    if ($w.GetAttribute("Visible") -ne "1") { throw "AutoHuntSettingsWindow Visible is not 1." }

    return $Text
}

function Invoke-SelfTest() {
    $sample = '<Control><Window Name="AutoHuntSettingsWindow" Activate="1" Visible="1"/></Control>'
    $converted = Convert-ToPledgeNoticeRoot $sample
    $doc = Load-Xml $converted
    if ($doc.DocumentElement.Name -ne "PledgeNoticeUI") { throw "Self-test root conversion failed." }

    $utf8 = New-Object Text.UTF8Encoding($false)
    [byte[]]$plain = $utf8.GetBytes($converted)
    [byte[]]$cipher = Encrypt-Xml $plain
    [byte[]]$round = Decrypt-Xml $cipher
    if (-not (Test-BytesEqual $plain $round)) { throw "Self-test crypto round-trip failed." }

    Ok "Runtime self-test passed."
}

if ($SelfTest) {
    Invoke-SelfTest
    Write-Host "SELF TEST COMPLETE - NO CLIENT FILES MODIFIED." -ForegroundColor Green
    exit 0
}

$running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "mjlin*" })
if ($running.Count -gt 0) { throw "Close all mjlin game clients first." }

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ClientRoot)) { $ClientRoot = Find-ClientRoot $scriptDir }
else { $ClientRoot = Find-ClientRoot $ClientRoot }
if ($null -eq $ClientRoot) { throw "Could not locate UI.idx/UI.pak." }

$idxPath = Join-Path $ClientRoot "UI.idx"
$pakPath = Join-Path $ClientRoot "UI.pak"
$utf8 = New-Object Text.UTF8Encoding($false)

[byte[]]$idx = [IO.File]::ReadAllBytes($idxPath)
$rec = Find-Record $idx "AutoHuntUI.xml"
$blob = Read-Pak $pakPath ([uint32]$rec.Offset) ([uint32]$rec.Size)
$text = $utf8.GetString((Decrypt-Xml $blob))
$doc = Load-Xml $text

if ($doc.DocumentElement.Name -notin @("Control","PledgeNoticeUI")) {
    throw ("AutoHuntUI.xml has an unexpected root: " + $doc.DocumentElement.Name)
}

if ($null -eq $doc.SelectSingleNode("/*/Window[@Name='AutoHuntSettingsWindow']")) {
    throw "AutoHuntSettingsWindow was not found in AutoHuntUI.xml."
}

$newText = Convert-ToPledgeNoticeRoot $text
[byte[]]$newPlain = $utf8.GetBytes($newText)
[byte[]]$newBlob = Encrypt-Xml $newPlain
if (-not (Test-BytesEqual $newPlain (Decrypt-Xml $newBlob))) {
    throw "Preflight crypto round-trip failed."
}

$checkDoc = Load-Xml ($utf8.GetString((Decrypt-Xml $newBlob)))
if ($checkDoc.DocumentElement.Name -ne "PledgeNoticeUI") { throw "Preflight root verification failed." }
if ($null -eq $checkDoc.SelectSingleNode("/PledgeNoticeUI/Window[@Name='AutoHuntSettingsWindow']")) {
    throw "Preflight window verification failed."
}

Ok "Preflight passed."
Info ("Current root: " + $doc.DocumentElement.Name)
Info "Test root: PledgeNoticeUI"
Info "AutoHuntUI.xml IDX record is reused; no new index record is created."

if ($VerifyOnly) {
    Write-Host "VERIFY ONLY COMPLETE - NO CLIENT FILES MODIFIED." -ForegroundColor Green
    exit 0
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backup = Join-Path $ClientRoot ("AUTOHUNT_NATIVE_OBJECT_BACKUP_V6_" + $stamp)
New-Item -ItemType Directory -Path $backup -Force | Out-Null
Copy-Item -LiteralPath $idxPath -Destination (Join-Path $backup "UI.idx") -Force
Copy-Item -LiteralPath $pakPath -Destination (Join-Path $backup "UI.pak") -Force

$restore = @"
@echo off
cd /d "%~dp0"
copy /y "UI.idx" "..\UI.idx" >nul
copy /y "UI.pak" "..\UI.pak" >nul
echo Restore complete.
pause
"@
[IO.File]::WriteAllText((Join-Path $backup "RESTORE.cmd"), $restore, (New-Object Text.ASCIIEncoding))
Ok ("Backup created: " + $backup)

$writeStarted = $false
try {
    $writeStarted = $true
    $newOffset = Append-Pak $pakPath $newBlob
    Put-U32 $idx ([int]$rec.RecordOffset) ([uint32]$newOffset)
    Put-U32 $idx ([int]$rec.RecordOffset + 4) ([uint32]$newBlob.Length)
    [IO.File]::WriteAllBytes($idxPath, $idx)

    [byte[]]$verifyIdx = [IO.File]::ReadAllBytes($idxPath)
    $verifyRec = Find-Record $verifyIdx "AutoHuntUI.xml"
    [byte[]]$verifyBlob = Read-Pak $pakPath ([uint32]$verifyRec.Offset) ([uint32]$verifyRec.Size)
    $verifyText = $utf8.GetString((Decrypt-Xml $verifyBlob))
    $verifyDoc = Load-Xml $verifyText

    if ($verifyDoc.DocumentElement.Name -ne "PledgeNoticeUI") { throw "Final root verification failed." }
    $verifyWindow = $verifyDoc.SelectSingleNode("/PledgeNoticeUI/Window[@Name='AutoHuntSettingsWindow']")
    if ($null -eq $verifyWindow) { throw "Final AutoHuntSettingsWindow verification failed." }
    if ($verifyWindow.GetAttribute("Activate") -ne "1" -or $verifyWindow.GetAttribute("Visible") -ne "1") {
        throw "Final window visibility verification failed."
    }

    Write-Host ""
    Write-Host "====================================================" -ForegroundColor Cyan
    Write-Host "V6 NATIVE OBJECT LOAD TEST COMPLETE" -ForegroundColor Cyan
    Write-Host "File: AutoHuntUI.xml"
    Write-Host "XML root: PledgeNoticeUI"
    Write-Host "Window: AutoHuntSettingsWindow"
    Write-Host ("Backup: " + $backup)
    Write-Host "====================================================" -ForegroundColor Cyan
}
catch {
    if ($writeStarted) {
        Warn "Post-write verification failed. Restoring UI.idx/UI.pak automatically."
        Copy-Item -LiteralPath (Join-Path $backup "UI.idx") -Destination $idxPath -Force
        Copy-Item -LiteralPath (Join-Path $backup "UI.pak") -Destination $pakPath -Force
        Warn "Automatic restore completed."
    }
    throw
}
