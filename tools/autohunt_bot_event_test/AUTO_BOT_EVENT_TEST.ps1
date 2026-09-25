param([string]$ClientDir = "")

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "AUTO BOT EVENT TEST"

function Info([string]$s){ Write-Host "[BOTTEST] $s" -ForegroundColor Cyan }
function Ok([string]$s){ Write-Host "[BOTTEST] $s" -ForegroundColor Green }

function Resolve-ClientDir([string]$Requested){
    $c=@()
    if($Requested){$c+=$Requested}
    $c+=$PSScriptRoot
    $c+=(Get-Location).Path
    foreach($p in ($c|Select-Object -Unique)){
        if($p -and (Test-Path -LiteralPath (Join-Path $p "UI.idx")) -and (Test-Path -LiteralPath (Join-Path $p "UI.pak"))){
            return (Resolve-Path -LiteralPath $p).Path
        }
    }
    Add-Type -AssemblyName System.Windows.Forms
    $d=New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description="Select the Lineage client folder containing UI.idx and UI.pak."
    $d.ShowNewFolderButton=$false
    if($d.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK){throw "Folder selection cancelled."}
    return $d.SelectedPath
}

function Write-U32([byte[]]$b,[int]$o,[uint32]$v){
    $x=[BitConverter]::GetBytes($v);[Array]::Copy($x,0,$b,$o,4)
}

function Get-Entries([byte[]]$idx){
    if([Text.Encoding]::ASCII.GetString($idx,0,4) -ne "_EXT"){throw "Unsupported UI.idx."}
    $count=[BitConverter]::ToUInt32($idx,4);$out=@()
    for($i=0;$i-lt$count;$i++){
        $r=8+($i*128);if($r+128-gt$idx.Length){break}
        $len=0
        for($j=0;$j-lt112;$j++){if($idx[$r+16+$j]-eq0){break};$len++}
        $name=if($len-gt0){[Text.Encoding]::ASCII.GetString($idx,$r+16,$len)}else{""}
        $out += [PSCustomObject]@{
            RecordOffset=$r;Offset=[BitConverter]::ToUInt32($idx,$r);
            Size=[BitConverter]::ToUInt32($idx,$r+4);Name=$name
        }
    }
    return $out
}

$key=[byte[]]@(0xdc,0x84,0x01,0x21,0x2a,0x40,0x20,0x0a,0xdd,0x25,0xb9,0xa7,0x0d,0xb9,0xc9,0x4e)
$seed=[byte[]]@(0x3e,0x09,0x78,0xaa,0xc4,0xd5,0x30,0x63,0x30,0x0c,0x5f,0x9a,0x80,0x7f,0x22,0x46)

function AesPair([bool]$enc){
    $a=[Security.Cryptography.Aes]::Create();$a.Mode=[Security.Cryptography.CipherMode]::ECB
    $a.Padding=[Security.Cryptography.PaddingMode]::None;$a.KeySize=128;$a.BlockSize=128;$a.Key=$key
    if($enc){return @($a,$a.CreateEncryptor())}
    return @($a,$a.CreateDecryptor())
}
function Dec([byte[]]$enc){
    $plain=New-Object byte[] $enc.Length;[Array]::Copy($enc,0,$plain,0,4);$plain[0]=0x3c
    $p=AesPair $false;$a=$p[0];$t=$p[1]
    try{
        $prev=[byte[]]$seed.Clone();$pos=4
        while(($pos+16)-le$enc.Length){
            $blk=New-Object byte[] 16;[Array]::Copy($enc,$pos,$blk,0,16)
            $tmp=New-Object byte[] 16;[void]$t.TransformBlock($blk,0,16,$tmp,0)
            for($j=0;$j-lt16;$j++){$plain[$pos+$j]=$tmp[$j]-bxor$prev[$j]}
            $prev=$blk;$pos+=16
        }
        $j=0;while($pos-lt$enc.Length){$plain[$pos]=$enc[$pos]-bxor$prev[$j];$pos++;$j++}
    }finally{$t.Dispose();$a.Dispose()}
    return $plain
}
function Enc([byte[]]$plain){
    $enc=New-Object byte[] $plain.Length;[Array]::Copy($plain,0,$enc,0,4);$enc[0]=0x58
    $p=AesPair $true;$a=$p[0];$t=$p[1]
    try{
        $prev=[byte[]]$seed.Clone();$pos=4
        while(($pos+16)-le$plain.Length){
            $x=New-Object byte[] 16
            for($j=0;$j-lt16;$j++){$x[$j]=$plain[$pos+$j]-bxor$prev[$j]}
            $o=New-Object byte[] 16;[void]$t.TransformBlock($x,0,16,$o,0)
            [Array]::Copy($o,0,$enc,$pos,16);$prev=$o;$pos+=16
        }
        $j=0;while($pos-lt$plain.Length){$enc[$pos]=$plain[$pos]-bxor$prev[$j];$pos++;$j++}
    }finally{$t.Dispose();$a.Dispose()}
    return $enc
}
function Get-Entry([byte[]]$pak,$e){
    $b=New-Object byte[] $e.Size;[Array]::Copy($pak,[int]$e.Offset,$b,0,[int]$e.Size);return $b
}
function Attr($n,[string]$name,[string]$def=""){
    if($n.Attributes[$name]){return $n.Attributes[$name].Value};return $def
}
function SetAttr($n,[string]$name,[string]$value){
    if($n.Attributes[$name]){$n.Attributes[$name].Value=$value}
    else{$a=$n.OwnerDocument.CreateAttribute($name);$a.Value=$value;[void]$n.Attributes.Append($a)}
}
function XmlBytes($doc){
    $ms=New-Object IO.MemoryStream;$s=New-Object Xml.XmlWriterSettings
    $s.Encoding=New-Object Text.UTF8Encoding($false);$s.Indent=$false;$s.OmitXmlDeclaration=$false
    $s.NewLineHandling=[Xml.NewLineHandling]::None;$w=[Xml.XmlWriter]::Create($ms,$s)
    try{$doc.Save($w);$w.Flush();return $ms.ToArray()}finally{$w.Dispose();$ms.Dispose()}
}
function ButtonNumber($b){
    $c=Attr $b "Caption"
    if($c -match "^\s*(20|1[0-9]|[1-9])\s*$"){return [int]$Matches[1]}
    $tip=$b.SelectSingleNode("./Tooltip")
    if($tip){
        $tc=Attr $tip "Caption"
        if($tc -match "(?i)(?:TEST|BOT|ACT)\s*(20|1[0-9]|[1-9])"){return [int]$Matches[1]}
    }
    return 0
}

