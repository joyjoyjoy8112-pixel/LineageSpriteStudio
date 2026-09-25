param([string]$ClientDir = "")

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "AUTOHUNT UI STAGE1"

function Info([string]$s){Write-Host "[AHUI1] $s" -ForegroundColor Cyan}
function Ok([string]$s){Write-Host "[AHUI1] $s" -ForegroundColor Green}
function Warn([string]$s){Write-Host "[AHUI1] $s" -ForegroundColor Yellow}

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
    if($idx.Length-lt8 -or [Text.Encoding]::ASCII.GetString($idx,0,4)-ne"_EXT"){throw "Unsupported UI.idx."}
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
function Attr($n,[string]$name,[string]$def=""){if($n.Attributes[$name]){return $n.Attributes[$name].Value};return $def}
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
function Prefix-Names($node,[string]$prefix){
    foreach($n in @($node.SelectNodes(".//*[@Name]"))){
        $old=$n.Attributes["Name"].Value
        if($old -notmatch "^AutoHunt_"){$n.Attributes["Name"].Value=$prefix+$old}
    }
    if($node.Attributes["Name"] -and $node.Attributes["Name"].Value -notmatch "^AutoHunt_"){
        $node.Attributes["Name"].Value=$prefix+$node.Attributes["Name"].Value
    }
}
function Hide-Node($n){
    SetAttr $n "Activate" "0"
    SetAttr $n "Visible" "0"
}
function New-Text($doc,$parent,$template,[string]$name,[int]$x,[int]$y,[int]$w,[int]$h,[string]$caption){
    if($template){$n=$doc.ImportNode($template,$true)}else{$n=$doc.CreateElement("Text")}
    SetAttr $n "Name" $name;SetAttr $n "X" "$x";SetAttr $n "Y" "$y";SetAttr $n "Width" "$w";SetAttr $n "Height" "$h"
    SetAttr $n "Caption" $caption
    if($n.Attributes["Text"]){$n.Attributes["Text"].Value=$caption}
    if($n.Attributes["Value"]){$n.Attributes["Value"].Value=$caption}
    SetAttr $n "Activate" "1";SetAttr $n "Visible" "1"
    [void]$parent.AppendChild($n);return $n
}
function New-Button($doc,$parent,$template,[string]$name,[int]$x,[int]$y,[int]$w,[int]$h,[string]$caption){
    if($template){$n=$doc.ImportNode($template,$true)}else{$n=$doc.CreateElement("Button")}
    SetAttr $n "Name" $name;SetAttr $n "X" "$x";SetAttr $n "Y" "$y";SetAttr $n "Width" "$w";SetAttr $n "Height" "$h"
    SetAttr $n "Caption" $caption;SetAttr $n "Activate" "1";SetAttr $n "Visible" "1";SetAttr $n "MouseEvent" "Dummy"
    $tip=$n.SelectSingleNode("./Tooltip");if($tip){$tip.ParentNode.RemoveChild($tip)|Out-Null}
    [void]$parent.AppendChild($n);return $n
}

