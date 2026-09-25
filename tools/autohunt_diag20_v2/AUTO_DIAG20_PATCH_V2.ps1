param([string]$ClientDir = "")

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "AUTO DIAG20 V2"

function Info([string]$s) { Write-Host "[AUTO20] $s" -ForegroundColor Cyan }
function Ok([string]$s)   { Write-Host "[AUTO20] $s" -ForegroundColor Green }
function Warn([string]$s) { Write-Host "[AUTO20] $s" -ForegroundColor Yellow }

function Resolve-ClientDir([string]$Requested) {
    $candidates = @()
    if ($Requested) { $candidates += $Requested }
    $candidates += $PSScriptRoot
    $candidates += (Get-Location).Path
    foreach ($c in ($candidates | Select-Object -Unique)) {
        if ($c -and
            (Test-Path -LiteralPath (Join-Path $c "UI.idx")) -and
            (Test-Path -LiteralPath (Join-Path $c "UI.pak"))) {
            return (Resolve-Path -LiteralPath $c).Path
        }
    }

    Add-Type -AssemblyName System.Windows.Forms
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Select the Lineage client folder containing UI.idx and UI.pak."
    $dlg.ShowNewFolderButton = $false
    if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        throw "Folder selection cancelled."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $dlg.SelectedPath "UI.idx")) -or
        -not (Test-Path -LiteralPath (Join-Path $dlg.SelectedPath "UI.pak"))) {
        throw "UI.idx/UI.pak not found in selected folder."
    }
    return $dlg.SelectedPath
}

function Write-U32([byte[]]$b,[int]$o,[uint32]$v) {
    $x=[BitConverter]::GetBytes($v)
    [Array]::Copy($x,0,$b,$o,4)
}

function Get-Entries([byte[]]$idx) {
    if ($idx.Length -lt 8) { throw "UI.idx too small." }
    if ([Text.Encoding]::ASCII.GetString($idx,0,4) -ne "_EXT") { throw "Unsupported UI.idx format." }
    $count=[BitConverter]::ToUInt32($idx,4)
    $out=@()
    for($i=0;$i-lt$count;$i++){
        $r=8+($i*128)
        if($r+128-gt$idx.Length){break}
        $len=0
        for($j=0;$j-lt112;$j++){ if($idx[$r+16+$j]-eq0){break}; $len++ }
        $name=if($len-gt0){[Text.Encoding]::ASCII.GetString($idx,$r+16,$len)}else{""}
        $out += [PSCustomObject]@{
            Index=$i; RecordOffset=$r;
            Offset=[BitConverter]::ToUInt32($idx,$r);
            Size=[BitConverter]::ToUInt32($idx,$r+4);
            Packed=[BitConverter]::ToUInt32($idx,$r+8);
            Flags=[BitConverter]::ToUInt32($idx,$r+12);
            Name=$name
        }
    }
    return $out
}

$key  = [byte[]]@(0xdc,0x84,0x01,0x21,0x2a,0x40,0x20,0x0a,0xdd,0x25,0xb9,0xa7,0x0d,0xb9,0xc9,0x4e)
$seed = [byte[]]@(0x3e,0x09,0x78,0xaa,0xc4,0xd5,0x30,0x63,0x30,0x0c,0x5f,0x9a,0x80,0x7f,0x22,0x46)

function New-AesPair([bool]$encrypt) {
    $aes=[Security.Cryptography.Aes]::Create()
    $aes.Mode=[Security.Cryptography.CipherMode]::ECB
    $aes.Padding=[Security.Cryptography.PaddingMode]::None
    $aes.KeySize=128; $aes.BlockSize=128; $aes.Key=$key
    if($encrypt){ return @($aes,$aes.CreateEncryptor()) }
    return @($aes,$aes.CreateDecryptor())
}

function Decrypt-Ui([byte[]]$enc) {
    $plain=New-Object byte[] $enc.Length
    [Array]::Copy($enc,0,$plain,0,4)
    $plain[0]=0x3c
    $p=New-AesPair $false; $aes=$p[0]; $tr=$p[1]
    try {
        $prev=[byte[]]$seed.Clone(); $pos=4
        while(($pos+16)-le$enc.Length){
            $block=New-Object byte[] 16
            [Array]::Copy($enc,$pos,$block,0,16)
            $tmp=New-Object byte[] 16
            [void]$tr.TransformBlock($block,0,16,$tmp,0)
            for($j=0;$j-lt16;$j++){ $plain[$pos+$j]=$tmp[$j]-bxor$prev[$j] }
            $prev=$block; $pos+=16
        }
        $j=0
        while($pos-lt$enc.Length){ $plain[$pos]=$enc[$pos]-bxor$prev[$j]; $pos++; $j++ }
    } finally { $tr.Dispose(); $aes.Dispose() }
    return $plain
}

