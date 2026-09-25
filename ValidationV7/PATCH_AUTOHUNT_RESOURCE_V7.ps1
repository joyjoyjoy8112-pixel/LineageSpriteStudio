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
        $ok = $true
        foreach ($n in @("UI.idx","UI.pak","Data.idx","Data.pak")) {
            if (-not (Test-Path -LiteralPath (Join-Path $p $n) -PathType Leaf)) {
                $ok = $false
                break
            }
        }
        if ($ok) { return $p }
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

function Assert-ExtIndex([byte[]]$Idx, [string]$Label) {
    if ($Idx.Length -lt 8) { throw ($Label + " is too small.") }
    if ($Idx[0] -ne 0x5F -or $Idx[1] -ne 0x45 -or $Idx[2] -ne 0x58 -or $Idx[3] -ne 0x54) {
        throw ($Label + " magic is not _EXT.")
    }
    $count = Read-U32 $Idx 4
    if ((8L + ([int64]$count * 128L)) -gt $Idx.Length) {
        throw ($Label + " record table is truncated.")
    }
}

function Find-Record([byte[]]$Idx, [string]$TargetName, [string]$Label) {
    Assert-ExtIndex $Idx $Label
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
        throw ($Label + ": expected exactly one " + $TargetName + " record, found " + $hitCount + ".")
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

            for ($j = 0; $j -lt 16; $j++) {
                $Out[$p + $j] = $t[$j] -bxor $Prev[$j]
            }

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
            for ($j = 0; $j -lt 16; $j++) {
                $x[$j] = $Plain[$p + $j] -bxor $Prev[$j]
            }

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
    if (([int64]$Offset + [int64]$Size) -gt $fi.Length) {
        throw ("PAK range is outside file: " + $Path)
    }

    $fs = [IO.File]::Open(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite
    )

    try {
        [void]$fs.Seek([int64]$Offset, [IO.SeekOrigin]::Begin)

        [byte[]]$buf = New-Object byte[] ([int]$Size)
        $got = $fs.Read($buf, 0, [int]$Size)

        if ($got -ne [int]$Size) { throw "Short PAK read." }
        return $buf
    }
    finally {
        $fs.Dispose()
    }
}

function Append-Pak([string]$Path, [byte[]]$Blob) {
    $fs = [IO.File]::Open(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None
    )

    try {
        [void]$fs.Seek(0, [IO.SeekOrigin]::End)
        if ($fs.Position -gt [uint32]::MaxValue) { throw "PAK is larger than 4GB." }

        $off = [uint32]$fs.Position
        $fs.Write($Blob, 0, $Blob.Length)
        $fs.Flush()
        return $off
    }
    finally {
        $fs.Dispose()
    }
}

function Load-Xml([string]$Text) {
    $d = New-Object Xml.XmlDocument
    $d.PreserveWhitespace = $true
    $d.LoadXml($Text)
    return $d
}

function Get-IdSetFromListXml([Xml.XmlDocument]$Doc, [string]$ElementName) {
    $set = New-Object 'System.Collections.Generic.HashSet[string]'

    $nodes = $Doc.SelectNodes("//" + $ElementName)
    foreach ($n in $nodes) {
        $id = $n.GetAttribute("id")
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            [void]$set.Add($id)
        }
    }

    return $set
}

