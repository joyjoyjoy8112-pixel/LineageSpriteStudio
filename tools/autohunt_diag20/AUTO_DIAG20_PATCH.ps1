param(
    [string]$ClientDir = ""
)

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "AUTO 1~20 진단 패처"

function Write-Info([string]$s) { Write-Host "[AUTO20] $s" -ForegroundColor Cyan }
function Write-Ok([string]$s)   { Write-Host "[AUTO20] $s" -ForegroundColor Green }
function Write-Warn([string]$s) { Write-Host "[AUTO20] $s" -ForegroundColor Yellow }

function Resolve-ClientDir {
    param([string]$Requested)
    $candidates = @()
    if ($Requested) { $candidates += $Requested }
    $candidates += $PSScriptRoot
    $candidates += (Get-Location).Path

    foreach ($c in $candidates | Select-Object -Unique) {
        if (-not $c) { continue }
        $idx = Join-Path $c "UI.idx"
        $pak = Join-Path $c "UI.pak"
        if ((Test-Path -LiteralPath $idx) -and (Test-Path -LiteralPath $pak)) {
            return (Resolve-Path -LiteralPath $c).Path
        }
    }

    Add-Type -AssemblyName System.Windows.Forms
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "UI.idx / UI.pak 이 있는 리니지 클라이언트 폴더를 선택하세요."
    $dlg.ShowNewFolderButton = $false
    if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        throw "클라이언트 폴더 선택이 취소되었습니다."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $dlg.SelectedPath "UI.idx")) -or
        -not (Test-Path -LiteralPath (Join-Path $dlg.SelectedPath "UI.pak"))) {
        throw "선택한 폴더에 UI.idx / UI.pak 이 없습니다."
    }
    return $dlg.SelectedPath
}

function Read-UInt32LE([byte[]]$bytes, [int]$offset) {
    return [BitConverter]::ToUInt32($bytes, $offset)
}

function Write-UInt32LE([byte[]]$bytes, [int]$offset, [uint32]$value) {
    $v = [BitConverter]::GetBytes($value)
    [Array]::Copy($v, 0, $bytes, $offset, 4)
}

function Get-UiEntries([byte[]]$idxBytes) {
    if ($idxBytes.Length -lt 8) { throw "UI.idx 파일이 너무 작습니다." }
    $sig = [Text.Encoding]::ASCII.GetString($idxBytes, 0, 4)
    if ($sig -ne "_EXT") { throw "지원하지 않는 UI.idx 형식입니다. 시그니처=$sig" }

    $count = [BitConverter]::ToUInt32($idxBytes, 4)
    $entries = @()
    for ($i = 0; $i -lt $count; $i++) {
        $r = 8 + ($i * 128)
        if ($r + 128 -gt $idxBytes.Length) { break }

        $nameLen = 0
        for ($j = 0; $j -lt 112; $j++) {
            if ($idxBytes[$r + 16 + $j] -eq 0) { break }
            $nameLen++
        }
        $name = if ($nameLen -gt 0) { [Text.Encoding]::ASCII.GetString($idxBytes, $r + 16, $nameLen) } else { "" }
        $entries += [PSCustomObject]@{
            Index = $i
            RecordOffset = $r
            Offset = [BitConverter]::ToUInt32($idxBytes, $r)
            Size = [BitConverter]::ToUInt32($idxBytes, $r + 4)
            Packed = [BitConverter]::ToUInt32($idxBytes, $r + 8)
            Flags = [BitConverter]::ToUInt32($idxBytes, $r + 12)
            Name = $name
        }
    }
    return $entries
}

$key = [byte[]]@(0xdc,0x84,0x01,0x21,0x2a,0x40,0x20,0x0a,0xdd,0x25,0xb9,0xa7,0x0d,0xb9,0xc9,0x4e)
$seed = [byte[]]@(0x3e,0x09,0x78,0xaa,0xc4,0xd5,0x30,0x63,0x30,0x0c,0x5f,0x9a,0x80,0x7f,0x22,0x46)