function Encrypt-Ui([byte[]]$plain) {
    $enc=New-Object byte[] $plain.Length
    [Array]::Copy($plain,0,$enc,0,4)
    $enc[0]=0x58
    $p=New-AesPair $true; $aes=$p[0]; $tr=$p[1]
    try {
        $prev=[byte[]]$seed.Clone(); $pos=4
        while(($pos+16)-le$plain.Length){
            $x=New-Object byte[] 16
            for($j=0;$j-lt16;$j++){ $x[$j]=$plain[$pos+$j]-bxor$prev[$j] }
            $out=New-Object byte[] 16
            [void]$tr.TransformBlock($x,0,16,$out,0)
            [Array]::Copy($out,0,$enc,$pos,16)
            $prev=$out; $pos+=16
        }
        $j=0
        while($pos-lt$plain.Length){ $enc[$pos]=$plain[$pos]-bxor$prev[$j]; $pos++; $j++ }
    } finally { $tr.Dispose(); $aes.Dispose() }
    return $enc
}

function Entry-Bytes([byte[]]$pak,$e) {
    if(($e.Offset+$e.Size)-gt$pak.Length){ throw "Entry outside UI.pak: $($e.Name)" }
    $b=New-Object byte[] $e.Size
    [Array]::Copy($pak,[int]$e.Offset,$b,0,[int]$e.Size)
    return $b
}

function Parse-Entry([byte[]]$pak,$e) {
    try {
        $plain=Decrypt-Ui (Entry-Bytes $pak $e)
        $txt=[Text.Encoding]::UTF8.GetString($plain)
        $doc=New-Object Xml.XmlDocument
        $doc.PreserveWhitespace=$true
        $doc.LoadXml($txt)
        return $doc
    } catch { return $null }
}

function Attr($n,[string]$name,[string]$def="") {
    if($n.Attributes[$name]){return $n.Attributes[$name].Value}
    return $def
}
function SetAttr($n,[string]$name,[string]$value) {
    if($n.Attributes[$name]){$n.Attributes[$name].Value=$value;return}
    $a=$n.OwnerDocument.CreateAttribute($name);$a.Value=$value;[void]$n.Attributes.Append($a)
}

function XmlBytes($doc) {
    $ms=New-Object IO.MemoryStream
    $settings=New-Object Xml.XmlWriterSettings
    $settings.Encoding=New-Object Text.UTF8Encoding($false)
    $settings.Indent=$false
    $settings.OmitXmlDeclaration=$false
    $settings.NewLineHandling=[Xml.NewLineHandling]::None
    $w=[Xml.XmlWriter]::Create($ms,$settings)
    try{$doc.Save($w);$w.Flush();return $ms.ToArray()}finally{$w.Dispose();$ms.Dispose()}
}

function Get-TestNumber($b) {
    $cap=Attr $b "Caption"
    if($cap -match "^\s*(?:TEST\s*)?(20|1[0-9]|[1-9])\s*$"){return [int]$Matches[1]}
    $tip=$b.SelectSingleNode("./Tooltip")
    if($tip){
        $tc=Attr $tip "Caption"
        if($tc -match "(?i)^\s*TEST\s*(20|1[0-9]|[1-9])\s*$"){return [int]$Matches[1]}
    }
    $nm=Attr $b "Name"
    if($nm -match "(?i)(20|1[0-9]|0?[1-9])(?:\D*$)"){return [int]$Matches[1]}
    return 0
}

function Find-TextTemplate($entries,[byte[]]$pak) {
    $preferred=@("MainButtonUI.xml","MainCharInfoUI.xml","ChatUI.xml","QuickMotionUI.xml","ActionUI.xml","ReNStatusUIEx.xml","OptionUI.xml")
    $best=$null;$bestScore=-1
    foreach($file in $preferred){
        $e=$entries|Where-Object{$_.Name-ieq$file}|Select-Object -First 1
        if(-not$e){continue}
        $d=Parse-Entry $pak $e
        if(-not$d){continue}
        foreach($n in $d.SelectNodes("//*[@Caption]")){
            $nm=Attr $n "Name"
            if($nm -match "^Diag20Label\d+$"){continue}
            if($n.Name -match "^Diag20Label\d+$"){continue}
            if($n.Name -match "^(Tooltip|Button|CheckButton|Window|Control)$"){continue}
            $score=0
            if($n.Name -match "(?i)(Text|Label|Static|String|Caption)"){$score+=25}
            if($n.Attributes["X"]-and$n.Attributes["Y"]){$score+=8}
            if($n.Attributes["Width"]-and$n.Attributes["Height"]){$score+=4}
            if($n.Attributes["Font"]-or$n.Attributes["FontSize"]-or$n.Attributes["Color"]){$score+=5}
            if($score-gt$bestScore){$best=$n;$bestScore=$score}
        }
        if($bestScore-ge30){break}
    }
    return $best
}

