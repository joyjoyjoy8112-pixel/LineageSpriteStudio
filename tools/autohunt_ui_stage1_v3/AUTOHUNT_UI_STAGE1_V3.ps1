param([string]$ClientDir = "")

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "AUTOHUNT UI STAGE1 V3"

function Info([string]$s){Write-Host "[AHUI1V3] $s" -ForegroundColor Cyan}
function Ok([string]$s){Write-Host "[AHUI1V3] $s" -ForegroundColor Green}
function Warn([string]$s){Write-Host "[AHUI1V3] $s" -ForegroundColor Yellow}

function Resolve-ClientDir([string]$Requested){
    $c=@(); if($Requested){$c+=$Requested}; $c+=$PSScriptRoot; $c+=(Get-Location).Path
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
    $a=[Security.Cryptography.Aes]::Create();$a.Mode=[Security.Cryptography.CipherMode]::ECB
    $a.Padding=[Security.Cryptography.PaddingMode]::None;$a.KeySize=128;$a.BlockSize=128;$a.Key=$key
    if($enc){return @($a,$a.CreateEncryptor())};return @($a,$a.CreateDecryptor())
}
function Dec([byte[]]$enc){
    $plain=New-Object byte[] $enc.Length;[Array]::Copy($enc,0,$plain,0,4);$plain[0]=0x3c
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
    $enc=New-Object byte[] $plain.Length;[Array]::Copy($plain,0,$enc,0,4);$enc[0]=0x58
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
function IntAttr($n,[string]$name,[int]$def=0){$v=$def;[void][int]::TryParse((Attr $n $name "$def"),[ref]$v);return $v}
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
        if($old -notmatch "^AutoHuntV3_"){$n.Attributes["Name"].Value=$prefix+$old}
    }
    if($node.Attributes["Name"] -and $node.Attributes["Name"].Value -notmatch "^AutoHuntV3_"){
        $node.Attributes["Name"].Value=$prefix+$node.Attributes["Name"].Value
    }
}
function CloneButton($doc,$parent,$template,[string]$name,[int]$x,[int]$y,[int]$w,[int]$h,[string]$caption){
    $n=$doc.ImportNode($template,$true)
    SetAttr $n "Name" $name;SetAttr $n "X" "$x";SetAttr $n "Y" "$y";SetAttr $n "Width" "$w";SetAttr $n "Height" "$h"
    SetAttr $n "Caption" $caption;SetAttr $n "MouseEvent" "Dummy";SetAttr $n "Activate" "1";SetAttr $n "Visible" "1"
    $tip=$n.SelectSingleNode("./Tooltip");if($tip){[void]$tip.ParentNode.RemoveChild($tip)}
    [void]$parent.AppendChild($n);return $n
}