function New-Aes([bool]$encrypt) {
    $aes = [System.Security.Cryptography.Aes]::Create()
    $aes.Mode = [System.Security.Cryptography.CipherMode]::ECB
    $aes.Padding = [System.Security.Cryptography.PaddingMode]::None
    $aes.KeySize = 128
    $aes.BlockSize = 128
    $aes.Key = $key
    if ($encrypt) { return @($aes, $aes.CreateEncryptor()) }
    return @($aes, $aes.CreateDecryptor())
}

function Decrypt-UiXml([byte[]]$enc) {
    if ($enc.Length -lt 4) { throw "암호화 UI 데이터가 너무 작습니다." }
    $plain = New-Object byte[] $enc.Length
    [Array]::Copy($enc, 0, $plain, 0, 4)
    $plain[0] = 0x3c

    $pair = New-Aes $false
    $aes = $pair[0]
    $dec = $pair[1]
    try {
        $prev = [byte[]]$seed.Clone()
        $pos = 4
        while (($pos + 16) -le $enc.Length) {
            $block = New-Object byte[] 16
            [Array]::Copy($enc, $pos, $block, 0, 16)
            $tmp = New-Object byte[] 16
            [void]$dec.TransformBlock($block, 0, 16, $tmp, 0)
            for ($j = 0; $j -lt 16; $j++) {
                $plain[$pos + $j] = $tmp[$j] -bxor $prev[$j]
            }
            $prev = $block
            $pos += 16
        }
        $j = 0
        while ($pos -lt $enc.Length) {
            $plain[$pos] = $enc[$pos] -bxor $prev[$j]
            $pos++; $j++
        }
    }
    finally {
        $dec.Dispose()
        $aes.Dispose()
    }
    return $plain
}

function Encrypt-UiXml([byte[]]$plain) {
    if ($plain.Length -lt 4) { throw "XML 데이터가 너무 작습니다." }
    $enc = New-Object byte[] $plain.Length
    [Array]::Copy($plain, 0, $enc, 0, 4)
    $enc[0] = 0x58

    $pair = New-Aes $true
    $aes = $pair[0]
    $encTransform = $pair[1]
    try {
        $prev = [byte[]]$seed.Clone()
        $pos = 4
        while (($pos + 16) -le $plain.Length) {
            $x = New-Object byte[] 16
            for ($j = 0; $j -lt 16; $j++) {
                $x[$j] = $plain[$pos + $j] -bxor $prev[$j]
            }
            $out = New-Object byte[] 16
            [void]$encTransform.TransformBlock($x, 0, 16, $out, 0)
            [Array]::Copy($out, 0, $enc, $pos, 16)
            $prev = $out
            $pos += 16
        }
        $j = 0
        while ($pos -lt $plain.Length) {
            $enc[$pos] = $plain[$pos] -bxor $prev[$j]
            $pos++; $j++
        }
    }
    finally {
        $encTransform.Dispose()
        $aes.Dispose()
    }
    return $enc
}

function Get-EntryBytes([byte[]]$pakBytes, $entry) {
    if (($entry.Offset + $entry.Size) -gt $pakBytes.Length) {
        throw "$($entry.Name) 범위가 UI.pak 크기를 벗어납니다."
    }
    $buf = New-Object byte[] $entry.Size
    [Array]::Copy($pakBytes, [int]$entry.Offset, $buf, 0, [int]$entry.Size)
    return $buf
}

function Try-ParseXmlFromEntry([byte[]]$pakBytes, $entry) {
    try {
        $enc = Get-EntryBytes $pakBytes $entry
        $plain = Decrypt-UiXml $enc
        $text = [Text.Encoding]::UTF8.GetString($plain)
        $doc = New-Object System.Xml.XmlDocument
        $doc.PreserveWhitespace = $true
        $doc.LoadXml($text)
        return $doc
    } catch {
        return $null
    }
}

function Get-Attr($node, [string]$name, [string]$default="") {
    if ($node.Attributes[$name]) { return $node.Attributes[$name].Value }
    return $default
}

function Set-Attr($node, [string]$name, [string]$value) {
    if ($node.Attributes[$name]) {
        $node.Attributes[$name].Value = $value
    } else {
        $a = $node.OwnerDocument.CreateAttribute($name)
        $a.Value = $value
        [void]$node.Attributes.Append($a)
    }
}