$ClientDir=Resolve-ClientDir $ClientDir
$idxPath=Join-Path $ClientDir "UI.idx"
$pakPath=Join-Path $ClientDir "UI.pak"
Info "Client: $ClientDir"
Info "Reading UI.idx/UI.pak"

$idx=[IO.File]::ReadAllBytes($idxPath)
$pak=[IO.File]::ReadAllBytes($pakPath)
$entries=Get-Entries $idx
$main=$entries|Where-Object{$_.Name-ieq"MainButtonUI.xml"}|Select-Object -First 1
if(-not$main){throw "MainButtonUI.xml not found."}
$doc=Parse-Entry $pak $main
if(-not$doc){throw "MainButtonUI.xml decrypt/parse failed."}

# Remove labels from V1/V2 previous attempts.
$remove=@()
foreach($n in @($doc.SelectNodes("//*"))){
    if((Attr $n "Name") -match "^Diag20Label\d+$" -or $n.Name -match "^Diag20Label\d+$"){
        $remove += $n
    }
}
foreach($n in $remove){if($n.ParentNode){[void]$n.ParentNode.RemoveChild($n)}}

$buttons=@($doc.SelectNodes("//*[self::Button or self::CheckButton]"))
$mapped=@{}
foreach($b in $buttons){
    $n=Get-TestNumber $b
    if($n-ge1-and$n-le20-and-not$mapped.ContainsKey($n)){$mapped[$n]=$b}
}

if($mapped.Count-lt20){
    $cand=@();$ord=0
    foreach($b in $buttons){
        $ord++
        $base=Attr $b "BasePng";$id=-1
        if($base-match "^\s*(\d+)"){$id=[int]$Matches[1]}
        $name=Attr $b "Name"
        $pn=if($b.ParentNode-and$b.ParentNode.Attributes["Name"]){$b.ParentNode.Attributes["Name"].Value}else{""}
        $score=0
        if($id-ge30000){$score+=10}
        if($id-eq29999){$score+=8}
        if($name-match"(?i)(diag|test|autohunt.*(?:test|diag)|hud.*test)"){$score+=20}
        if($pn-match"(?i)(diag|test)"){$score+=10}
        if($score-ge10){$cand += [PSCustomObject]@{Node=$b;Order=$ord;Score=$score}}
    }
    $used=New-Object Collections.Generic.HashSet[object]
    foreach($k in $mapped.Keys){[void]$used.Add($mapped[$k])}
    $remaining=@($cand|Where-Object{-not$used.Contains($_.Node)}|Sort-Object @{Expression="Score";Descending=$true},Order)
    for($n=1;$n-le20;$n++){
        if(-not$mapped.ContainsKey($n)){
            if($remaining.Count-eq0){break}
            $mapped[$n]=$remaining[0].Node
            if($remaining.Count-gt1){$remaining=@($remaining[1..($remaining.Count-1)])}else{$remaining=@()}
        }
    }
}

if($mapped.Count-lt20){
    $msg="Could not identify all 20 diagnostic buttons. Found $($mapped.Count). Apply the original AUTO 1-20 diagnostic pack first."
    [IO.File]::WriteAllText((Join-Path $ClientDir "AUTO_DIAG20_ERROR.txt"),$msg,[Text.Encoding]::ASCII)
    throw $msg
}

$template=Find-TextTemplate $entries $pak
if($template){Info ("Text template: <"+$template.Name+">")}else{Warn "No text template found. Using <Text>."}

$lines=New-Object Collections.Generic.List[string]
$lines.Add("AUTO DIAG20 V2")
$lines.Add("Client="+$ClientDir)
$lines.Add("Time="+(Get-Date -Format "yyyy-MM-dd HH:mm:ss"))
$lines.Add("")