$S_TITLE=([char]0xC790+[char]0xB3D9+[char]0xC0AC+[char]0xB0E5+[char]0x0020+[char]0xC124+[char]0xC815)
$S_BASIC=([char]0xAE30+[char]0xBCF8+[char]0x0020+[char]0xC124+[char]0xC815)
$S_GROUND=([char]0xC0AC+[char]0xB0E5+[char]0xD130+[char]0x0020+[char]0xC124+[char]0xC815)
$S_POTIONTAB=([char]0xBB3C+[char]0xC57D+[char]0x0020+[char]0xC124+[char]0xC815)
$S_SKILLTAB=([char]0xC2A4+[char]0xD0AC+[char]0x0020+[char]0xC124+[char]0xC815)
$S_HUNT=([char]0xC0AC+[char]0xB0E5+[char]0xD130)
$S_HUNTVAL=([char]0xAE30+[char]0xB780+[char]0x0020+[char]0xAC10+[char]0xC625+[char]0x0020+[char]0x0031+[char]0xCE35)
$S_POTION=([char]0xC0AC+[char]0xC6A9+[char]0xD560+[char]0x0020+[char]0xBB3C+[char]0xC57D)
$S_POTIONVAL=([char]0xBE68+[char]0xAC04+[char]0x0020+[char]0xBB3C+[char]0xC57D)
$S_PHP=([char]0xBB3C+[char]0xC57D+[char]0x0020+[char]0xC0AC+[char]0xC6A9+[char]0x0020+[char]0x0048+[char]0x0050)
$S_RET=([char]0xADC0+[char]0xD658+[char]0x0020+[char]0x0048+[char]0x0050)
$S_RESUME=([char]0xC0AC+[char]0xB0E5+[char]0xD130+[char]0x0020+[char]0xBCF5+[char]0xADC0+[char]0x0020+[char]0x0048+[char]0x0050)
$S_AR=([char]0xC790+[char]0xB3D9+[char]0x0020+[char]0xADC0+[char]0xD658)
$S_SHOP=([char]0xC790+[char]0xB3D9+[char]0x0020+[char]0xC0C1+[char]0xC810)
$S_USE=([char]0xC0AC+[char]0xC6A9)
$S_STATUS=([char]0xC790+[char]0xB3D9+[char]0xC0AC+[char]0xB0E5+[char]0x0020+[char]0xC0C1+[char]0xD0DC)
$S_WAIT=([char]0xB300+[char]0xAE30+[char]0x0020+[char]0xC911)
$S_ATK=([char]0xACF5+[char]0xACA9+[char]0x0020+[char]0xC2A4+[char]0xD0AC+[char]0x0020+[char]0xC0AC+[char]0xC6A9)
$S_BUFF=([char]0xBC84+[char]0xD504+[char]0x0020+[char]0xC2A4+[char]0xD0AC+[char]0x0020+[char]0xC0AC+[char]0xC6A9)
$S_DEF=([char]0xBC29+[char]0xC5B4+[char]0x0020+[char]0xC2A4+[char]0xD0AC+[char]0x0020+[char]0xC0AC+[char]0xC6A9)
$S_ATKS=([char]0xACF5+[char]0xACA9+[char]0x0020+[char]0xC2A4+[char]0xD0AC)
$S_BUFFS=([char]0xBC84+[char]0xD504+[char]0x0020+[char]0xC2A4+[char]0xD0AC)
$S_DEFS=([char]0xBC29+[char]0xC5B4+[char]0x0020+[char]0xC2A4+[char]0xD0AC)
$S_SELECT=([char]0xC120+[char]0xD0DD)
$S_DEFAULTS=([char]0xAE30+[char]0xBCF8+[char]0xAC12)
$S_SAVE=([char]0xC800+[char]0xC7A5)
$S_START=([char]0xC790+[char]0xB3D9+[char]0xC0AC+[char]0xB0E5+[char]0x0020+[char]0xC2DC+[char]0xC791)
$S_STOP=([char]0xC911+[char]0xC9C0)
$S_50=([char]0x0035+[char]0x0030+[char]0x0025+[char]0x0020+[char]0xC774+[char]0xD558)
$S_20=([char]0x0032+[char]0x0030+[char]0x0025+[char]0x0020+[char]0xC774+[char]0xD558)
$S_90=([char]0x0039+[char]0x0030+[char]0x0025+[char]0x0020+[char]0xC774+[char]0xC0C1)

$ClientDir=Resolve-ClientDir $ClientDir
Info "Client: $ClientDir"
$idxPath=Join-Path $ClientDir "UI.idx";$pakPath=Join-Path $ClientDir "UI.pak"
$idx=[IO.File]::ReadAllBytes($idxPath);$pak=[IO.File]::ReadAllBytes($pakPath)
$entries=Get-Entries $idx
$rank=$entries|Where-Object{$_.Name-ieq"HighRankUI.xml"}|Select-Object -First 1
if(-not$rank){throw "HighRankUI.xml not found."}

