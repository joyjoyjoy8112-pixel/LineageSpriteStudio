param([string]$ClientDir = "D:\리니지1\리니지클라\클라")

$ErrorActionPreference = "Stop"
$TargetName = "AutoHuntSettingsUI.xml"

function Info([string]$s){ Write-Host "[AUTO-V3] $s" -ForegroundColor Cyan }
function Ok([string]$s){ Write-Host "[AUTO-V3] $s" -ForegroundColor Green }

function U32([byte[]]$b,[int]$o){ [BitConverter]::ToUInt32($b,$o) }
function PutU32([byte[]]$b,[int]$o,[uint32]$v){ [Array]::Copy([BitConverter]::GetBytes($v),0,$b,$o,4) }

function Entries([byte[]]$idx){
    if($idx.Length -lt 8 -or [Text.Encoding]::ASCII.GetString($idx,0,4) -ne "_EXT"){ throw "Unsupported UI.idx" }
    $count=U32 $idx 4
    $arr=@()
    for($i=0;$i-lt$count;$i++){
        $p=8+$i*128
        if($p+128 -gt $idx.Length){ break }
        $len=0
        for($j=0;$j-lt64;$j++){ if($idx[$p+16+$j]-eq0){break}; $len++ }
        $name=if($len){[Text.Encoding]::ASCII.GetString($idx,$p+16,$len)}else{""}
        $arr += [pscustomobject]@{Index=$i;Pos=$p;Name=$name;Offset=U32 $idx $p;Size=U32 $idx ($p+4)}
    }
    $arr
}

$key=[byte[]]@(0xdc,0x84,0x01,0x21,0x2a,0x40,0x20,0x0a,0xdd,0x25,0xb9,0xa7,0x0d,0xb9,0xc9,0x4e)
$seed=[byte[]]@(0x3e,0x09,0x78,0xaa,0xc4,0xd5,0x30,0x63,0x30,0x0c,0x5f,0x9a,0x80,0x7f,0x22,0x46)

function Pair([bool]$enc){
    $a=[Security.Cryptography.Aes]::Create()
    $a.Mode=[Security.Cryptography.CipherMode]::ECB
    $a.Padding=[Security.Cryptography.PaddingMode]::None
    $a.Key=$key
    if($enc){ @($a,$a.CreateEncryptor()) } else { @($a,$a.CreateDecryptor()) }
}
function Dec([byte[]]$enc){
    $plain=New-Object byte[] $enc.Length
    [Array]::Copy($enc,0,$plain,0,4); $plain[0]=0x3c
    $p=Pair $false; $a=$p[0]; $t=$p[1]
    try{
        $prev=[byte[]]$seed.Clone(); $pos=4
        while($pos+16 -le $enc.Length){
            $blk=New-Object byte[] 16; [Array]::Copy($enc,$pos,$blk,0,16)
            $tmp=New-Object byte[] 16; [void]$t.TransformBlock($blk,0,16,$tmp,0)
            for($j=0;$j-lt16;$j++){ $plain[$pos+$j]=$tmp[$j]-bxor$prev[$j] }
            $prev=$blk; $pos+=16
        }
        $j=0
        while($pos-lt$enc.Length){$plain[$pos]=$enc[$pos]-bxor$prev[$j];$pos++;$j++}
    } finally {$t.Dispose();$a.Dispose()}
    $plain
}
function Enc([byte[]]$plain){
    $enc=New-Object byte[] $plain.Length
    [Array]::Copy($plain,0,$enc,0,4); $enc[0]=0x58
    $p=Pair $true; $a=$p[0]; $t=$p[1]
    try{
        $prev=[byte[]]$seed.Clone(); $pos=4
        while($pos+16 -le $plain.Length){
            $x=New-Object byte[] 16
            for($j=0;$j-lt16;$j++){$x[$j]=$plain[$pos+$j]-bxor$prev[$j]}
            $o=New-Object byte[] 16; [void]$t.TransformBlock($x,0,16,$o,0)
            [Array]::Copy($o,0,$enc,$pos,16); $prev=$o; $pos+=16
        }
        $j=0
        while($pos-lt$plain.Length){$enc[$pos]=$plain[$pos]-bxor$prev[$j];$pos++;$j++}
    } finally {$t.Dispose();$a.Dispose()}
    $enc
}