function Find-TextTemplate($entries, [byte[]]$pakBytes) {
    $preferred = @("MainButtonUI.xml","MainCharInfoUI.xml","ChatUI.xml","QuickMotionUI.xml","ActionUI.xml","ReNStatusUIEx.xml")
    $best = $null
    $bestScore = -1

    foreach ($file in $preferred) {
        $e = $entries | Where-Object { $_.Name -ieq $file } | Select-Object -First 1
        if (-not $e) { continue }
        $d = Try-ParseXmlFromEntry $pakBytes $e
        if (-not $d) { continue }

        foreach ($n in $d.SelectNodes("//*[@Caption]")) {
            $tag = $n.Name
            if ($tag -match "^(Tooltip|Button|CheckButton|Window|Control)$") { continue }
            $score = 0
            if ($tag -match "(Text|Label|Static|String|Caption)") { $score += 20 }
            if ($n.Attributes["X"] -and $n.Attributes["Y"]) { $score += 8 }
            if ($n.Attributes["Width"] -and $n.Attributes["Height"]) { $score += 4 }
            if ($n.Attributes["Name"]) { $score += 2 }
            if ($score -gt $bestScore) {
                $best = $n
                $bestScore = $score
            }
        }
        if ($bestScore -ge 30) { break }
    }
    return $best
}

function To-XmlBytes($doc) {
    $ms = New-Object System.IO.MemoryStream
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.Indent = $false
    $settings.OmitXmlDeclaration = $false
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::None
    $writer = [System.Xml.XmlWriter]::Create($ms, $settings)
    try {
        $doc.Save($writer)
        $writer.Flush()
        return $ms.ToArray()
    } finally {
        $writer.Dispose()
        $ms.Dispose()
    }
}

$ClientDir = Resolve-ClientDir $ClientDir
$idxPath = Join-Path $ClientDir "UI.idx"
$pakPath = Join-Path $ClientDir "UI.pak"

Write-Info "클라이언트 폴더: $ClientDir"
Write-Info "현재 UI.idx / UI.pak 을 분석합니다."

$idxBytes = [IO.File]::ReadAllBytes($idxPath)
$pakBytes = [IO.File]::ReadAllBytes($pakPath)
$entries = Get-UiEntries $idxBytes
$main = $entries | Where-Object { $_.Name -ieq "MainButtonUI.xml" } | Select-Object -First 1
if (-not $main) { throw "UI.idx에서 MainButtonUI.xml 을 찾지 못했습니다." }

$mainDoc = Try-ParseXmlFromEntry $pakBytes $main
if (-not $mainDoc) { throw "MainButtonUI.xml 복호화/파싱에 실패했습니다." }

# 이전 실행에서 만든 숫자 라벨 제거
$oldLabels = @($mainDoc.SelectNodes("//*[@Name]") | Where-Object { (Get-Attr $_ "Name") -match "^Diag20Label\d+$" })
foreach ($n in $oldLabels) {
    [void]$n.ParentNode.RemoveChild($n)
}

$buttons = @($mainDoc.SelectNodes("//*[self::Button or self::CheckButton]"))
$candidates = @()
$order = 0
foreach ($b in $buttons) {
    $order++
    $name = Get-Attr $b "Name"
    $base = Get-Attr $b "BasePng"
    $id = -1
    if ($base -match "^\s*(\d+)") { $id = [int]$Matches[1] }
    $parentName = if ($b.ParentNode -and $b.ParentNode.Attributes["Name"]) { $b.ParentNode.Attributes["Name"].Value } else { "" }

    $score = 0
    if ($id -ge 30000) { $score += 10 }
    if ($name -match "(?i)(diag|test|autohunt.*(?:test|diag)|hud.*test)") { $score += 20 }
    if ($parentName -match "(?i)(diag|test)") { $score += 10 }
    if ($name -match "(?i)(?:0?[1-9]|1\d|20)") { $score += 4 }

    if ($score -ge 10) {
        $candidates += [PSCustomObject]@{
            Node=$b; Order=$order; Score=$score; Name=$name; BasePng=$base; Id=$id; ParentName=$parentName
        }
    }
}

