param([string]$ClientDir = "")

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "ORIGINAL BOT WINDOW TEST"

function Info([string]$s){Write-Host "[ORIGBOT] $s" -ForegroundColor Cyan}
function Ok([string]$s){Write-Host "[ORIGBOT] $s" -ForegroundColor Green}

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
    $d.Description="Select client folder containing UI.idx and UI.pak."
    $d.ShowNewFolderButton=$false
    if($d.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK){throw "Folder selection cancelled."}
    return $d.SelectedPath
}

function Write-U32([byte[]]$b,[int]$o,[uint32]$v){$x=[BitConverter]::GetBytes($v);[Array]::Copy($x,0,$b,$o,4)}
function Get-Entries([byte[]]$idx){
    if([Text.Encoding]::ASCII.GetString($idx,0,4) -ne "_EXT"){throw "Unsupported UI.idx."}
    $count=[BitConverter]::ToUInt32($idx,4);$out=@()
    for($i=0;$i-lt$count;$i++){
        $r=8+($i*128);if($r+128-gt$idx.Length){break}
        $len=0;for($j=0;$j-lt112;$j++){if($idx[$r+16+$j]-eq0){break};$len++}
        $name=if($len-gt0){[Text.Encoding]::ASCII.GetString($idx,$r+16,$len)}else{""}
        $out += [PSCustomObject]@{RecordOffset=$r;Offset=[BitConverter]::ToUInt32($idx,$r);Size=[BitConverter]::ToUInt32($idx,$r+4);Name=$name}
    }
    return $out
}

$key=[byte[]]@(0xdc,0x84,0x01,0x21,0x2a,0x40,0x20,0x0a,0xdd,0x25,0xb9,0xa7,0x0d,0xb9,0xc9,0x4e)
$seed=[byte[]]@(0x3e,0x09,0x78,0xaa,0xc4,0xd5,0x30,0x63,0x30,0x0c,0x5f,0x9a,0x80,0x7f,0x22,0x46)