function EntryBytes([byte[]]$pak,$e){
    $b=New-Object byte[] $e.Size
    [Array]::Copy($pak,[int]$e.Offset,$b,0,[int]$e.Size)
    $b
}
function ParseDoc([byte[]]$pak,$e){
    $raw=Dec (EntryBytes $pak $e)
    foreach($enc in @((New-Object Text.UTF8Encoding($false)),[Text.Encoding]::GetEncoding(949))){
        try{
            $d=New-Object Xml.XmlDocument
            $d.PreserveWhitespace=$true
            $d.LoadXml($enc.GetString($raw))
            return $d
        }catch{}
    }
    throw "Cannot parse $($e.Name)"
}
function SafeXmlBytes($doc){
    $xml=$doc.OuterXml
    $sb=New-Object Text.StringBuilder
    foreach($ch in $xml.ToCharArray()){
        $v=[int][char]$ch
        if($v-gt127){[void]$sb.Append("&#x"+$v.ToString("X")+";")}else{[void]$sb.Append($ch)}
    }
    (New-Object Text.UTF8Encoding($false)).GetBytes($sb.ToString())
}
function SetA($n,[string]$k,[string]$v){
    if($n.Attributes[$k]){$n.Attributes[$k].Value=$v}
    else{$a=$n.OwnerDocument.CreateAttribute($k);$a.Value=$v;[void]$n.Attributes.Append($a)}
}
function IntA($n,[string]$k,[int]$def=0){
    $x=$def
    if($n.Attributes[$k]){[void][int]::TryParse($n.Attributes[$k].Value,[ref]$x)}
    $x
}

if(Get-Process -Name "mjlin" -ErrorAction SilentlyContinue){ throw "Close mjlin before patching." }

$idxPath=Join-Path $ClientDir "UI.idx"
$pakPath=Join-Path $ClientDir "UI.pak"
if(-not(Test-Path $idxPath)-or-not(Test-Path $pakPath)){throw "UI.idx/UI.pak not found: $ClientDir"}

$idx=[IO.File]::ReadAllBytes($idxPath)
$pak=[IO.File]::ReadAllBytes($pakPath)
$entries=Entries $idx
$main=$entries|?{$_.Name-ieq"MainButtonUI.xml"}|select -First 1
$rank=$entries|?{$_.Name-ieq"HighRankUI.xml"}|select -First 1
if(-not$main -or -not$rank){throw "MainButtonUI.xml or HighRankUI.xml not found"}

$stamp=Get-Date -Format "yyyyMMdd_HHmmss"
$backup=Join-Path $ClientDir ("AUTOHUNT_STANDALONE_V3_BACKUP_"+$stamp)
New-Item -ItemType Directory -Force -Path $backup|Out-Null
Copy-Item $idxPath (Join-Path $backup "UI.idx")
Copy-Item $pakPath (Join-Path $backup "UI.pak")
Info "Backup: $backup"

# 1) Clean only our old injected test windows from MainButtonUI.
$mainDoc=ParseDoc $pak $main
$removed=0
foreach($n in @($mainDoc.SelectNodes("//*[@Name='AutoHuntSettingsWindow' or @Name='AutoHuntSettingsWindowV2']"))){
    if($n.ParentNode){[void]$n.ParentNode.RemoveChild($n);$removed++}
}
if($removed-gt0){
    $cleanEnc=Enc (SafeXmlBytes $mainDoc)
    $cleanOff=[uint32]$pak.Length
    $fs=[IO.File]::Open($pakPath,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$fs.Write($cleanEnc,0,$cleanEnc.Length)}finally{$fs.Dispose()}
    PutU32 $idx $main.Pos $cleanOff
    PutU32 $idx ($main.Pos+4) ([uint32]$cleanEnc.Length)
    PutU32 $idx ($main.Pos+8) 0
    PutU32 $idx ($main.Pos+12) 0
    $pak=[IO.File]::ReadAllBytes($pakPath)
    Ok "Removed old MainButtonUI test window(s): $removed"
}