# 기존 단일 AUTO 버튼(900001) 같은 항목보다 1~20 진단 버튼을 우선
$numbered = @($candidates | Where-Object { $_.Name -match "(?i)(?:^|[^0-9])(0?[1-9]|1[0-9]|20)(?:[^0-9]|$)" } | Sort-Object Order)
if ($numbered.Count -ge 20) {
    $selected = @($numbered | Select-Object -First 20)
} else {
    $selected = @($candidates | Where-Object { $_.Name -notmatch "^(?i)AutoHuntHUDButton$" } | Sort-Object @{Expression="Score";Descending=$true},Order | Select-Object -First 20)
    $selected = @($selected | Sort-Object Order)
}

if ($selected.Count -lt 20) {
    $detail = ($candidates | ForEach-Object { "$($_.Name) / $($_.BasePng) / parent=$($_.ParentName)" }) -join [Environment]::NewLine
    $msg = @"
1~20 진단 버튼 20개를 찾지 못했습니다. 찾은 후보: $($selected.Count)개

이 패처는 먼저 'AUTO_1~20_게임화면_동시진단팩'을 적용한 상태에서 실행해야 합니다.

검색된 후보:
$detail
"@
    $log = Join-Path $ClientDir "AUTO_DIAG20_패치실패.txt"
    [IO.File]::WriteAllText($log, $msg, (New-Object Text.UTF8Encoding($true)))
    throw $msg
}

$textTemplate = Find-TextTemplate $entries $pakBytes
if ($textTemplate) {
    Write-Info "숫자 표시용 UI 텍스트 타입 발견: <$($textTemplate.Name)>"
} else {
    Write-Warn "기존 텍스트 템플릿을 못 찾았습니다. <Text> 기본 타입으로 시도합니다."
}