function Get-ResourceId([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    $semi = $Value.IndexOf(";")
    if ($semi -ge 0) { return $Value.Substring(0, $semi) }
    return $Value
}

function Test-UiResources(
    [Xml.XmlDocument]$UiDoc,
    [System.Collections.Generic.HashSet[string]]$PngIds,
    [System.Collections.Generic.HashSet[string]]$SurfIds
) {
    $missing = New-Object System.Collections.ArrayList

    foreach ($node in $UiDoc.SelectNodes("//*")) {
        if ($null -eq $node.Attributes) { continue }

        foreach ($attr in $node.Attributes) {
            $name = $attr.Name
            $value = $attr.Value
            $id = Get-ResourceId $value

            if ([string]::IsNullOrWhiteSpace($id)) { continue }

            if ($name -like "*Png") {
                if (-not $PngIds.Contains($id)) {
                    [void]$missing.Add(("PNG " + $id + " at " + $node.Name + "/" + $node.GetAttribute("Name") + " attr=" + $name))
                }
            }
            elseif ($name -like "*Surf") {
                if (-not $SurfIds.Contains($id)) {
                    [void]$missing.Add(("SURF " + $id + " at " + $node.Name + "/" + $node.GetAttribute("Name") + " attr=" + $name))
                }
            }
        }
    }

    return ,$missing
}

function Prepare-AutoHuntXml([string]$Text) {
    $doc = Load-Xml $Text

    if ($doc.DocumentElement.Name -eq "Control") {
        $newRoot = $doc.CreateElement("PledgeNoticeUI")

        while ($doc.DocumentElement.HasChildNodes) {
            [void]$newRoot.AppendChild($doc.DocumentElement.FirstChild)
        }

        [void]$doc.ReplaceChild($newRoot, $doc.DocumentElement)
    }
    elseif ($doc.DocumentElement.Name -ne "PledgeNoticeUI") {
        throw ("Unexpected AutoHuntUI.xml root: " + $doc.DocumentElement.Name)
    }

    $backgrounds = @($doc.SelectNodes("//*[@Name='AH_Background']"))
    if ($backgrounds.Count -gt 1) {
        throw ("Found duplicate AH_Background nodes: " + $backgrounds.Count)
    }

    foreach ($b in $backgrounds) {
        [void]$b.ParentNode.RemoveChild($b)
    }

    $bad910101 = @($doc.SelectNodes("//*[@BasePng='910101;1' or @HighlightPng='910101;1' or @PushPng='910101;1' or @Png='910101;1']"))
    if ($bad910101.Count -ne 0) {
        throw "PNG 910101 is still referenced after cleanup."
    }

    $w = $doc.SelectSingleNode("/PledgeNoticeUI/Window[@Name='AutoHuntSettingsWindow']")
    if ($null -eq $w) { throw "AutoHuntSettingsWindow is missing." }

    $w.SetAttribute("Activate", "1")
    $w.SetAttribute("Visible", "1")

    return $doc
}

function Invoke-SelfTest() {
    $sample = '<PledgeNoticeUI><Window Name="AutoHuntSettingsWindow" Activate="1" Visible="1"><Button Name="AH_Background" BasePng="910101;1"/><Button Name="Good" BasePng="3361;1" BaseSurf="9183;1"/></Window></PledgeNoticeUI>'

    $doc = Prepare-AutoHuntXml $sample

    if (@($doc.SelectNodes("//*[@Name='AH_Background']")).Count -ne 0) {
        throw "Self-test failed to remove AH_Background."
    }

    $png = New-Object 'System.Collections.Generic.HashSet[string]'
    [void]$png.Add("3361")

    $surf = New-Object 'System.Collections.Generic.HashSet[string]'
    [void]$surf.Add("9183")

    $missing = @(Test-UiResources $doc $png $surf)
    if ($missing.Count -ne 0) {
        throw ("Self-test resource validation failed: " + ($missing -join "; "))
    }

    $utf8 = New-Object Text.UTF8Encoding($false)
    [byte[]]$plain = $utf8.GetBytes($doc.OuterXml)
    [byte[]]$cipher = Encrypt-Xml $plain
    [byte[]]$round = Decrypt-Xml $cipher

    if (-not (Test-BytesEqual $plain $round)) {
        throw "Self-test crypto round-trip failed."
    }

    Ok "Runtime self-test passed."
}

if ($SelfTest) {
    Invoke-SelfTest
    Write-Host "SELF TEST COMPLETE - NO CLIENT FILES MODIFIED." -ForegroundColor Green
    exit 0
}

$running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -like "mjlin*"
})