$buf=New-Object byte[] $rank.Size;[Array]::Copy($pak,[int]$rank.Offset,$buf,0,[int]$rank.Size)
$doc=New-Object Xml.XmlDocument;$doc.PreserveWhitespace=$true
$doc.LoadXml([Text.Encoding]::UTF8.GetString((Dec $buf)))

# Remove previous injected clones if present.
foreach($old in @($doc.SelectNodes("//*[@Name='AutoHunt_Stage1Window' or @Name='AutoHuntV2_Window' or @Name='AutoHuntV3_Window']"))){
    if($old.ParentNode){[void]$old.ParentNode.RemoveChild($old)}
}

# V3 deliberately avoids Korean-caption lookup. V1 proved the first window is the visible rank window.
$sourceWin=$doc.SelectSingleNode("//*[self::Window][1]")
if(-not$sourceWin){throw "No Window found in HighRankUI.xml."}

$clone=$sourceWin.CloneNode($true)
Prefix-Names $clone "AutoHuntV3_"
SetAttr $clone "Name" "AutoHuntV3_Window"
SetAttr $clone "X" "285";SetAttr $clone "Y" "145"
SetAttr $clone "Activate" "1";SetAttr $clone "Visible" "1";SetAttr $clone "Top" "1";SetAttr $clone "Moveable" "12"

# Find rank tab row by geometry: the largest horizontal group of button/checkbutton controls near top.
$btns=@($clone.SelectNodes(".//*[self::Button or self::CheckButton]"))
if($btns.Count-lt4){throw "Too few button controls in HighRankUI.xml."}

$groups=@{}
foreach($b in $btns){
    $y=IntAttr $b "Y" 0
    if($y-lt0 -or $y-gt120){continue}
    $bucket=[int]([Math]::Round($y/3.0)*3)
    if(-not$groups.ContainsKey($bucket)){$groups[$bucket]=New-Object Collections.ArrayList}
    [void]$groups[$bucket].Add($b)
}
$best=$null;$bestCount=0;$bestY=0
foreach($k in $groups.Keys){
    $arr=@($groups[$k]|Sort-Object {IntAttr $_ "X" 0})
    if($arr.Count-gt$bestCount){
        $best=$arr;$bestCount=$arr.Count;$bestY=[int]$k
    }
}
if($bestCount-lt4){throw "Could not identify rank tab row by geometry."}

# Prefer the first 9 controls from the widest tab row.
$tabs=@($best|Sort-Object {IntAttr $_ "X" 0})
if($tabs.Count-gt9){$tabs=@($tabs|Select-Object -First 9)}
$tabParent=$tabs[0].ParentNode
$template=$tabs[0]
$tabY=IntAttr $template "Y" 0
$tabX=IntAttr $template "X" 15
$tabH=IntAttr $template "Height" 20
if($tabH-le0){$tabH=20}

Info ("Detected tab row: Y="+$tabY+" count="+$tabs.Count)

# Relabel/reposition first 4 detected tabs; move the rest off-screen.
$newTabCaps=@($S_BASIC,$S_GROUND,$S_POTIONTAB,$S_SKILLTAB)
for($i=0;$i-lt$tabs.Count;$i++){
    if($i-lt4){
        SetAttr $tabs[$i] "Caption" $newTabCaps[$i]
        SetAttr $tabs[$i] "X" "$(15+($i*108))"
        SetAttr $tabs[$i] "Y" "$tabY"
        SetAttr $tabs[$i] "Width" "102";SetAttr $tabs[$i] "Height" "$tabH"
        SetAttr $tabs[$i] "MouseEvent" "Dummy";SetAttr $tabs[$i] "Activate" "1";SetAttr $tabs[$i] "Visible" "1"
    } else {
        SetAttr $tabs[$i] "X" "-2500";SetAttr $tabs[$i] "Y" "-2500";SetAttr $tabs[$i] "Caption" ""
    }
}

# Add a title using the same known-visible control style.
$titleY=[Math]::Max(0,$tabY-28)
CloneButton $doc $tabParent $template "AHV3_Title" 135 $titleY 240 22 $S_TITLE | Out-Null

