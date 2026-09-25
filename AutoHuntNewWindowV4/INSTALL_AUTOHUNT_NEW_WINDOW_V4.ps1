param([string]$ConfigPath = "")

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
if(-not $ConfigPath){$ConfigPath=Join-Path $Root "config.json"}

function Info([string]$s){Write-Host "[AUTO-V4] $s" -ForegroundColor Cyan}
function Ok([string]$s){Write-Host "[AUTO-V4] $s" -ForegroundColor Green}
function Warn([string]$s){Write-Host "[AUTO-V4] $s" -ForegroundColor Yellow}

if(-not(Test-Path -LiteralPath $ConfigPath)){throw "config.json not found"}
$cfg=([IO.File]::ReadAllText($ConfigPath,[Text.Encoding]::UTF8)|ConvertFrom-Json)
$ClientDir=[string]$cfg.clientPath
$UiName=[string]$cfg.uiFileName
$BgName=[string]$cfg.backgroundImageName
$BgId=[string]$cfg.backgroundImageId
$LayoutPath=Join-Path $Root "AutoHuntSettingsUI.source.xml"

if(Get-Process -Name "mjlin" -ErrorAction SilentlyContinue){throw "Close the Lineage client before patching."}
if(-not(Test-Path -LiteralPath $ClientDir)){throw "Client path not found: $ClientDir"}
if(-not(Test-Path -LiteralPath $LayoutPath)){throw "AutoHuntSettingsUI.source.xml not found"}

$uiIdxPath=Join-Path $ClientDir "UI.idx"
$uiPakPath=Join-Path $ClientDir "UI.pak"
$imgIdxPath=Join-Path $ClientDir "Image00.idx"
$imgPakPath=Join-Path $ClientDir "Image00.pak"
foreach($p in @($uiIdxPath,$uiPakPath,$imgIdxPath,$imgPakPath)){if(-not(Test-Path -LiteralPath $p)){throw "Required file not found: $p"}}

function U32([byte[]]$b,[int]$o){[BitConverter]::ToUInt32($b,$o)}
function PutU32([byte[]]$b,[int]$o,[uint32]$v){[Array]::Copy([BitConverter]::GetBytes($v),0,$b,$o,4)}
function ParseIdx([byte[]]$idx){
    if($idx.Length-lt8 -or [Text.Encoding]::ASCII.GetString($idx,0,4)-ne"_EXT"){throw "Unsupported EXT index"}
    $count=[int](U32 $idx 4);$out=@()
    for($i=0;$i-lt$count;$i++){
        $p=8+$i*128;if($p+128-gt$idx.Length){throw "Index truncated"}
        $len=0;for($j=0;$j-lt64;$j++){if($idx[$p+16+$j]-eq0){break};$len++}
        $name=if($len){[Text.Encoding]::ASCII.GetString($idx,$p+16,$len)}else{""}
        $rec=New-Object byte[] 128;[Array]::Copy($idx,$p,$rec,0,128)
        $out += [pscustomobject]@{Name=$name;Pos=$p;Offset=U32 $idx $p;Size=U32 $idx ($p+4);Record=$rec}
    }
    $out
}
function MakeRecord([byte[]]$template,[string]$name,[uint32]$offset,[uint32]$size){
    $r=[byte[]]$template.Clone()
    for($i=16;$i-lt80;$i++){$r[$i]=0}
    $nb=[Text.Encoding]::ASCII.GetBytes($name)
    if($nb.Length-gt63){throw "Index name too long: $name"}
    [Array]::Copy($nb,0,$r,16,$nb.Length)
    PutU32 $r 0 $offset;PutU32 $r 4 $size;PutU32 $r 8 0;PutU32 $r 12 0
    PutU32 $r 92 ([uint32](47+$nb.Length))
    PutU32 $r 100 ([uint32](96+$nb.Length*2))
    $r
}
function UpsertIdx([byte[]]$idx,[string]$name,[uint32]$offset,[uint32]$size,[string]$templateName){
    $entries=@(ParseIdx $idx)
    $existing=$entries|Where-Object{$_.Name-ieq$name}|Select-Object -First 1
    if($existing){
        $out=[byte[]]$idx.Clone()
        PutU32 $out $existing.Pos $offset;PutU32 $out ($existing.Pos+4) $size;PutU32 $out ($existing.Pos+8) 0;PutU32 $out ($existing.Pos+12) 0
        return $out
    }
    $template=$entries|Where-Object{$_.Name-ieq$templateName}|Select-Object -First 1
    if(-not$template){$template=$entries|Select-Object -First 1}
    $items=New-Object Collections.ArrayList
    foreach($e in $entries){[void]$items.Add([pscustomobject]@{Name=$e.Name;Record=$e.Record})}
    [void]$items.Add([pscustomobject]@{Name=$name;Record=(MakeRecord $template.Record $name $offset $size)})
    $sorted=@($items|Sort-Object {$_.Name.ToLowerInvariant()})
    $out=New-Object byte[] (8+$sorted.Count*128)
    [Array]::Copy($idx,0,$out,0,8);PutU32 $out 4 ([uint32]$sorted.Count)
    for($i=0;$i-lt$sorted.Count;$i++){[Array]::Copy($sorted[$i].Record,0,$out,8+$i*128,128)}
    $out
}