if ($running.Count -gt 0) {
    throw "Close all mjlin game clients before running this tool."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($ClientRoot)) {
    $ClientRoot = Find-ClientRoot $scriptDir
}
else {
    $ClientRoot = Find-ClientRoot $ClientRoot
}

if ($null -eq $ClientRoot) {
    throw "Could not locate UI.idx/UI.pak/Data.idx/Data.pak."
}

Info ("Client root: " + $ClientRoot)

$uiIdxPath = Join-Path $ClientRoot "UI.idx"
$uiPakPath = Join-Path $ClientRoot "UI.pak"
$dataIdxPath = Join-Path $ClientRoot "Data.idx"
$dataPakPath = Join-Path $ClientRoot "Data.pak"

$utf8 = New-Object Text.UTF8Encoding($false)

[byte[]]$uiIdx = [IO.File]::ReadAllBytes($uiIdxPath)
[byte[]]$dataIdx = [IO.File]::ReadAllBytes($dataIdxPath)

$autoRec = Find-Record $uiIdx "AutoHuntUI.xml" "UI.idx"
$pngRec = Find-Record $dataIdx "PngList.xml" "Data.idx"
$surfRec = Find-Record $dataIdx "SurfList.xml" "Data.idx"

$autoText = $utf8.GetString(
    (Decrypt-Xml (Read-Pak $uiPakPath ([uint32]$autoRec.Offset) ([uint32]$autoRec.Size)))
)

$pngDoc = Load-Xml (
    $utf8.GetString(
        (Decrypt-Xml (Read-Pak $dataPakPath ([uint32]$pngRec.Offset) ([uint32]$pngRec.Size)))
    )
)

$surfDoc = Load-Xml (
    $utf8.GetString(
        (Decrypt-Xml (Read-Pak $dataPakPath ([uint32]$surfRec.Offset) ([uint32]$surfRec.Size)))
    )
)

if ($pngDoc.DocumentElement.Name -ne "pngs") { throw "PngList.xml root is not pngs." }
if ($surfDoc.DocumentElement.Name -ne "surfs") { throw "SurfList.xml root is not surfs." }

$pngIds = Get-IdSetFromListXml $pngDoc "png"
$surfIds = Get-IdSetFromListXml $surfDoc "surf"

Info ("PngList ids loaded: " + $pngIds.Count)
Info ("SurfList ids loaded: " + $surfIds.Count)

$beforeDoc = Load-Xml $autoText
$beforeMissing = @(Test-UiResources $beforeDoc $pngIds $surfIds)

Info ("Resource errors before cleanup: " + $beforeMissing.Count)
foreach ($m in $beforeMissing) { Warn $m }

$preparedDoc = Prepare-AutoHuntXml $autoText
$afterMissing = @(Test-UiResources $preparedDoc $pngIds $surfIds)

if ($afterMissing.Count -ne 0) {
    foreach ($m in $afterMissing) { Warn $m }
    throw ("Resource validation failed after cleanup. Missing count=" + $afterMissing.Count)
}

$expectedPng = @("3361","3362","11527","11528")
foreach ($id in $expectedPng) {
    if (-not $pngIds.Contains($id)) { throw ("Expected PNG id is missing from PngList.xml: " + $id) }
}

if (-not $surfIds.Contains("9183")) {
    throw "Expected SURF id 9183 is missing from SurfList.xml."
}

$newText = $preparedDoc.OuterXml
[byte[]]$newPlain = $utf8.GetBytes($newText)
[byte[]]$newBlob = Encrypt-Xml $newPlain

[byte[]]$round = Decrypt-Xml $newBlob
if (-not (Test-BytesEqual $newPlain $round)) {
    throw "Preflight crypto round-trip failed."
}

$roundDoc = Load-Xml ($utf8.GetString($round))
$roundMissing = @(Test-UiResources $roundDoc $pngIds $surfIds)

if ($roundMissing.Count -ne 0) {
    throw ("Round-trip resource validation failed: " + ($roundMissing -join "; "))
}

if ($roundDoc.DocumentElement.Name -ne "PledgeNoticeUI") {
    throw "Prepared root is not PledgeNoticeUI."
}