# Korean captions are generated from Unicode code points so Windows PowerShell 5 cannot corrupt them.
$S_TITLE=([char]0xC790+[char]0xB3D9+[char]0xC0AC+[char]0xB0E5+[char]0x0020+[char]0xC124+[char]0xC815)
$S_TAB1=([char]0xAE30+[char]0xBCF8+[char]0x0020+[char]0xC124+[char]0xC815)
$S_TAB2=([char]0xC0AC+[char]0xB0E5+[char]0xD130+[char]0x0020+[char]0xC124+[char]0xC815)
$S_TAB3=([char]0xBB3C+[char]0xC57D+[char]0x0020+[char]0xC124+[char]0xC815)
$S_TAB4=([char]0xC2A4+[char]0xD0AC+[char]0x0020+[char]0xC124+[char]0xC815)
$S_HUNT=([char]0xC0AC+[char]0xB0E5+[char]0xD130)
$S_HUNTVAL=([char]0xAE30+[char]0xB780+[char]0x0020+[char]0xAC10+[char]0xC625+[char]0x0020+[char]0x0031+[char]0xCE35)
$S_POTION=([char]0xC0AC+[char]0xC6A9+[char]0xD560+[char]0x0020+[char]0xBB3C+[char]0xC57D)
$S_POTIONVAL=([char]0xBE68+[char]0xAC04+[char]0x0020+[char]0xBB3C+[char]0xC57D)
$S_PHP=([char]0xBB3C+[char]0xC57D+[char]0x0020+[char]0xC0AC+[char]0xC6A9+[char]0x0020+[char]0x0048+[char]0x0050)
$S_RETURNHP=([char]0xADC0+[char]0xD658+[char]0x0020+[char]0x0048+[char]0x0050)
$S_RESUMEHP=([char]0xC0AC+[char]0xB0E5+[char]0xD130+[char]0x0020+[char]0xBCF5+[char]0xADC0+[char]0x0020+[char]0x0048+[char]0x0050)
$S_UNDER=([char]0x0025+[char]0x0020+[char]0xC774+[char]0xD558)
$S_OVER=([char]0x0025+[char]0x0020+[char]0xC774+[char]0xC0C1)
$S_AUTORETURN=([char]0xC790+[char]0xB3D9+[char]0x0020+[char]0xADC0+[char]0xD658)
$S_AUTOSHOP=([char]0xC790+[char]0xB3D9+[char]0x0020+[char]0xC0C1+[char]0xC810)
$S_USE=([char]0xC0AC+[char]0xC6A9)
$S_STATUS=([char]0xC790+[char]0xB3D9+[char]0xC0AC+[char]0xB0E5+[char]0x0020+[char]0xC0C1+[char]0xD0DC)
$S_WAITING=([char]0xD604+[char]0xC7AC+[char]0x0020+[char]0xC0C1+[char]0xD0DC+[char]0x0020+[char]0x003A+[char]0x0020+[char]0xB300+[char]0xAE30+[char]0x0020+[char]0xC911)
$S_ATKUSE=([char]0xACF5+[char]0xACA9+[char]0x0020+[char]0xC2A4+[char]0xD0AC+[char]0x0020+[char]0xC0AC+[char]0xC6A9)
$S_BUFFUSE=([char]0xBC84+[char]0xD504+[char]0x0020+[char]0xC2A4+[char]0xD0AC+[char]0x0020+[char]0xC0AC+[char]0xC6A9)
$S_DEFUSE=([char]0xBC29+[char]0xC5B4+[char]0x0020+[char]0xC2A4+[char]0xD0AC+[char]0x0020+[char]0xC0AC+[char]0xC6A9)
$S_ATKSKILL=([char]0xACF5+[char]0xACA9+[char]0x0020+[char]0xC2A4+[char]0xD0AC)
$S_BUFFSKILL=([char]0xBC84+[char]0xD504+[char]0x0020+[char]0xC2A4+[char]0xD0AC)
$S_DEFSKILL=([char]0xBC29+[char]0xC5B4+[char]0x0020+[char]0xC2A4+[char]0xD0AC)
$S_SELECT=([char]0xC120+[char]0xD0DD)
$S_DEFAULTS=([char]0xAE30+[char]0xBCF8+[char]0xAC12)
$S_SAVE=([char]0xC800+[char]0xC7A5)
$S_START=([char]0xC790+[char]0xB3D9+[char]0xC0AC+[char]0xB0E5+[char]0x0020+[char]0xC2DC+[char]0xC791)
$S_STOP=([char]0xC911+[char]0xC9C0)

$ClientDir=Resolve-ClientDir $ClientDir
Info "Client: $ClientDir"
$idxPath=Join-Path $ClientDir "UI.idx";$pakPath=Join-Path $ClientDir "UI.pak"
$idx=[IO.File]::ReadAllBytes($idxPath);$pak=[IO.File]::ReadAllBytes($pakPath)
$entries=Get-Entries $idx
$rank=$entries|Where-Object{$_.Name-ieq"HighRankUI.xml"}|Select-Object -First 1
if(-not$rank){throw "HighRankUI.xml not found."}

$buf=New-Object byte[] $rank.Size
[Array]::Copy($pak,[int]$rank.Offset,$buf,0,[int]$rank.Size)
$plain=Dec $buf
$doc=New-Object Xml.XmlDocument;$doc.PreserveWhitespace=$true
$doc.LoadXml([Text.Encoding]::UTF8.GetString($plain))