$key=[byte[]]@(0xdc,0x84,0x01,0x21,0x2a,0x40,0x20,0x0a,0xdd,0x25,0xb9,0xa7,0x0d,0xb9,0xc9,0x4e)
$seed=[byte[]]@(0x3e,0x09,0x78,0xaa,0xc4,0xd5,0x30,0x63,0x30,0x0c,0x5f,0x9a,0x80,0x7f,0x22,0x46)
function Pair([bool]$enc){
    $a=[Security.Cryptography.Aes]::Create();$a.Mode=[Security.Cryptography.CipherMode]::ECB;$a.Padding=[Security.Cryptography.PaddingMode]::None;$a.Key=$key
    if($enc){@($a,$a.CreateEncryptor())}else{@($a,$a.CreateDecryptor())}
}
function Dec([byte[]]$enc){
    $p=New-Object byte[] $enc.Length;[Array]::Copy($enc,0,$p,0,4);$p[0]=0x3c
    $x=Pair $false;$a=$x[0];$t=$x[1]
    try{$prev=[byte[]]$seed.Clone();$pos=4;while($pos+16-le$enc.Length){$b=New-Object byte[] 16;[Array]::Copy($enc,$pos,$b,0,16);$z=New-Object byte[] 16;[void]$t.TransformBlock($b,0,16,$z,0);for($j=0;$j-lt16;$j++){$p[$pos+$j]=$z[$j]-bxor$prev[$j]};$prev=$b;$pos+=16};$j=0;while($pos-lt$enc.Length){$p[$pos]=$enc[$pos]-bxor$prev[$j];$pos++;$j++}}finally{$t.Dispose();$a.Dispose()}
    $p
}
function Enc([byte[]]$p){
    $e=New-Object byte[] $p.Length;[Array]::Copy($p,0,$e,0,4);$e[0]=0x58
    $x=Pair $true;$a=$x[0];$t=$x[1]
    try{$prev=[byte[]]$seed.Clone();$pos=4;while($pos+16-le$p.Length){$b=New-Object byte[] 16;for($j=0;$j-lt16;$j++){$b[$j]=$p[$pos+$j]-bxor$prev[$j]};$z=New-Object byte[] 16;[void]$t.TransformBlock($b,0,16,$z,0);[Array]::Copy($z,0,$e,$pos,16);$prev=$z;$pos+=16};$j=0;while($pos-lt$p.Length){$e[$pos]=$p[$pos]-bxor$prev[$j];$pos++;$j++}}finally{$t.Dispose();$a.Dispose()}
    $e
}
function GetEntryBytes([byte[]]$pak,$e){$b=New-Object byte[] $e.Size;[Array]::Copy($pak,[int]$e.Offset,$b,0,[int]$e.Size);$b}
function ParseUiDoc([byte[]]$pak,$e){
    $raw=Dec (GetEntryBytes $pak $e)
    foreach($enc in @((New-Object Text.UTF8Encoding($false)),[Text.Encoding]::GetEncoding(949))){
        try{$d=New-Object Xml.XmlDocument;$d.PreserveWhitespace=$true;$d.LoadXml($enc.GetString($raw));return $d}catch{}
    }
    throw "Cannot parse $($e.Name)"
}
function SafeXml([Xml.XmlDocument]$d){
    $s=$d.OuterXml;$b=New-Object Text.StringBuilder
    foreach($ch in $s.ToCharArray()){$v=[int][char]$ch;if($v-gt127){[void]$b.Append("&#x"+$v.ToString("X")+";")}else{[void]$b.Append($ch)}}
    (New-Object Text.UTF8Encoding($false)).GetBytes($b.ToString())
}
function SetA($n,[string]$k,[string]$v){if($n.Attributes[$k]){$n.Attributes[$k].Value=$v}else{$a=$n.OwnerDocument.CreateAttribute($k);$a.Value=$v;[void]$n.Attributes.Append($a)}}