# 2) Build a NEW independent XML document.
$rankDoc=ParseDoc $pak $rank
$source=$rankDoc.SelectSingleNode("//*[self::Window][1]")
if(-not$source){throw "No visual Window source found"}

$newDoc=New-Object Xml.XmlDocument
$newDoc.PreserveWhitespace=$false

# If this UI family uses a wrapper element, reproduce only the wrapper shell.
if($rankDoc.DocumentElement.Name -ne "Window"){
    $root=$newDoc.CreateElement($rankDoc.DocumentElement.Name)
    foreach($a in @($rankDoc.DocumentElement.Attributes)){
        $na=$newDoc.CreateAttribute($a.Name);$na.Value=$a.Value;[void]$root.Attributes.Append($na)
    }
    [void]$newDoc.AppendChild($root)
}else{$root=$null}

$win=$newDoc.CreateElement("Window")
SetA $win "Name" "AutoHuntSettingsWindow"
SetA $win "X" "145";SetA $win "Y" "125";SetA $win "Width" "510";SetA $win "Height" "330"
SetA $win "MouseEvent" "Dummy";SetA $win "MostTop" "1";SetA $win "Moveable" "12"
SetA $win "Activate" "1";SetA $win "Visible" "1";SetA $win "Transparent" "false"
if($root){[void]$root.AppendChild($win)}else{[void]$newDoc.AppendChild($win)}

# Reuse only existing frame/image assets as styling; no ranking controls or ranking behavior are copied.
foreach($n in @($source.ChildNodes)){
    if($n.NodeType -ne [Xml.XmlNodeType]::Element){continue}
    if($n.Name -match "(?i)^(Image|Sprite|Texture|Frame|StaticImage)$"){
        $c=$newDoc.ImportNode($n,$true)
        if($c.Attributes["Name"]){$c.Attributes["Name"].Value="AH_Frame_"+$c.Attributes["Name"].Value}
        [void]$win.AppendChild($c)
    }
}

# Borrow ONE native button style, then create every control ourselves.
$template=$source.SelectSingleNode(".//*[self::Button or self::CheckButton][1]")
if(-not$template){throw "No native button style found"}

function AddButton([string]$name,[int]$x,[int]$y,[int]$w,[int]$h,[string]$caption){
    $b=$newDoc.CreateElement($template.Name)
    foreach($a in @($template.Attributes)){
        $na=$newDoc.CreateAttribute($a.Name);$na.Value=$a.Value;[void]$b.Attributes.Append($na)
    }
    SetA $b "Name" $name;SetA $b "X" "$x";SetA $b "Y" "$y";SetA $b "Width" "$w";SetA $b "Height" "$h"
    SetA $b "Caption" $caption;SetA $b "MouseEvent" "Dummy";SetA $b "Activate" "1";SetA $b "Visible" "1"
    [void]$win.AppendChild($b)
    $b
}

AddButton "AH_Title" 135 8 240 24 "자동사냥 설정"|Out-Null
$tabs=@("기본 설정","사냥터 설정","물약 설정","스킬 설정")
for($i=0;$i-lt4;$i++){AddButton ("AH_Tab"+$i) (16+$i*118) 40 112 22 $tabs[$i]|Out-Null}

function Row([int]$y,[string]$l,[string]$v){
    AddButton ("AH_L_"+$y) 18 $y 138 21 $l|Out-Null
    AddButton ("AH_V_"+$y) 162 $y 150 21 $v|Out-Null
}
function RowR([int]$y,[string]$l,[string]$v){
    AddButton ("AH_RL_"+$y) 320 $y 110 21 $l|Out-Null
    AddButton ("AH_RV_"+$y) 435 $y 58 21 $v|Out-Null
}
Row 78  "사냥터"          "기란 감옥 1층"
Row 102 "사용할 물약"     "농축 체력 회복제"
Row 126 "물약 사용 HP"     "50% 이하"
Row 150 "귀환 HP"          "20% 이하"
Row 174 "사냥터 복귀 HP"   "90% 이상"
Row 198 "자동 귀환"        "사용"
Row 222 "자동 상점"        "사용"