function Pair([bool]$enc){
    $a=[Security.Cryptography.Aes]::Create()
    $a.Mode=[Security.Cryptography.CipherMode]::ECB
    $a.Padding=[Security.Cryptography.PaddingMode]::None
    $a.KeySize=128;$a.BlockSize=128;$a.Key=$key
    if($enc){return @($a,$a.CreateEncryptor())}
    return @($a,$a.CreateDecryptor())
}
function Dec([byte[]]$enc){
    $plain=New-Object byte[] $enc.Length
    [Array]::Copy($enc,0,$plain,0,4);$plain[0]=0x3c
    $p=Pair $false;$a=$p[0];$t=$p[1]
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
    $enc=New-Object byte[] $plain.Length
    [Array]::Copy($plain,0,$enc,0,4);$enc[0]=0x58
    $p=Pair $true;$a=$p[0];$t=$p[1]
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
function SetAttr($n,[string]$name,[string]$value){
    if($n.Attributes[$name]){$n.Attributes[$name].Value=$value}
    else{$a=$n.OwnerDocument.CreateAttribute($name);$a.Value=$value;[void]$n.Attributes.Append($a)}
}
function XmlBytes($doc){
    $ms=New-Object IO.MemoryStream
    $s=New-Object Xml.XmlWriterSettings
    $s.Encoding=New-Object Text.UTF8Encoding($false)
    $s.Indent=$false;$s.OmitXmlDeclaration=$false;$s.NewLineHandling=[Xml.NewLineHandling]::None
    $w=[Xml.XmlWriter]::Create($ms,$s)
    try{$doc.Save($w);$w.Flush();return $ms.ToArray()}finally{$w.Dispose();$ms.Dispose()}
}

$ClientDir=Resolve-ClientDir $ClientDir
Info "Client: $ClientDir"
$idxPath=Join-Path $ClientDir "UI.idx"
$pakPath=Join-Path $ClientDir "UI.pak"
$idx=[IO.File]::ReadAllBytes($idxPath)
$pak=[IO.File]::ReadAllBytes($pakPath)
$entries=Get-Entries $idx
$main=$entries|Where-Object{$_.Name-ieq"MainButtonUI.xml"}|Select-Object -First 1
if(-not$main){throw "MainButtonUI.xml not found."}

$buf=New-Object byte[] $main.Size
[Array]::Copy($pak,[int]$main.Offset,$buf,0,[int]$main.Size)
$plain=Dec $buf
$doc=New-Object Xml.XmlDocument
$doc.PreserveWhitespace=$true
$doc.LoadXml([Text.Encoding]::UTF8.GetString($plain))

$win=$doc.SelectSingleNode("//*[@Name='BotOpenWindow']")
if(-not$win){throw "Original BotOpenWindow not found."}
$btn=$win.SelectSingleNode(".//*[@Name='BotOpenButton']")
if(-not$btn){throw "Original BotOpenButton not found."}

# Activate the exact original native object. Do not clone it.
SetAttr $win "X" "620"
SetAttr $win "Y" "380"
SetAttr $win "Width" "31"
SetAttr $win "Height" "28"
SetAttr $win "MouseEvent" "Dummy"
SetAttr $win "Top" "1"
SetAttr $win "MostTop" "1"
SetAttr $win "Moveable" "12"
SetAttr $win "Activate" "1"
SetAttr $win "Visible" "1"
SetAttr $win "Transparent" "false"
SetAttr $win "Toggle" "false"

SetAttr $btn "X" "0"
SetAttr $btn "Y" "0"
SetAttr $btn "Width" "27"
SetAttr $btn "Height" "27"
SetAttr $btn "BasePng" "29999;1"
SetAttr $btn "PushPng" "29999;1"
SetAttr $btn "HighlightPng" "29999;1"
SetAttr $btn "MouseEvent" "BotOpenUI"
SetAttr $btn "Activate" "1"
SetAttr $btn "Visible" "1"
SetAttr $btn "Caption" "BOT"

$tip=$btn.SelectSingleNode("./Tooltip")
if(-not$tip){$tip=$doc.CreateElement("Tooltip");[void]$btn.AppendChild($tip)}
SetAttr $tip "X" "0";SetAttr $tip "Y" "0";SetAttr $tip "Width" "27";SetAttr $tip "Height" "27"
SetAttr $tip "Caption" "ORIGINAL BOT"

# Keep one confirmed control button as Action UI to verify this patch was applied.
$control=$null
foreach($b in @($doc.SelectNodes("//*[self::Button or self::CheckButton]"))){
    if($b.Attributes["Caption"] -and $b.Attributes["Caption"].Value -eq "20"){$control=$b;break}
}
if($control){SetAttr $control "MouseEvent" "OpenActionUI"}

$newPlain=XmlBytes $doc
$new=Enc $newPlain

$stamp=Get-Date -Format "yyyyMMdd_HHmmss"
$backup=Join-Path $ClientDir ("ORIGINAL_BOT_BACKUP_"+$stamp)
New-Item -ItemType Directory -Force -Path $backup|Out-Null
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

@(
"ORIGINAL BOT WINDOW TEST",
"BotOpenWindow X=620 Y=380 Activate=1 Top=1 MostTop=1",
"BotOpenButton image=29999 event=BotOpenUI",
"Tooltip=ORIGINAL BOT",
"Backup="+$backup
)|Set-Content -Encoding ASCII (Join-Path $ClientDir "ORIGINAL_BOT_TEST_RESULT.txt")

Ok "PATCH COMPLETE"
Ok "Look near X=620 Y=380 for the AUTO icon."
Ok "Hover tooltip: ORIGINAL BOT"
Write-Host ""
Write-Host "Click only the ORIGINAL BOT icon and report whether any window opens." -ForegroundColor Yellow