$report = New-Object System.Collections.Generic.List[string]
$report.Add("AUTO 1~20 동시 진단 패치 결과")
$report.Add("적용 시각: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
$report.Add("클라이언트: $ClientDir")
$report.Add("")

for ($i = 0; $i -lt 20; $i++) {
    $item = $selected[$i]
    $b = $item.Node
    $num = $i + 1
    $oldPng = Get-Attr $b "BasePng"
    $event = Get-Attr $b "MouseEvent"
    $bx = 0; $by = 0; $bw = 27; $bh = 27
    [void][int]::TryParse((Get-Attr $b "X" "0"), [ref]$bx)
    [void][int]::TryParse((Get-Attr $b "Y" "0"), [ref]$by)
    [void][int]::TryParse((Get-Attr $b "Width" "27"), [ref]$bw)
    [void][int]::TryParse((Get-Attr $b "Height" "27"), [ref]$bh)
    if ($bw -le 0) { $bw = 27 }
    if ($bh -le 0) { $bh = 27 }

    Set-Attr $b "BasePng" "29999;1"
    Set-Attr $b "PushPng" "29999;1"
    Set-Attr $b "HighlightPng" "29999;1"

    # 버튼 자체 Caption도 넣어 텍스트 지원 클라이언트에서는 추가 표기
    Set-Attr $b "Caption" "$num"

    # 툴팁에도 TEST 번호를 넣어 마우스를 올렸을 때 반드시 번호를 확인 가능하게 함
    $tip = $b.SelectSingleNode("./Tooltip")
    if ($tip) {
        Set-Attr $tip "Caption" "TEST $num"
    } else {
        $tip = $mainDoc.CreateElement("Tooltip")
        Set-Attr $tip "X" "0"
        Set-Attr $tip "Y" "0"
        Set-Attr $tip "Width" "$bw"
        Set-Attr $tip "Height" "$bh"
        Set-Attr $tip "Caption" "TEST $num"
        [void]$b.AppendChild($tip)
    }

    # 이미지 아래 숫자 라벨
    if ($textTemplate) {
        $label = $mainDoc.ImportNode($textTemplate, $true)
    } else {
        $label = $mainDoc.CreateElement("Text")
    }

    Set-Attr $label "Name" ("Diag20Label{0:D2}" -f $num)
    Set-Attr $label "X" "$bx"
    Set-Attr $label "Y" "$($by + $bh + 1)"
    Set-Attr $label "Width" "$([Math]::Max($bw, 28))"
    Set-Attr $label "Height" "13"
    Set-Attr $label "Caption" "$num"
    if ($label.Attributes["Text"]) { $label.Attributes["Text"].Value = "$num" }
    if ($label.Attributes["Value"]) { $label.Attributes["Value"].Value = "$num" }
    if ($label.Attributes["Activate"]) { $label.Attributes["Activate"].Value = "1" }
    if ($label.Attributes["Visible"]) { $label.Attributes["Visible"].Value = "1" }
    if ($label.Attributes["MostTop"]) { $label.Attributes["MostTop"].Value = "1" }

    $parent = $b.ParentNode
    [void]$parent.InsertAfter($label, $b)

    # 새 작은 Window/Control 안의 버튼이면 숫자가 잘리지 않도록 부모 높이 확장
    if ($parent -and $parent.Attributes["Height"]) {
        $ph = 0
        if ([int]::TryParse($parent.Attributes["Height"].Value, [ref]$ph)) {
            $need = $by + $bh + 15
            if ($ph -lt $need -and $ph -lt 120) {
                $parent.Attributes["Height"].Value = "$need"
            }
        }
    }

    $report.Add(("{0,2}번 | Name={1} | Parent={2} | 기존PNG={3} -> 29999;1 | Event={4}" -f $num,$item.Name,$item.ParentName,$oldPng,$event))
}

$xmlBytes = To-XmlBytes $mainDoc
if ($xmlBytes.Length -lt 5 -or $xmlBytes[0] -ne 0x3c) {
    throw "수정 XML 직렬화 결과가 올바르지 않습니다."
}
$newEnc = Encrypt-UiXml $xmlBytes

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupDir = Join-Path $ClientDir ("AUTO_DIAG20_BACKUP_" + $stamp)
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
Copy-Item -LiteralPath $idxPath -Destination (Join-Path $backupDir "UI.idx")
Copy-Item -LiteralPath $pakPath -Destination (Join-Path $backupDir "UI.pak")

$newOffset = [uint32]$pakBytes.Length
$newSize = [uint32]$newEnc.Length

# PAK 뒤에 새 MainButtonUI.xml 데이터 추가
$fs = [IO.File]::Open($pakPath, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $fs.Write($newEnc, 0, $newEnc.Length)
    $fs.Flush()
} finally {
    $fs.Dispose()
}

# IDX의 MainButtonUI.xml 엔트리만 새 위치로 갱신
Write-UInt32LE $idxBytes $main.RecordOffset $newOffset
Write-UInt32LE $idxBytes ($main.RecordOffset + 4) $newSize
Write-UInt32LE $idxBytes ($main.RecordOffset + 8) 0
Write-UInt32LE $idxBytes ($main.RecordOffset + 12) 0
[IO.File]::WriteAllBytes($idxPath, $idxBytes)

$report.Add("")
$report.Add("MainButtonUI 새 offset=$newOffset size=$newSize")
$report.Add("백업폴더=$backupDir")
$report.Add("")
$report.Add("게임에서 1~20 중 보이는 번호와 클릭 결과만 기록하세요.")
$reportPath = Join-Path $ClientDir "AUTO_DIAG20_적용결과.txt"
[IO.File]::WriteAllLines($reportPath, $report, (New-Object Text.UTF8Encoding($true)))

Write-Ok "완료: 20개 테스트 버튼 이미지를 모두 기존 29999로 변경했습니다."
Write-Ok "각 버튼 아래 숫자 1~20 라벨도 추가했습니다."
Write-Ok "백업: $backupDir"
Write-Ok "결과표: $reportPath"
Write-Host ""
Write-Host "게임/접속기를 완전히 종료한 상태에서 실행했어야 합니다." -ForegroundColor Yellow
Write-Host "이제 게임을 실행해서 보이는 번호와 클릭 결과를 확인하세요." -ForegroundColor Yellow