if ($null -eq $roundDoc.SelectSingleNode("/PledgeNoticeUI/Window[@Name='AutoHuntSettingsWindow']")) {
    throw "Prepared AutoHuntSettingsWindow is missing."
}

Ok "Preflight passed."
Info "PNG 910101 reference removed."
Info "Every remaining *Png id exists in the live Data.pak PngList.xml."
Info "Every remaining *Surf id exists in the live Data.pak SurfList.xml."

if ($VerifyOnly) {
    Write-Host "VERIFY ONLY COMPLETE - NO CLIENT FILES MODIFIED." -ForegroundColor Green
    exit 0
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backup = Join-Path $ClientRoot ("AUTOHUNT_RESOURCE_BACKUP_V7_" + $stamp)
New-Item -ItemType Directory -Path $backup -Force | Out-Null

Copy-Item -LiteralPath $uiIdxPath -Destination (Join-Path $backup "UI.idx") -Force
Copy-Item -LiteralPath $uiPakPath -Destination (Join-Path $backup "UI.pak") -Force

$restore = @"
@echo off
cd /d "%~dp0"
copy /y "UI.idx" "..\UI.idx" >nul
copy /y "UI.pak" "..\UI.pak" >nul
echo Restore complete.
pause
"@

[IO.File]::WriteAllText(
    (Join-Path $backup "RESTORE.cmd"),
    $restore,
    (New-Object Text.ASCIIEncoding)
)

Ok ("Backup created: " + $backup)

$writeStarted = $false

try {
    $writeStarted = $true

    $newOffset = Append-Pak $uiPakPath $newBlob

    Put-U32 $uiIdx ([int]$autoRec.RecordOffset) ([uint32]$newOffset)
    Put-U32 $uiIdx ([int]$autoRec.RecordOffset + 4) ([uint32]$newBlob.Length)

    [IO.File]::WriteAllBytes($uiIdxPath, $uiIdx)

    [byte[]]$verifyIdx = [IO.File]::ReadAllBytes($uiIdxPath)
    $verifyRec = Find-Record $verifyIdx "AutoHuntUI.xml" "UI.idx final"

    $verifyText = $utf8.GetString(
        (Decrypt-Xml (Read-Pak $uiPakPath ([uint32]$verifyRec.Offset) ([uint32]$verifyRec.Size)))
    )

    $verifyDoc = Load-Xml $verifyText

    if ($verifyDoc.DocumentElement.Name -ne "PledgeNoticeUI") {
        throw "Final root verification failed."
    }

    if (@($verifyDoc.SelectNodes("//*[@Name='AH_Background']")).Count -ne 0) {
        throw "Final AH_Background removal verification failed."
    }

    $verifyMissing = @(Test-UiResources $verifyDoc $pngIds $surfIds)

    if ($verifyMissing.Count -ne 0) {
        throw ("Final resource verification failed: " + ($verifyMissing -join "; "))
    }

    if ($null -eq $verifyDoc.SelectSingleNode("/PledgeNoticeUI/Window[@Name='AutoHuntSettingsWindow']")) {
        throw "Final AutoHuntSettingsWindow verification failed."
    }

    Write-Host ""
    Write-Host "====================================================" -ForegroundColor Cyan
    Write-Host "V7 RESOURCE VALIDATION PATCH COMPLETE" -ForegroundColor Cyan
    Write-Host "Removed invalid PNG id: 910101"
    Write-Host "Remaining PNG/SURF references: VALID"
    Write-Host ("Backup: " + $backup)
    Write-Host "====================================================" -ForegroundColor Cyan
}
catch {
    if ($writeStarted) {
        Warn "Post-write verification failed. Restoring UI.idx/UI.pak automatically."
        Copy-Item -LiteralPath (Join-Path $backup "UI.idx") -Destination $uiIdxPath -Force
        Copy-Item -LiteralPath (Join-Path $backup "UI.pak") -Destination $uiPakPath -Force
        Warn "Automatic restore completed."
    }

    throw
}
