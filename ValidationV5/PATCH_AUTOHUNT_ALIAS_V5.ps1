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
    for ($i=0; $i -lt 8; $i++) {
        $ok = $true
        foreach ($n in @("Data.idx","Data.pak","UI.idx","UI.pak")) {
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

function Read-U32([byte[]]$b,[int]$o) { return [BitConverter]::ToUInt32($b,$o) }
function Put-U32([byte[]]$b,[int]$o,[uint32]$v) {
    $x=[BitConverter]::GetBytes($v)
    [Array]::Copy($x,0,$b,$o,4)
}

function Test-BytesEqual([byte[]]$a,[byte[]]$b) {
    if ($null -eq $a -or $null -eq $b) { return $false }
    if ($a.Length -ne $b.Length) { return $false }
    for ($i=0; $i -lt $a.Length; $i++) {
        if ($a[$i] -ne $b[$i]) { return $false }
    }
    return $true
}

function Assert-ExtIndex([byte[]]$idx,[string]$label) {
    if ($idx.Length -lt 8) { throw ($label + " too small.") }
    if ($idx[0] -ne 0x5F -or $idx[1] -ne 0x45 -or $idx[2] -ne 0x58 -or $idx[3] -ne 0x54) {
        throw ($label + " magic is not _EXT.")
    }
    $count=Read-U32 $idx 4
    if ((8L + ([int64]$count * 128L)) -gt $idx.Length) {
        throw ($label + " record table is truncated.")
    }
}

function Get-RecordName([byte[]]$rec) {
    $end=16
    while ($end -lt 128 -and $rec[$end] -ne 0) { $end++ }
    if ($end -le 16) { return "" }
    return [Text.Encoding]::ASCII.GetString($rec,16,$end-16)
}

function Get-Records([byte[]]$idx) {
    Assert-ExtIndex $idx "IDX"
    $count=Read-U32 $idx 4
    $list=New-Object System.Collections.ArrayList
    for ($i=0; $i -lt $count; $i++) {
        [byte[]]$rec=New-Object byte[] 128
        [Array]::Copy($idx,8+($i*128),$rec,0,128)
        [void]$list.Add($rec)
    }
    return ,$list
}

function Find-Record([byte[]]$idx,[string]$target,[string]$label) {
    $records=Get-Records $idx
    $hits=New-Object System.Collections.ArrayList
    for ($i=0; $i -lt $records.Count; $i++) {
        [byte[]]$rec=$records[$i]
        $name=Get-RecordName $rec
        if ($name -ieq $target) {
            [void]$hits.Add([ordered]@{
                Index=$i
                Record=$rec
                Offset=(Read-U32 $rec 0)
                Size=(Read-U32 $rec 4)
            })
        }
    }
    if ($hits.Count -ne 1) {
        throw ($label + ": expected exactly one " + $target + " record, found " + $hits.Count + ".")
    }
    return $hits[0]
}

function Build-IndexWithAlias(
    [byte[]]$idx,
    [byte[]]$templateRec,
    [string]$aliasName,
    [uint32]$offset,
    [uint32]$size
) {
    $templateName=Get-RecordName $templateRec
    if ($templateName.Length -ne $aliasName.Length) {
        throw ("Template/alias length mismatch: " + $templateName + " vs " + $aliasName)
    }

    [byte[]]$aliasRec=New-Object byte[] 128
    [Array]::Copy($templateRec,0,$aliasRec,0,128)

    Put-U32 $aliasRec 0 $offset
    Put-U32 $aliasRec 4 $size

    $nameBytes=[Text.Encoding]::ASCII.GetBytes($aliasName)
    for ($i=16; $i -lt (16+$nameBytes.Length+1); $i++) { $aliasRec[$i]=0 }
    [Array]::Copy($nameBytes,0,$aliasRec,16,$nameBytes.Length)
    $aliasRec[16+$nameBytes.Length]=0

    $records=Get-Records $idx
    $kept=New-Object System.Collections.ArrayList

    foreach ($r in $records) {
        [byte[]]$rr=$r
        if ((Get-RecordName $rr) -ine $aliasName) {
            [void]$kept.Add($rr)
        }
    }

    [void]$kept.Add($aliasRec)

    $sorted=@($kept | Sort-Object { (Get-RecordName ([byte[]]$_)).ToLowerInvariant() })

    [byte[]]$out=New-Object byte[] (8 + ($sorted.Count * 128))
    $out[0]=0x5F; $out[1]=0x45; $out[2]=0x58; $out[3]=0x54
    Put-U32 $out 4 ([uint32]$sorted.Count)

    for ($i=0; $i -lt $sorted.Count; $i++) {
        [Array]::Copy([byte[]]$sorted[$i],0,$out,8+($i*128),128)
    }

    return $out
}

[byte[]]$Key=@(
0xDC,0x84,0x01,0x21,0x2A,0x40,0x20,0x0A,
0xDD,0x25,0xB9,0xA7,0x0D,0xB9,0xC9,0x4E
)
[byte[]]$IV=@(
0x3E,0x09,0x78,0xAA,0xC4,0xD5,0x30,0x63,
0x30,0x0C,0x5F,0x9A,0x80,0x7F,0x22,0x46
)

function New-Aes() {
    $a=[Security.Cryptography.Aes]::Create()
    $a.Mode=[Security.Cryptography.CipherMode]::ECB
    $a.Padding=[Security.Cryptography.PaddingMode]::None
    $a.Key=$Key
    return $a
}

function Decrypt-Xml([byte[]]$data) {
    if ($data.Length -lt 4) { throw "Encrypted XML too short." }
    [byte[]]$out=New-Object byte[] $data.Length
    [Array]::Copy($data,0,$out,0,$data.Length)
    $out[0]=0x3C
    [byte[]]$prev=New-Object byte[] 16
    [Array]::Copy($IV,0,$prev,0,16)
    $a=New-Aes
    $d=$a.CreateDecryptor()
    try {
        $p=4
        while (($p+16) -le $data.Length) {
            [byte[]]$c=New-Object byte[] 16
            [Array]::Copy($data,$p,$c,0,16)
            [byte[]]$t=New-Object byte[] 16
            [void]$d.TransformBlock($c,0,16,$t,0)
            for ($j=0; $j -lt 16; $j++) { $out[$p+$j]=$t[$j] -bxor $prev[$j] }
            [Array]::Copy($c,0,$prev,0,16)
            $p+=16
        }
        if ($p -lt $data.Length) {
            for ($j=0; $j -lt ($data.Length-$p); $j++) {
                $out[$p+$j]=$data[$p+$j] -bxor $prev[$j]
            }
        }
    } finally {
        $d.Dispose(); $a.Dispose()
    }
    return $out
}

function Encrypt-Xml([byte[]]$plain) {
    if ($plain.Length -lt 4) { throw "Plain XML too short." }
    [byte[]]$out=New-Object byte[] $plain.Length
    [Array]::Copy($plain,0,$out,0,$plain.Length)
    $out[0]=0x58
    [byte[]]$prev=New-Object byte[] 16
    [Array]::Copy($IV,0,$prev,0,16)
    $a=New-Aes
    $e=$a.CreateEncryptor()
    try {
        $p=4
        while (($p+16) -le $plain.Length) {
            [byte[]]$x=New-Object byte[] 16
            for ($j=0; $j -lt 16; $j++) { $x[$j]=$plain[$p+$j] -bxor $prev[$j] }
            [byte[]]$c=New-Object byte[] 16
            [void]$e.TransformBlock($x,0,16,$c,0)
            [Array]::Copy($c,0,$out,$p,16)
            [Array]::Copy($c,0,$prev,0,16)
            $p+=16
        }
        if ($p -lt $plain.Length) {
            for ($j=0; $j -lt ($plain.Length-$p); $j++) {
                $out[$p+$j]=$plain[$p+$j] -bxor $prev[$j]
            }
        }
    } finally {
        $e.Dispose(); $a.Dispose()
    }
    return $out
}

function Read-Pak([string]$path,[uint32]$off,[uint32]$size) {
    $fi=Get-Item -LiteralPath $path
    if (([int64]$off+[int64]$size) -gt $fi.Length) { throw "PAK range outside file." }
    $fs=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try {
        [void]$fs.Seek([int64]$off,[IO.SeekOrigin]::Begin)
        [byte[]]$buf=New-Object byte[] ([int]$size)
        $got=$fs.Read($buf,0,[int]$size)
        if ($got -ne [int]$size) { throw "Short PAK read." }
        return $buf
    } finally { $fs.Dispose() }
}

function Append-Pak([string]$path,[byte[]]$blob) {
    $fs=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        [void]$fs.Seek(0,[IO.SeekOrigin]::End)
        $off=[uint32]$fs.Position
        $fs.Write($blob,0,$blob.Length)
        $fs.Flush()
        return $off
    } finally { $fs.Dispose() }
}

function Load-Xml([string]$text) {
    $d=New-Object Xml.XmlDocument
    $d.PreserveWhitespace=$true
    $d.LoadXml($text)
    return $d
}

function Normalize-AutoText([string]$text) {
    $d=Load-Xml $text
    $root=$d.DocumentElement.Name
    if (@("Control","HighRankUI","AutoHuntSettingsUI") -notcontains $root) {
        throw ("Unsupported AutoHunt root: " + $root)
    }

    if ($root -ne "Control") {
        $open=$text.IndexOf("<"+$root,[StringComparison]::Ordinal)
        $oe=$text.IndexOf(">",$open)
        $closeToken="</"+$root+">"
        $cs=$text.LastIndexOf($closeToken,[StringComparison]::Ordinal)
        if ($open -lt 0 -or $oe -lt 0 -or $cs -le $oe) { throw "Root conversion failed." }
        $text=$text.Substring(0,$open)+"<Control>"+$text.Substring($oe+1,$cs-($oe+1))+"</Control>"+$text.Substring($cs+$closeToken.Length)
    }

    $d=Load-Xml $text
    if ($d.DocumentElement.Name -ne "Control") { throw "Normalized root is not Control." }

    $w=$d.SelectSingleNode("/Control/Window[@Name='AutoHuntSettingsWindow']")
    if ($null -eq $w) { throw "AutoHuntSettingsWindow not found." }

    $tagPattern='<Window\b(?=[^>]*\bName="AutoHuntSettingsWindow")[^>]*>'
    $m=[Text.RegularExpressions.Regex]::Match($text,$tagPattern,[Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $m.Success) { throw "Window tag text not found." }
    $tag=$m.Value
    foreach ($pair in @(@("Activate","1"),@("Visible","1"))) {
        $attr=$pair[0]; $val=$pair[1]
        $ap='\b'+[regex]::Escape($attr)+'\s*=\s*"[^"]*"'
        if ([Text.RegularExpressions.Regex]::IsMatch($tag,$ap,[Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            $tag=[Text.RegularExpressions.Regex]::Replace($tag,$ap,($attr+'="'+$val+'"'),[Text.RegularExpressions.RegexOptions]::IgnoreCase)
        } else {
            $tag=$tag.Substring(0,$tag.Length-1)+" "+$attr+'="'+$val+'">'
        }
    }
    $text=$text.Substring(0,$m.Index)+$tag+$text.Substring($m.Index+$m.Length)

    $check=Load-Xml $text
    $ww=$check.SelectSingleNode("/Control/Window[@Name='AutoHuntSettingsWindow']")
    if ($null -eq $ww -or $ww.GetAttribute("Activate") -ne "1" -or $ww.GetAttribute("Visible") -ne "1") {
        throw "Window visibility verification failed."
    }
    return $text
}

function Prepare-List([string]$text,[string]$aliasName) {
    $d=Load-Xml $text
    if ($d.DocumentElement.Name -ne "uilist") { throw "XmlUiList root is not uilist." }

    foreach ($name in @("AutoHuntSettingsUI.xml","AutoHuntUI.xml")) {
        $nodes=@($d.SelectNodes("/uilist/file[@name='"+$name+"']"))
        foreach ($n in $nodes) { [void]$n.ParentNode.RemoveChild($n) }
    }

    $main=$d.SelectSingleNode("/uilist/file[@name='MainButtonUI.xml']")
    if ($null -eq $main) { throw "MainButtonUI.xml entry not found." }

    $new=$d.CreateElement("file")
    $new.SetAttribute("name",$aliasName)
    $new.SetAttribute("load","true")
    [void]$main.ParentNode.InsertAfter($new,$main)

    $settings=New-Object Xml.XmlWriterSettings
    $settings.Encoding=New-Object Text.UTF8Encoding($false)
    $settings.Indent=$true
    $settings.IndentChars="	"
    $settings.NewLineChars="
"
    $settings.NewLineHandling=[Xml.NewLineHandling]::Replace
    $settings.OmitXmlDeclaration=$false

    $sb=New-Object Text.StringBuilder
    $sw=New-Object IO.StringWriter($sb,[Globalization.CultureInfo]::InvariantCulture)
    $xw=[Xml.XmlWriter]::Create($sw,$settings)
    try { $d.Save($xw) } finally { $xw.Dispose(); $sw.Dispose() }
    $out=$sb.ToString()

    $out=[regex]::Replace($out,'<\?xml[^?]*\?>','<?xml version="1.0"?>',1)

    $check=Load-Xml $out
    $hits=@($check.SelectNodes("/uilist/file[@name='"+$aliasName+"' and @load='true']"))
    if ($hits.Count -ne 1) { throw "Alias startup-list verification failed." }
    return $out
}

function Invoke-SelfTest() {
    if ("AdenTelSub.xml".Length -ne "AutoHuntUI.xml".Length) {
        throw "Alias/template filename lengths are not equal."
    }

    [byte[]]$r=New-Object byte[] 128
    $n=[Text.Encoding]::ASCII.GetBytes("AdenTelSub.xml")
    [Array]::Copy($n,0,$r,16,$n.Length)
    $r[16+$n.Length]=0
    Put-U32 $r 0 1234
    Put-U32 $r 4 5678

    [byte[]]$idx=New-Object byte[] 136
    $idx[0]=0x5F; $idx[1]=0x45; $idx[2]=0x58; $idx[3]=0x54
    Put-U32 $idx 4 1
    [Array]::Copy($r,0,$idx,8,128)

    $out=Build-IndexWithAlias $idx $r "AutoHuntUI.xml" 111 222
    $a=Find-Record $out "AutoHuntUI.xml" "selftest"
    if ($a.Offset -ne 111 -or $a.Size -ne 222) { throw "Alias record self-test failed." }

    $sample='<Control><Window Name="AutoHuntSettingsWindow" Activate="1" Visible="1"/></Control>'
    [byte[]]$pb=(New-Object Text.UTF8Encoding($false)).GetBytes($sample)
    [byte[]]$cb=Encrypt-Xml $pb
    if (-not (Test-BytesEqual $pb (Decrypt-Xml $cb))) { throw "Crypto self-test failed." }

    Ok "Runtime self-test passed."
}

if ($SelfTest) {
    Invoke-SelfTest
    Write-Host "SELF TEST COMPLETE - NO CLIENT FILES MODIFIED." -ForegroundColor Green
    exit 0
}

$running=@(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "mjlin*" })
if ($running.Count -gt 0) { throw "Close all mjlin game clients first." }

$scriptDir=Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ClientRoot)) { $ClientRoot=Find-ClientRoot $scriptDir }
else { $ClientRoot=Find-ClientRoot $ClientRoot }
if ($null -eq $ClientRoot) { throw "Client root not found." }

Info ("Client root: "+$ClientRoot)

$uiIdxPath=Join-Path $ClientRoot "UI.idx"
$uiPakPath=Join-Path $ClientRoot "UI.pak"
$dataIdxPath=Join-Path $ClientRoot "Data.idx"
$dataPakPath=Join-Path $ClientRoot "Data.pak"

[byte[]]$uiIdx=[IO.File]::ReadAllBytes($uiIdxPath)
[byte[]]$dataIdx=[IO.File]::ReadAllBytes($dataIdxPath)

$src=Find-Record $uiIdx "AutoHuntSettingsUI.xml" "UI.idx"
$template=Find-Record $uiIdx "AdenTelSub.xml" "UI.idx"
$listRec=Find-Record $dataIdx "XmlUiList.xml" "Data.idx"

if ((Get-RecordName ([byte[]]$template.Record)).Length -ne "AutoHuntUI.xml".Length) {
    throw "AdenTelSub.xml template length no longer matches alias length."
}

$utf8=New-Object Text.UTF8Encoding($false)
$srcBlob=Read-Pak $uiPakPath ([uint32]$src.Offset) ([uint32]$src.Size)
$srcText=$utf8.GetString((Decrypt-Xml $srcBlob))
$normalizedText=Normalize-AutoText $srcText
[byte[]]$normalizedPlain=$utf8.GetBytes($normalizedText)
[byte[]]$normalizedBlob=Encrypt-Xml $normalizedPlain
if (-not (Test-BytesEqual $normalizedPlain (Decrypt-Xml $normalizedBlob))) {
    throw "AutoHunt round-trip failed."
}

$listBlob=Read-Pak $dataPakPath ([uint32]$listRec.Offset) ([uint32]$listRec.Size)
$listText=$utf8.GetString((Decrypt-Xml $listBlob))
$preparedList=Prepare-List $listText "AutoHuntUI.xml"
[byte[]]$listPlain=$utf8.GetBytes($preparedList)
[byte[]]$newListBlob=Encrypt-Xml $listPlain
if (-not (Test-BytesEqual $listPlain (Decrypt-Xml $newListBlob))) {
    throw "XmlUiList round-trip failed."
}

$simIdx=Build-IndexWithAlias $uiIdx ([byte[]]$template.Record) "AutoHuntUI.xml" ([uint32]$src.Offset) ([uint32]$normalizedBlob.Length)
$simAlias=Find-Record $simIdx "AutoHuntUI.xml" "simulated UI.idx"
if ((Get-RecordName ([byte[]]$simAlias.Record)) -ne "AutoHuntUI.xml") { throw "Alias name simulation failed." }

Ok "Preflight passed."

if ($VerifyOnly) {
    Write-Host "VERIFY ONLY COMPLETE - NO CLIENT FILES MODIFIED." -ForegroundColor Green
    exit 0
}

$stamp=Get-Date -Format "yyyyMMdd_HHmmss"
$backup=Join-Path $ClientRoot ("AUTOHUNT_ALIAS_BACKUP_"+$stamp)
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($n in @("UI.idx","UI.pak","Data.idx","Data.pak")) {
    Copy-Item -LiteralPath (Join-Path $ClientRoot $n) -Destination (Join-Path $backup $n) -Force
}
$restore=@"
@echo off
cd /d "%~dp0"
copy /y "UI.idx" "..\UI.idx" >nul
copy /y "UI.pak" "..\UI.pak" >nul
copy /y "Data.idx" "..\Data.idx" >nul
copy /y "Data.pak" "..\Data.pak" >nul
echo Restore complete.
pause
"@
[IO.File]::WriteAllText((Join-Path $backup "RESTORE.cmd"),$restore,(New-Object Text.ASCIIEncoding))
Ok ("Backup created: "+$backup)

$writeStarted=$false
try {
    $writeStarted=$true

    $newUiOff=Append-Pak $uiPakPath $normalizedBlob
    [byte[]]$newUiIdx=Build-IndexWithAlias $uiIdx ([byte[]]$template.Record) "AutoHuntUI.xml" ([uint32]$newUiOff) ([uint32]$normalizedBlob.Length)
    [IO.File]::WriteAllBytes($uiIdxPath,$newUiIdx)

    $newDataOff=Append-Pak $dataPakPath $newListBlob
    Put-U32 $dataIdx ([int](8+($listRec.Index*128))) ([uint32]$newDataOff)
    Put-U32 $dataIdx ([int](8+($listRec.Index*128)+4)) ([uint32]$newListBlob.Length)
    [IO.File]::WriteAllBytes($dataIdxPath,$dataIdx)

    [byte[]]$vUiIdx=[IO.File]::ReadAllBytes($uiIdxPath)
    [byte[]]$vDataIdx=[IO.File]::ReadAllBytes($dataIdxPath)
    $alias=Find-Record $vUiIdx "AutoHuntUI.xml" "UI.idx final"
    $vl=Find-Record $vDataIdx "XmlUiList.xml" "Data.idx final"

    [byte[]]$aliasEncrypted = Read-Pak $uiPakPath ([uint32]$alias.Offset) ([uint32]$alias.Size)
    [byte[]]$aliasPlain = Decrypt-Xml $aliasEncrypted
    $aliasText = $utf8.GetString($aliasPlain)
    $aliasDoc=Load-Xml $aliasText
    if ($aliasDoc.DocumentElement.Name -ne "Control") { throw "Final alias XML root is not Control." }
    if ($null -eq $aliasDoc.SelectSingleNode("/Control/Window[@Name='AutoHuntSettingsWindow']")) {
        throw "Final alias window missing."
    }

    [byte[]]$vlEncrypted = Read-Pak $dataPakPath ([uint32]$vl.Offset) ([uint32]$vl.Size)
    [byte[]]$vlPlain = Decrypt-Xml $vlEncrypted
    $vlText = $utf8.GetString($vlPlain)
    $vlDoc=Load-Xml $vlText
    $hits=@($vlDoc.SelectNodes("/uilist/file[@name='AutoHuntUI.xml' and @load='true']"))
    if ($hits.Count -ne 1) { throw "Final XmlUiList alias entry missing." }

    Write-Host "V5 ALIAS LOAD TEST COMPLETE" -ForegroundColor Cyan
}
catch {
    if ($writeStarted) {
        Warn "Verification failed after write. Restoring original four files."
        foreach ($n in @("UI.idx","UI.pak","Data.idx","Data.pak")) {
            Copy-Item -LiteralPath (Join-Path $backup $n) -Destination (Join-Path $ClientRoot $n) -Force
        }
        Warn "Automatic restore completed."
    }
    throw
}