# Remove any prior stage1 injected window.
foreach($old in @($doc.SelectNodes("//*[@Name='AutoHunt_Stage1Window']"))){
    if($old.ParentNode){[void]$old.ParentNode.RemoveChild($old)}
}

$sourceWin=$doc.SelectSingleNode("//*[self::Window][1]")
if(-not$sourceWin){throw "No Window node found in HighRankUI.xml."}

# Capture reusable control templates before cloning/hiding.
$buttonTemplate=$sourceWin.SelectSingleNode(".//*[self::Button or self::CheckButton][1]")
$textTemplate=$null
foreach($n in @($sourceWin.SelectNodes(".//*[@Caption]"))){
    if($n.Name -notmatch "^(Window|Control|Button|CheckButton|Tooltip)$"){$textTemplate=$n;break}
}
if(-not$textTemplate){Warn "No text template found; using generic Text nodes."}
if(-not$buttonTemplate){Warn "No button template found; using generic Button nodes."}

$clone=$sourceWin.CloneNode($true)
Prefix-Names $clone "AutoHunt_"
SetAttr $clone "Name" "AutoHunt_Stage1Window"
SetAttr $clone "X" "145";SetAttr $clone "Y" "95"
SetAttr $clone "Activate" "1";SetAttr $clone "Visible" "1";SetAttr $clone "Top" "1"
SetAttr $clone "Moveable" "12"

# Hide all old rank-specific interactive/text content inside the clone,
# while leaving image/background/frame nodes intact.
foreach($n in @($clone.SelectNodes(".//*"))){
    $nm=Attr $n "Name"
    $tag=$n.Name
    if($tag -match "^(Button|CheckButton|List|ListBox|Grid|Table|Tree|Scroll|Edit|ComboBox|Combo|Text|Label|Static)$" -or
       ($n.Attributes["Caption"] -and $tag -notmatch "^(Image|Png|Sprite)$")){
        Hide-Node $n
    }
}

# Build stage1 visible layout. No server/DB logic yet.
New-Text $doc $clone $textTemplate "AutoHunt_Title" 165 10 180 24 $S_TITLE | Out-Null

$tabY=42
New-Button $doc $clone $buttonTemplate "AutoHunt_TabBasic" 20 $tabY 105 24 $S_TAB1 | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_TabGround" 130 $tabY 105 24 $S_TAB2 | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_TabPotion" 240 $tabY 105 24 $S_TAB3 | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_TabSkill" 350 $tabY 105 24 $S_TAB4 | Out-Null

# Left column
$lx=30;$vx=155;$y=82;$dy=28
New-Text $doc $clone $textTemplate "AutoHunt_L_Hunt" $lx $y 110 18 $S_HUNT | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_V_Hunt" $vx ($y-3) 165 22 $S_HUNTVAL | Out-Null
$y+=$dy
New-Text $doc $clone $textTemplate "AutoHunt_L_Potion" $lx $y 110 18 $S_POTION | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_V_Potion" $vx ($y-3) 165 22 $S_POTIONVAL | Out-Null
$y+=$dy
New-Text $doc $clone $textTemplate "AutoHunt_L_PHP" $lx $y 110 18 $S_PHP | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_V_PHP" $vx ($y-3) 58 22 "50" | Out-Null
New-Text $doc $clone $textTemplate "AutoHunt_U_PHP" 220 $y 65 18 $S_UNDER | Out-Null
$y+=$dy
New-Text $doc $clone $textTemplate "AutoHunt_L_ReturnHP" $lx $y 110 18 $S_RETURNHP | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_V_ReturnHP" $vx ($y-3) 58 22 "20" | Out-Null
New-Text $doc $clone $textTemplate "AutoHunt_U_ReturnHP" 220 $y 65 18 $S_UNDER | Out-Null
$y+=$dy
New-Text $doc $clone $textTemplate "AutoHunt_L_ResumeHP" $lx $y 110 18 $S_RESUMEHP | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_V_ResumeHP" $vx ($y-3) 58 22 "90" | Out-Null
New-Text $doc $clone $textTemplate "AutoHunt_U_ResumeHP" 220 $y 65 18 $S_OVER | Out-Null
$y+=$dy
New-Text $doc $clone $textTemplate "AutoHunt_L_AutoReturn" $lx $y 110 18 $S_AUTORETURN | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_V_AutoReturn" $vx ($y-3) 80 22 $S_USE | Out-Null
$y+=$dy
New-Text $doc $clone $textTemplate "AutoHunt_L_AutoShop" $lx $y 110 18 $S_AUTOSHOP | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_V_AutoShop" $vx ($y-3) 80 22 $S_USE | Out-Null