$ClientDir=Resolve-ClientDir $ClientDir
Info "Client: $ClientDir"
$idxPath=Join-Path $ClientDir "UI.idx";$pakPath=Join-Path $ClientDir "UI.pak"
$idx=[IO.File]::ReadAllBytes($idxPath);$pak=[IO.File]::ReadAllBytes($pakPath)
$entries=Get-Entries $idx
$main=$entries|Where-Object{$_.Name-ieq"MainButtonUI.xml"}|Select-Object -First 1
if(-not$main){throw "MainButtonUI.xml not found."}
$plain=Dec (Get-Entry $pak $main)
$txt=[Text.Encoding]::UTF8.GetString($plain)
$doc=New-Object Xml.XmlDocument;$doc.PreserveWhitespace=$true;$doc.LoadXml($txt)

$targets=@{
    2="BotOpenUI"
    5="OpenActionUI"
    7="BotOpenUI"
    9="BotOpenUI"
    10="OpenActionUI"
    17="BotOpenUI"
    20="OpenActionUI"
}

$found=@{}
foreach($b in @($doc.SelectNodes("//*[self::Button or self::CheckButton]"))){
    $n=ButtonNumber $b
    if($targets.ContainsKey($n) -and -not$found.ContainsKey($n)){
        $found[$n]=$b
    }
}
if($found.Count-ne7){
    $missing=@()
    foreach($n in 2,5,7,9,10,17,20){if(-not$found.ContainsKey($n)){$missing+=$n}}
    throw ("Test buttons not found: "+($missing -join ","))
}

foreach($n in 2,5,7,9,10,17,20){
    $b=$found[$n]
    SetAttr $b "MouseEvent" $targets[$n]
    SetAttr $b "BasePng" "29999;1"
    SetAttr $b "PushPng" "29999;1"
    SetAttr $b "HighlightPng" "29999;1"
    $tip=$b.SelectSingleNode("./Tooltip")
    if(-not$tip){$tip=$doc.CreateElement("Tooltip");[void]$b.AppendChild($tip)}
    $tag=if($targets[$n]-eq"BotOpenUI"){"BOT"}else{"ACT"}
    SetAttr $tip "Caption" ($tag+" "+$n)
    Info ("Button "+$n+" -> "+$targets[$n])
}

$xml=XmlBytes $doc;$new=Enc $xml
$stamp=Get-Date -Format "yyyyMMdd_HHmmss"
$backup=Join-Path $ClientDir ("AUTO_BOT_EVENT_BACKUP_"+$stamp)
New-Item -ItemType Directory -Path $backup -Force|Out-Null
Copy-Item -LiteralPath $idxPath -Destination (Join-Path $backup "UI.idx")
Copy-Item -LiteralPath $pakPath -Destination (Join-Path $backup "UI.pak")

$newOff=[uint32]$pak.Length;$newSize=[uint32]$new.Length
$f=[IO.File]::Open($pakPath,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$f.Write($new,0,$new.Length);$f.Flush()}finally{$f.Dispose()}
Write-U32 $idx $main.RecordOffset $newOff
Write-U32 $idx ($main.RecordOffset+4) $newSize
Write-U32 $idx ($main.RecordOffset+8) 0
Write-U32 $idx ($main.RecordOffset+12) 0
[IO.File]::WriteAllBytes($idxPath,$idx)

$report=@(
"AUTO BOT EVENT TEST",
"2=BotOpenUI",
"5=OpenActionUI",
"7=BotOpenUI",
"9=BotOpenUI",
"10=OpenActionUI",
"17=BotOpenUI",
"20=OpenActionUI",
"Backup="+$backup
)
[IO.File]::WriteAllLines((Join-Path $ClientDir "AUTO_BOT_EVENT_RESULT.txt"),$report,[Text.Encoding]::ASCII)

Ok "PATCH COMPLETE"
Ok "CONTROL: 5,10,20 should open Action UI."
Ok "BOT TEST: 2,7,9,17 use BotOpenUI."
Write-Host ""
Write-Host "Report results like: 2=no response, 7=auto window, 9=..., 17=..." -ForegroundColor Yellow