for($num=1;$num-le20;$num++){
    $b=$mapped[$num]
    $old=Attr $b "BasePng"
    $event=Attr $b "MouseEvent"
    $bx=0;$by=0;$bw=27;$bh=27
    [void][int]::TryParse((Attr $b "X" "0"),[ref]$bx)
    [void][int]::TryParse((Attr $b "Y" "0"),[ref]$by)
    [void][int]::TryParse((Attr $b "Width" "27"),[ref]$bw)
    [void][int]::TryParse((Attr $b "Height" "27"),[ref]$bh)
    if($bw-le0){$bw=27};if($bh-le0){$bh=27}

    SetAttr $b "BasePng" "29999;1"
    SetAttr $b "PushPng" "29999;1"
    SetAttr $b "HighlightPng" "29999;1"
    SetAttr $b "Caption" "$num"

    $tip=$b.SelectSingleNode("./Tooltip")
    if(-not$tip){$tip=$doc.CreateElement("Tooltip");[void]$b.AppendChild($tip)}
    SetAttr $tip "X" "0";SetAttr $tip "Y" "0";SetAttr $tip "Width" "$bw";SetAttr $tip "Height" "$bh";SetAttr $tip "Caption" "TEST $num"

    if($template){$label=$doc.ImportNode($template,$true)}else{$label=$doc.CreateElement("Text")}
    SetAttr $label "Name" ("Diag20Label{0:D2}" -f $num)
    SetAttr $label "X" "$bx"
    SetAttr $label "Y" "$($by+$bh+1)"
    SetAttr $label "Width" "$([Math]::Max($bw,28))"
    SetAttr $label "Height" "13"
    SetAttr $label "Caption" "$num"
    if($label.Attributes["Text"]){$label.Attributes["Text"].Value="$num"}
    if($label.Attributes["Value"]){$label.Attributes["Value"].Value="$num"}
    if($label.Attributes["Activate"]){$label.Attributes["Activate"].Value="1"}
    if($label.Attributes["Visible"]){$label.Attributes["Visible"].Value="1"}
    if($label.Attributes["MostTop"]){$label.Attributes["MostTop"].Value="1"}

    [void]$b.ParentNode.InsertAfter($label,$b)

    if($b.ParentNode-and$b.ParentNode.Attributes["Height"]){
        $ph=0
        if([int]::TryParse($b.ParentNode.Attributes["Height"].Value,[ref]$ph)){
            $need=$by+$bh+15
            if($ph-lt$need-and$ph-lt120){$b.ParentNode.Attributes["Height"].Value="$need"}
        }
    }

    $parentName=if($b.ParentNode-and$b.ParentNode.Attributes["Name"]){$b.ParentNode.Attributes["Name"].Value}else{""}
    $lines.Add(("{0,2}: name={1} parent={2} oldpng={3} event={4}" -f $num,(Attr $b "Name"),$parentName,$old,$event))
}

$xml=XmlBytes $doc
$newEnc=Encrypt-Ui $xml

$stamp=Get-Date -Format "yyyyMMdd_HHmmss"
$backup=Join-Path $ClientDir ("AUTO_DIAG20_BACKUP_"+$stamp)
New-Item -ItemType Directory -Path $backup -Force|Out-Null
Copy-Item -LiteralPath $idxPath -Destination (Join-Path $backup "UI.idx")
Copy-Item -LiteralPath $pakPath -Destination (Join-Path $backup "UI.pak")

$newOff=[uint32]$pak.Length
$newSize=[uint32]$newEnc.Length
$fs=[IO.File]::Open($pakPath,[IO.FileMode]::Append,[IO.FileAccess]::Write,[IO.FileShare]::None)
try{$fs.Write($newEnc,0,$newEnc.Length);$fs.Flush()}finally{$fs.Dispose()}

Write-U32 $idx $main.RecordOffset $newOff
Write-U32 $idx ($main.RecordOffset+4) $newSize
Write-U32 $idx ($main.RecordOffset+8) 0
Write-U32 $idx ($main.RecordOffset+12) 0
[IO.File]::WriteAllBytes($idxPath,$idx)

$lines.Add("")
$lines.Add("MainButtonUI offset=$newOff size=$newSize")
$lines.Add("Backup="+$backup)
$report=Join-Path $ClientDir "AUTO_DIAG20_RESULT.txt"
[IO.File]::WriteAllLines($report,$lines,[Text.Encoding]::ASCII)

Ok "PATCH COMPLETE"
Ok "All 20 test buttons use image 29999."
Ok "Labels 1-20 were added below the buttons."
Ok "Backup: $backup"
Ok "Report: $report"
Write-Host ""
Write-Host "Start the game and report which numbers are visible and clickable." -ForegroundColor Yellow