RowR 78  "상태"            "대기"
RowR 102 "공격 스킬"       "사용"
RowR 126 "버프 스킬"       "사용"
RowR 150 "방어 스킬"       "사용"
RowR 174 "공격 선택"       "선택"
RowR 198 "버프 선택"       "선택"
RowR 222 "방어 선택"       "선택"

AddButton "AH_Default" 18 264 92 24 "기본값"|Out-Null
AddButton "AH_Save"    116 264 82 24 "저장"|Out-Null
AddButton "AH_Start"   204 264 178 24 "자동사냥 시작"|Out-Null
AddButton "AH_Stop"    388 264 105 24 "중지"|Out-Null

$plain=SafeXmlBytes $newDoc
$enc=Enc $plain

# Self validation of the compiled XML.
$verify=Dec $enc
$vd=New-Object Xml.XmlDocument
$vd.LoadXml([Text.Encoding]::UTF8.GetString($verify))
if(-not$vd.SelectSingleNode("//*[@Name='AutoHuntSettingsWindow']")){throw "Standalone XML validation failed"}

# 3) Append compiled AutoHuntSettingsUI.xml and register/update it in UI.idx.
$pakNow=[IO.File]::ReadAllBytes($pakPath)
$newOffset=[uint32]$pakNow.Length
$fs=[IO.File]::Open($pakPath,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$fs.Write($enc,0,$enc.Length)}finally{$fs.Dispose()}

$entries=Entries $idx
$existing=$entries|?{$_.Name-ieq$TargetName}|select -First 1
if($existing){
    PutU32 $idx $existing.Pos $newOffset
    PutU32 $idx ($existing.Pos+4) ([uint32]$enc.Length)
    PutU32 $idx ($existing.Pos+8) 0
    PutU32 $idx ($existing.Pos+12) 0
    Info "Updated existing index entry: $TargetName"
}else{
    $templateEntry=$entries|?{$_.Name-ieq"HighRankUI.xml"}|select -First 1
    $oldCount=[int](U32 $idx 4)
    $newIdx=New-Object byte[] ($idx.Length+128)
    [Array]::Copy($idx,0,$newIdx,0,$idx.Length)
    $pos=$idx.Length
    [Array]::Copy($idx,$templateEntry.Pos,$newIdx,$pos,128)
    for($i=16;$i-lt80;$i++){$newIdx[$pos+$i]=0}
    $nameBytes=[Text.Encoding]::ASCII.GetBytes($TargetName)
    [Array]::Copy($nameBytes,0,$newIdx,$pos+16,$nameBytes.Length)
    PutU32 $newIdx $pos $newOffset
    PutU32 $newIdx ($pos+4) ([uint32]$enc.Length)
    PutU32 $newIdx ($pos+8) 0
    PutU32 $newIdx ($pos+12) 0
    PutU32 $newIdx ($pos+92) ([uint32](47+$nameBytes.Length))
    PutU32 $newIdx ($pos+100) ([uint32](96+$nameBytes.Length*2))
    PutU32 $newIdx 4 ([uint32]($oldCount+1))
    $idx=$newIdx
    Info "Registered NEW UI file: $TargetName"
}

[IO.File]::WriteAllBytes($idxPath,$idx)

# Save source XML for inspection next to the backup, proving it is a separate UI file.
[IO.File]::WriteAllText((Join-Path $backup $TargetName),$newDoc.OuterXml,(New-Object Text.UTF8Encoding($false)))

Ok "PATCH COMPLETE"
Ok "Independent compiled file: $TargetName"
Ok "No AutoHunt window was inserted into MainButtonUI."
Ok "Enter world and check whether the client auto-loads the new UI file."