# Move likely ranking list/grid controls off-screen in the clone.
foreach($n in @($clone.SelectNodes(".//*"))){
    if($n -eq $tabParent){continue}
    $tag=$n.Name;$nm=Attr $n "Name"
    if($tag -match "(?i)(List|Grid|Table|Tree|Scroll)" -or $nm -match "(?i)(Rank|List|Scroll|Grid)"){
        if($n -notin $tabs){SetAttr $n "X" "-3000";SetAttr $n "Y" "-3000"}
    }
}

$baseY=$tabY+$tabH+8
function AddRow([string]$prefix,[int]$y,[string]$left,[string]$right){
    CloneButton $doc $tabParent $template ($prefix+"_L") 15 $y 112 21 $left | Out-Null
    CloneButton $doc $tabParent $template ($prefix+"_V") 132 $y 150 21 $right | Out-Null
}
function AddRowR([string]$prefix,[int]$y,[string]$left,[string]$right){
    CloneButton $doc $tabParent $template ($prefix+"_L") 292 $y 115 21 $left | Out-Null
    CloneButton $doc $tabParent $template ($prefix+"_V") 412 $y 73 21 $right | Out-Null
}

AddRow "AHV3_Hunt" ($baseY+0) $S_HUNT $S_HUNTVAL
AddRow "AHV3_Potion" ($baseY+23) $S_POTION $S_POTIONVAL
AddRow "AHV3_PHP" ($baseY+46) $S_PHP $S_50
AddRow "AHV3_Return" ($baseY+69) $S_RET $S_20
AddRow "AHV3_Resume" ($baseY+92) $S_RESUME $S_90
AddRow "AHV3_AutoReturn" ($baseY+115) $S_AR $S_USE
AddRow "AHV3_AutoShop" ($baseY+138) $S_SHOP $S_USE

AddRowR "AHV3_Status" ($baseY+0) $S_STATUS $S_WAIT
AddRowR "AHV3_AtkUse" ($baseY+23) $S_ATK $S_USE
AddRowR "AHV3_BuffUse" ($baseY+46) $S_BUFF $S_USE
AddRowR "AHV3_DefUse" ($baseY+69) $S_DEF $S_USE
AddRowR "AHV3_AtkSkill" ($baseY+92) $S_ATKS $S_SELECT
AddRowR "AHV3_BuffSkill" ($baseY+115) $S_BUFFS $S_SELECT
AddRowR "AHV3_DefSkill" ($baseY+138) $S_DEFS $S_SELECT

$bottomY=$baseY+170
CloneButton $doc $tabParent $template "AHV3_Defaults" 15 $bottomY 100 24 $S_DEFAULTS | Out-Null
CloneButton $doc $tabParent $template "AHV3_Save" 120 $bottomY 90 24 $S_SAVE | Out-Null
CloneButton $doc $tabParent $template "AHV3_Start" 215 $bottomY 165 24 $S_START | Out-Null
CloneButton $doc $tabParent $template "AHV3_Stop" 385 $bottomY 100 24 $S_STOP | Out-Null

[void]$sourceWin.ParentNode.AppendChild($clone)

$newEnc=Enc (XmlBytes $doc)
$stamp=Get-Date -Format "yyyyMMdd_HHmmss"
$backup=Join-Path $ClientDir ("AUTOHUNT_STAGE1V3_BACKUP_"+$stamp)
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
"AUTOHUNT UI STAGE1 V3",
"source=first Window in HighRankUI.xml",
"tabY="+$tabY,
"tabCount="+$tabs.Count,
"backup="+$backup
)|Set-Content -Encoding ASCII (Join-Path $ClientDir "AUTOHUNT_STAGE1V3_RESULT.txt")

Ok "PATCH COMPLETE"
Ok "Open Character Ranking again."
Ok "V3 does not depend on Korean-caption matching."