# Right column/status/skill quick view
$rx=335
New-Text $doc $clone $textTemplate "AutoHunt_StatusTitle" $rx 82 130 18 $S_STATUS | Out-Null
New-Text $doc $clone $textTemplate "AutoHunt_StatusValue" $rx 106 145 18 $S_WAITING | Out-Null

$sy=145
foreach($pair in @(
    @("AutoHunt_AtkUse",$S_ATKUSE),
    @("AutoHunt_BuffUse",$S_BUFFUSE),
    @("AutoHunt_DefUse",$S_DEFUSE)
)){
    New-Text $doc $clone $textTemplate ($pair[0]+"_L") $rx $sy 120 18 $pair[1] | Out-Null
    New-Button $doc $clone $buttonTemplate ($pair[0]+"_V") 448 ($sy-3) 55 22 $S_USE | Out-Null
    $sy+=27
}
foreach($pair in @(
    @("AutoHunt_AtkSkill",$S_ATKSKILL),
    @("AutoHunt_BuffSkill",$S_BUFFSKILL),
    @("AutoHunt_DefSkill",$S_DEFSKILL)
)){
    New-Text $doc $clone $textTemplate ($pair[0]+"_L") $rx $sy 100 18 $pair[1] | Out-Null
    New-Button $doc $clone $buttonTemplate ($pair[0]+"_V") 430 ($sy-3) 75 22 $S_SELECT | Out-Null
    $sy+=27
}

# Bottom buttons
New-Button $doc $clone $buttonTemplate "AutoHunt_Defaults" 25 278 105 25 $S_DEFAULTS | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_Save" 140 278 105 25 $S_SAVE | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_Start" 255 278 140 25 $S_START | Out-Null
New-Button $doc $clone $buttonTemplate "AutoHunt_Stop" 405 278 90 25 $S_STOP | Out-Null

# Append beside original rank window so rank UI remains intact.
[void]$sourceWin.ParentNode.AppendChild($clone)

$newPlain=XmlBytes $doc
$newEnc=Enc $newPlain

$stamp=Get-Date -Format "yyyyMMdd_HHmmss"
$backup=Join-Path $ClientDir ("AUTOHUNT_STAGE1_BACKUP_"+$stamp)
New-Item -ItemType Directory -Force -Path $backup|Out-Null
Copy-Item -LiteralPath $idxPath -Destination (Join-Path $backup "UI.idx")
Copy-Item -LiteralPath $pakPath -Destination (Join-Path $backup "UI.pak")

$newOff=[uint32]$pak.Length;$newSize=[uint32]$newEnc.Length
$f=[IO.File]::Open($pakPath,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$f.Write($newEnc,0,$newEnc.Length);$f.Flush()}finally{$f.Dispose()}

Write-U32 $idx $rank.RecordOffset $newOff
Write-U32 $idx ($rank.RecordOffset+4) $newSize
Write-U32 $idx ($rank.RecordOffset+8) 0
Write-U32 $idx ($rank.RecordOffset+12) 0
[IO.File]::WriteAllBytes($idxPath,$idx)

@(
"AUTOHUNT UI STAGE1",
"Injected into HighRankUI.xml as AutoHunt_Stage1Window",
"Visible immediately for layout test",
"No server/DB/action logic in stage1",
"Backup="+$backup
)|Set-Content -Encoding ASCII (Join-Path $ClientDir "AUTOHUNT_STAGE1_RESULT.txt")

Ok "PATCH COMPLETE"
Ok "Stage1 auto-hunt settings window was injected."
Ok "This stage is VISUAL/LAYOUT ONLY."
Ok "Backup: $backup"