$stamp=Get-Date -Format "yyyyMMdd_HHmmss"
$backup=Join-Path $ClientDir ("AUTOHUNT_V4_BACKUP_"+$stamp)
New-Item -ItemType Directory -Force -Path $backup|Out-Null
foreach($n in @("UI.idx","UI.pak","Image00.idx","Image00.pak")){Copy-Item -LiteralPath (Join-Path $ClientDir $n) -Destination (Join-Path $backup $n)}
Info "Backup created: $backup"

# Create our own background PNG.
Add-Type -AssemblyName System.Drawing
$pngPath=Join-Path $backup $BgName
$bmp=New-Object Drawing.Bitmap 470,320
$g=[Drawing.Graphics]::FromImage($bmp)
try{
    $g.Clear([Drawing.Color]::FromArgb(18,18,20))
    $outer=New-Object Drawing.Pen ([Drawing.Color]::FromArgb(176,139,72)),2
    $inner=New-Object Drawing.Pen ([Drawing.Color]::FromArgb(72,59,38)),1
    $line=New-Object Drawing.Pen ([Drawing.Color]::FromArgb(86,73,48)),1
    $head=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(34,31,27))
    $panel=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(24,24,27))
    $g.FillRectangle($panel,3,3,464,314)
    $g.FillRectangle($head,4,4,462,31)
    $g.DrawRectangle($outer,1,1,467,317)
    $g.DrawRectangle($inner,5,5,459,309)
    $g.DrawLine($line,10,67,460,67)
    $g.DrawLine($line,10,252,460,252)
    $g.DrawLine($line,373,70,373,238)
    $g.DrawRectangle($inner,12,72,358,168)
    $g.DrawRectangle($inner,375,72,81,168)
    $outer.Dispose();$inner.Dispose();$line.Dispose();$head.Dispose();$panel.Dispose()
}finally{$g.Dispose()}
$bmp.Save($pngPath,[Drawing.Imaging.ImageFormat]::Png);$bmp.Dispose()
$png=[IO.File]::ReadAllBytes($pngPath)

# Register/update custom PNG in Image00.
$imgIdx=[IO.File]::ReadAllBytes($imgIdxPath);$imgPak=[IO.File]::ReadAllBytes($imgPakPath)
$pngOffset=[uint32]$imgPak.Length
$f=[IO.File]::Open($imgPakPath,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$f.Write($png,0,$png.Length)}finally{$f.Dispose()}
$imgIdx=UpsertIdx $imgIdx $BgName $pngOffset ([uint32]$png.Length) "29999.png"
[IO.File]::WriteAllBytes($imgIdxPath,$imgIdx)
Ok "Custom background image registered: $BgName"

# Read existing UI style templates.
$uiIdx=[IO.File]::ReadAllBytes($uiIdxPath);$uiPak=[IO.File]::ReadAllBytes($uiPakPath)
$uiEntries=@(ParseIdx $uiIdx)
$rank=$uiEntries|Where-Object{$_.Name-ieq"HighRankUI.xml"}|Select-Object -First 1
$main=$uiEntries|Where-Object{$_.Name-ieq"MainButtonUI.xml"}|Select-Object -First 1
if(-not$rank -or -not$main){throw "HighRankUI.xml/MainButtonUI.xml not found"}
$rankDoc=ParseUiDoc $uiPak $rank
$mainDoc=ParseUiDoc $uiPak $main
$nativeWindow=$rankDoc.SelectSingleNode("//*[self::Window][1]")
$nativeButton=$rankDoc.SelectSingleNode("//*[self::Button or self::CheckButton][1]")
if(-not$nativeWindow -or -not$nativeButton){throw "Native UI style templates not found"}

# Clean only our older injected test windows from MainButtonUI.
$removed=0
foreach($n in @($mainDoc.SelectNodes("//*[@Name='AutoHuntSettingsWindow' or @Name='AutoHuntSettingsWindowV2']"))){if($n.ParentNode){[void]$n.ParentNode.RemoveChild($n);$removed++}}
if($removed-gt0){
    $clean=Enc (SafeXml $mainDoc);$off=[uint32]$uiPak.Length
    $f=[IO.File]::Open($uiPakPath,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$f.Write($clean,0,$clean.Length)}finally{$f.Dispose()}
    $uiIdx=UpsertIdx $uiIdx "MainButtonUI.xml" $off ([uint32]$clean.Length) "MainButtonUI.xml"
    [IO.File]::WriteAllBytes($uiIdxPath,$uiIdx)
    $uiPak=[IO.File]::ReadAllBytes($uiPakPath)
    Ok "Old injected MainButtonUI test window removed."
}

# Load OUR source layout.
$src=New-Object Xml.XmlDocument
$src.PreserveWhitespace=$false
$src.LoadXml([IO.File]::ReadAllText($LayoutPath,[Text.Encoding]::UTF8))
$srcWin=$src.DocumentElement
if($srcWin.Name-ne"Window"){throw "Source root must be Window"}

# Build a brand-new client UI document using our layout.
$out=New-Object Xml.XmlDocument
$out.PreserveWhitespace=$false
if($rankDoc.DocumentElement.Name-ne"Window"){
    $root=$out.CreateElement($rankDoc.DocumentElement.Name)
    foreach($a in @($rankDoc.DocumentElement.Attributes)){$na=$out.CreateAttribute($a.Name);$na.Value=$a.Value;[void]$root.Attributes.Append($na)}
    [void]$out.AppendChild($root)
}else{$root=$null}

$w=$out.CreateElement("Window")
foreach($a in @($nativeWindow.Attributes)){$na=$out.CreateAttribute($a.Name);$na.Value=$a.Value;[void]$w.Attributes.Append($na)}
foreach($a in @($srcWin.Attributes)){SetA $w $a.Name $a.Value}
if($root){[void]$root.AppendChild($w)}else{[void]$out.AppendChild($w)}

foreach($s in @($srcWin.ChildNodes)){
    if($s.NodeType-ne[Xml.XmlNodeType]::Element){continue}
    $b=$out.CreateElement($nativeButton.Name)
    foreach($a in @($nativeButton.Attributes)){$na=$out.CreateAttribute($a.Name);$na.Value=$a.Value;[void]$b.Attributes.Append($na)}
    foreach($a in @($s.Attributes)){SetA $b $a.Name $a.Value}
    SetA $b "Activate" "1";SetA $b "Visible" "1"
    if($s.Attributes["Name"].Value-eq"AH_Background"){
        SetA $b "BasePng" ($BgId+";1");SetA $b "PushPng" ($BgId+";1");SetA $b "HighlightPng" ($BgId+";1")
    }
    [void]$w.AppendChild($b)
}

$plain=SafeXml $out
$compiled=Enc $plain
$verify=Dec $compiled
$vd=New-Object Xml.XmlDocument;$vd.LoadXml([Text.Encoding]::UTF8.GetString($verify))
if(-not$vd.SelectSingleNode("//*[@Name='AutoHuntSettingsWindow']")){throw "Compiled AutoHuntSettingsUI validation failed"}

# Register the independent compiled UI file.
$uiPak=[IO.File]::ReadAllBytes($uiPakPath);$off=[uint32]$uiPak.Length
$f=[IO.File]::Open($uiPakPath,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$f.Write($compiled,0,$compiled.Length)}finally{$f.Dispose()}
$uiIdx=[IO.File]::ReadAllBytes($uiIdxPath)
$uiIdx=UpsertIdx $uiIdx $UiName $off ([uint32]$compiled.Length) "HighRankUI.xml"
[IO.File]::WriteAllBytes($uiIdxPath,$uiIdx)

# Save the exact generated source for inspection.
[IO.File]::WriteAllText((Join-Path $backup "AutoHuntSettingsUI.generated.xml"),$out.OuterXml,(New-Object Text.UTF8Encoding($true)))

Ok "PATCH COMPLETE"
Ok "New independent UI file: $UiName"
Ok "New custom UI background: $BgName"
Ok "MainButtonUI does not contain the new AutoHunt window."
Ok "This build is the window/design load test; button actions are intentionally not connected yet."
