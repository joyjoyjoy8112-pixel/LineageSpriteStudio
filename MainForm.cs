using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LineageSpriteStudio;

public sealed class MainForm : Form
{
    private readonly TextBox _client = new() { Dock = DockStyle.Fill };
    private readonly TextBox _png = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _gfx = new() { Minimum = 0, Maximum = 999999, Value = 61, Width = 100 };
    private readonly Button _scan = new() { Text = "GFX 전체 검색", AutoSize = true };
    private readonly Button _deepScan = new() { Text = "클라이언트 전체 IDX 검색", AutoSize = true };
    private readonly Button _traceMap = new() { Text = "GFX 실제 매핑 추적", AutoSize = true };
    private readonly Button _apply = new() { Text = "백업 후 클라이언트에 적용", AutoSize = true, Enabled = false };
    private readonly Button _restore = new() { Text = "원본 복원", AutoSize = true };
    private readonly Button _extractAllSpr = new() { Text = "SPR 전체 추출", AutoSize = true };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = true
    };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(24, 26, 31), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None };
    private readonly Label _status = new() { Text = "대기 중", AutoSize = true };
    private readonly Label _elapsed = new() { Text = "00:00", AutoSize = true };
    private readonly AnimatedProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 28 };
    private readonly CheckBox _backup = new() { Text = "적용 전 원본 전체 백업", Checked = true, AutoSize = true };
    private readonly PictureBox _originalPreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(18, 20, 24) };
    private readonly PictureBox _newPreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(18, 20, 24) };
    private readonly NumericUpDown _previewFrame = new() { Minimum = 0, Maximum = 0, Value = 0, Width = 72 };
    private readonly Button _previewPrev = new() { Text = "◀", Width = 40 };
    private readonly Button _previewNext = new() { Text = "▶", Width = 40 };
    private readonly Label _previewInfo = new() { Text = "동작을 선택하세요.", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _autoPlayButton = new() { Text = "▶ 자동재생", AutoSize = true };
    private readonly CheckBox _playAllActions = new() { Text = "전체 168동작", Checked = true, AutoSize = true };
    private readonly ComboBox _playSpeed = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 86 };
    private readonly ComboBox _directionMap = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly NumericUpDown _groupOffset = new() { Minimum = -20, Maximum = 20, Value = 0, Width = 62 };
    private readonly NumericUpDown _sourceGroup = new() { Minimum = 0, Maximum = 20, Value = 0, Width = 62 };
    private readonly Button _autoMap = new() { Text = "프레임수 자동 맞춤", AutoSize = true };
    private readonly Button _mapSelectedGroup = new() { Text = "선택 그룹 연결", AutoSize = true };
    private readonly Button _clearSelectedGroupMap = new() { Text = "선택 그룹 연결 해제", AutoSize = true };
    private readonly System.Windows.Forms.Timer _autoPlayTimer = new() { Interval = 180 };
    private readonly Dictionary<int, int> _groupMapOverrides = new();
    private int _previewPart = -1;
    private bool _changingPreviewFrame;

    private readonly List<SpriteTarget> _targets = new();
    private readonly Dictionary<int, List<string>> _pngByPart = new();
    private readonly Dictionary<int, int> _originalFrames = new();
    private readonly Dictionary<int, bool> _originalZlib = new();
    private readonly Dictionary<int, SprFormatInfo> _originalFormat = new();
    private readonly List<SpritePak> _opened = new();
    private readonly Stopwatch _watch = new();
    private readonly System.Windows.Forms.Timer _elapsedTimer = new() { Interval = 250 };

    public MainForm()
    {
        Text = "리니지 Sprite Studio V2.3.3";
        Width = 1260;
        Height = 820;
        MinimumSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildUi();
        _elapsedTimer.Tick += (_, _) => _elapsed.Text = _watch.Elapsed.ToString(@"mm\:ss");
        _scan.Click += async (_, _) => await ScanAsync();
        _deepScan.Click += async (_, _) => await DeepScanAllIdxAsync();
        _traceMap.Click += async (_, _) => await TraceGfxMappingAsync();
        _apply.Click += async (_, _) => await ApplyAsync();
        _restore.Click += async (_, _) => await RestoreLatestBackupAsync();
        _extractAllSpr.Click += async (_, _) => await ExtractAllSprAsync();
        _grid.SelectionChanged += (_, _) => SelectPreviewFromGrid();
        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            _grid.ClearSelection();
            _grid.Rows[e.RowIndex].Selected = true;
            _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            SelectPreviewFromGrid();
        };

        _previewFrame.ValueChanged += (_, _) => { if (!_changingPreviewFrame) ShowSelectedPreview(); };
        _previewPrev.Click += (_, _) => { if (_previewFrame.Value > _previewFrame.Minimum) _previewFrame.Value--; };
        _previewNext.Click += (_, _) => { if (_previewFrame.Value < _previewFrame.Maximum) _previewFrame.Value++; };

        _playSpeed.Items.AddRange(new object[] { "80 ms", "120 ms", "180 ms", "250 ms", "400 ms" });
        _playSpeed.SelectedItem = "180 ms";
        _playSpeed.SelectedIndexChanged += (_, _) =>
        {
            if (_playSpeed.SelectedItem is string s && int.TryParse(s.Split(' ')[0], out int ms))
                _autoPlayTimer.Interval = Math.Clamp(ms, 50, 1000);
        };

        _directionMap.Items.AddRange(new object[]
        {
            "방향 그대로",
            "방향 순서 반전",
            "방향 +1", "방향 +2", "방향 +3", "방향 +4",
            "방향 +5", "방향 +6", "방향 +7"
        });
        _directionMap.SelectedIndex = 0;
        _directionMap.SelectedIndexChanged += (_, _) =>
        {
            RebuildGrid();
            SelectPreviewFromGrid();
        };

        _groupOffset.ValueChanged += (_, _) =>
        {
            RebuildGrid();
            SelectPreviewFromGrid();
        };

        _autoMap.Click += (_, _) => AutoFindFrameMapping();
        _mapSelectedGroup.Click += (_, _) => MapSelectedGroup();
        _clearSelectedGroupMap.Click += (_, _) => ClearSelectedGroupMap();

        _autoPlayButton.Click += (_, _) => ToggleAutoPlay();
        _autoPlayTimer.Tick += (_, _) => AdvanceAutoPlay();

        FormClosed += (_, _) =>
        {
            _autoPlayTimer.Stop();
            foreach (var p in _opened) p.Dispose();
            _originalPreview.Image?.Dispose();
            _newPreview.Image?.Dispose();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12), AutoScroll = true };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var title = new Label
        {
            Text = "Lineage Sprite Studio V2.3.3  ·  전체 Sprite00~15 자동 추적/검증",
            Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(title, 0, 0);

        var settings = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 5, AutoSize = true };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        settings.Controls.Add(new Label { Text = "클라이언트 폴더", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0) }, 0, 0);
        settings.Controls.Add(_client, 1, 0);
        var browseClient = new Button { Text = "폴더 선택", AutoSize = true };
        settings.Controls.Add(browseClient, 2, 0);
        settings.Controls.Add(new Label { Text = "GFX", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 4, 0) }, 3, 0);
        settings.Controls.Add(_gfx, 4, 0);

        settings.Controls.Add(new Label { Text = "새 PNG 프레임", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 8, 0) }, 0, 1);
        settings.Controls.Add(_png, 1, 1);
        var browsePng = new Button { Text = "폴더 선택", AutoSize = true };
        settings.Controls.Add(browsePng, 2, 1);
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 6, 0, 0)
        };
        buttons.Controls.Add(_scan);
        buttons.Controls.Add(_deepScan);
        buttons.Controls.Add(_traceMap);
        buttons.Controls.Add(_apply);
        buttons.Controls.Add(_restore);
        buttons.Controls.Add(_extractAllSpr);
        buttons.Controls.Add(_backup);
        buttons.Controls.Add(new Label { Text = "방향 보정", AutoSize = true, Margin = new Padding(10, 6, 3, 0) });
        buttons.Controls.Add(_directionMap);
        settings.Controls.Add(buttons, 0, 2);
        settings.SetColumnSpan(buttons, 5);

        var mappingBar = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 6, 0, 0)
        };
        mappingBar.Controls.Add(new Label { Text = "동작 그룹 오프셋", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
        mappingBar.Controls.Add(_groupOffset);
        mappingBar.Controls.Add(_autoMap);
        mappingBar.Controls.Add(new Label { Text = "선택 그룹 → 수정 그룹", AutoSize = true, Margin = new Padding(12, 6, 4, 0) });
        mappingBar.Controls.Add(_sourceGroup);
        mappingBar.Controls.Add(_mapSelectedGroup);
        mappingBar.Controls.Add(_clearSelectedGroupMap);
        settings.Controls.Add(mappingBar, 0, 3);
        settings.SetColumnSpan(mappingBar, 5);

        browseClient.Click += (_, _) =>
        {
            using var f = new FolderBrowserDialog { Description = "리니지 클라이언트 폴더를 선택하세요." };
            if (f.ShowDialog(this) == DialogResult.OK) _client.Text = f.SelectedPath;
        };
        browsePng.Click += (_, _) =>
        {
            using var f = new FolderBrowserDialog { Description = "새 기사 PNG 545장이 있는 폴더를 선택하세요." };
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                _png.Text = f.SelectedPath;
                LoadPngMap();
                ShowFirstPngPreview();
            }
        };
        root.Controls.Add(settings, 0, 1);

        _grid.Columns.Add("Part", "동작");
        _grid.Columns.Add("File", "SPR 파일");
        _grid.Columns.Add("Pak", "실제 PAK");
        _grid.Columns.Add("Frames", "원본 프레임");
        _grid.Columns.Add("Png", "새 PNG");
        _grid.Columns.Add("Map", "수정 매핑");
        _grid.Columns.Add("Hash", "분배 검사");
        root.Controls.Add(_grid, 0, 2);

        var lower = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 455 };
        lower.Panel1.Controls.Add(_log);

        var compare = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(6) };
        compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        compare.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        compare.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        compare.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var originalLabel = new Label
        {
            Text = "원본 SPR",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            Padding = new Padding(0, 2, 0, 5)
        };
        var newLabel = new Label
        {
            Text = "수정 PNG",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
            Padding = new Padding(0, 2, 0, 5)
        };
        compare.Controls.Add(originalLabel, 0, 0);
        compare.Controls.Add(newLabel, 1, 0);
        compare.Controls.Add(_originalPreview, 0, 1);
        compare.Controls.Add(_newPreview, 1, 1);

        var frameBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(4, 5, 4, 0)
        };
        frameBar.Controls.Add(_previewPrev);
        frameBar.Controls.Add(new Label { Text = "프레임", AutoSize = true, Margin = new Padding(8, 6, 3, 0) });
        frameBar.Controls.Add(_previewFrame);
        frameBar.Controls.Add(_previewNext);
        frameBar.Controls.Add(_autoPlayButton);
        frameBar.Controls.Add(_playAllActions);
        frameBar.Controls.Add(_playSpeed);
        frameBar.Controls.Add(_previewInfo);
        compare.Controls.Add(frameBar, 0, 2);
        compare.SetColumnSpan(frameBar, 2);

        lower.Panel2.Controls.Add(compare);
        root.Controls.Add(lower, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true, Padding = new Padding(0, 7, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(_progress, 0, 0);
        footer.Controls.Add(_status, 1, 0);
        footer.Controls.Add(new Label { Text = "경과", AutoSize = true, Margin = new Padding(16, 5, 4, 0) }, 2, 0);
        footer.Controls.Add(_elapsed, 3, 0);
        root.Controls.Add(footer, 0, 4);
    }

    private async Task ScanAsync()
    {
        if (!Directory.Exists(_client.Text))
        {
            MessageBox.Show(this, "클라이언트 폴더를 먼저 선택하세요.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        _watch.Restart();
        _elapsedTimer.Start();
        try
        {
            int gfx = (int)_gfx.Value;
            string root = Path.GetFullPath(_client.Text);
            SetProgress(1, "3.8 Sprite IDX 분석 중...");
            _targets.Clear();
            _originalFrames.Clear();
            _originalZlib.Clear();
            _originalFormat.Clear();
            _groupMapOverrides.Clear();
            _groupOffset.Value = 0;
            foreach (var p in _opened) p.Dispose();
            _opened.Clear();

            var idxFiles = Directory.EnumerateFiles(root, "Sprite*.idx", SearchOption.TopDirectoryOnly)
                .Where(p => !p.Contains("SpriteStudio_Backup_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Log($"[3.8] Sprite IDX {idxFiles.Count}개 발견");
            if (idxFiles.Count == 0)
                Log("[3.8] 선택 폴더에 Sprite*.idx가 없습니다.");

            await Task.Run(() =>
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int done = 0;

                foreach (var idx in idxFiles)
                {
                    done++;
                    try
                    {
                        var pak = new SpritePak(idx);
                        _opened.Add(pak);

                        string baseName = Path.GetFileNameWithoutExtension(idx);
                        int pakNo = -1;
                        var noMatch = Regex.Match(baseName, @"^Sprite(?<n>\d{2})$", RegexOptions.IgnoreCase);
                        if (noMatch.Success) int.TryParse(noMatch.Groups["n"].Value, out pakNo);

                        BeginInvoke(() => Log($"[IDX] {Path.GetFileName(idx)} = {pak.IndexFormat}{(pak.IsDesEncrypted ? "+DES" : "")} / {pak.Entries.Count:N0}개"));

                        var rx = new Regex($"^{gfx}-(\\d+)\\.spr$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                        foreach (var e in pak.Entries)
                        {
                            var m = rx.Match(Path.GetFileName(e.FileName));
                            if (!m.Success) continue;
                            if (!seen.Add(e.FileName)) continue;
                            int part = int.Parse(m.Groups[1].Value);
                            _targets.Add(new SpriteTarget(pakNo, idx, pak, e, part));
                        }
                    }
                    catch (Exception ex)
                    {
                        string idxName = Path.GetFileName(idx);
                        BeginInvoke(() => Log($"[IDX 건너뜀] {idxName}: {ex.Message}"));
                    }

                    ReportFromWorker(3 + (int)(45.0 * done / Math.Max(1, idxFiles.Count)),
                        $"{Path.GetFileName(idx)} 분석 중...");
                }

                foreach (var t in _targets)
                {
                    var stored = t.Pak.Extract(t.Entry);
                    bool zlib = SpriteCodec.IsZlib(stored);
                    var raw = SpriteCodec.DecodeIfNeeded(stored);
                    _originalZlib[t.Part] = zlib;
                    var format = SprInfo.Analyze(raw);
                    _originalFormat[t.Part] = format;
                    _originalFrames[t.Part] = format.FrameCount;
                }
            });

            _targets.Sort((a, b) => a.Part.CompareTo(b.Part));
            LoadPngMap();
            RebuildGrid();

            if (_targets.Count == 0)
                Log($"[검색] GFX {gfx}의 SPR을 찾지 못했습니다. '클라이언트 전체 IDX 검색'을 눌러 실제 위치를 확인하세요.");

            int totalFrames = _originalFrames.Values.Sum();
            int pngCount = _pngByPart.Values.Sum(x => x.Count);
            Log($"[검색] GFX {gfx}: {_targets.Count}개 동작 SPR 찾음");
            Log($"[검색] 원본 프레임 합계: {totalFrames} / 새 PNG 인식: {pngCount}");
            Log($"[검색] ZLIB SPR: {_originalZlib.Values.Count(v => v)}개 / RAW SPR: {_originalZlib.Values.Count(v => !v)}개");
            int paletteSpr = _originalFormat.Values.Count(v => v.IsPalette);
            Log($"[검색] SPR 색상 형식: PALETTE {paletteSpr}개 / RGB555 {_originalFormat.Count - paletteSpr}개");
            if (_originalFormat.Count > 0)
            {
                string types = string.Join(", ", _originalFormat.Values
                    .GroupBy(v => v.FrameType)
                    .OrderBy(g => g.Key)
                    .Select(g => $"type {g.Key}={g.Count()}"));
                Log($"[검색] 프레임 타입: {types}");
            }

            var hashed = _targets.Where(t => t.PakIndex >= 0).ToList();
            var misplaced = hashed.Where(t => SpritePak.ExpectedPakIndex(t.Entry.FileName) != t.PakIndex).ToList();
            if (hashed.Count == 0)
                Log("[분배] Sprite.idx 단일 PAK 구조 또는 번호 없는 Sprite PAK 구조입니다.");
            else if (misplaced.Count == 0)
                Log("[분배] 번호형 Sprite PAK의 파일명 byte합 % 16 분배가 일치합니다.");
            else
                Log($"[경고] 번호형 PAK 분배 불일치 {misplaced.Count}개 발견");

            SetProgress(100, $"검색 완료 · {_targets.Count}개 SPR");
            _apply.Enabled = _targets.Count > 0 && _pngByPart.Count > 0;
            if (_grid.Rows.Count > 0)
            {
                _grid.ClearSelection();
                _grid.Rows[0].Selected = true;
                SelectPreviewFromGrid();
            }
        }
        catch (Exception ex)
        {
            SetProgress(0, "검색 실패");
            Log("[오류] " + ex);
            MessageBox.Show(this, ex.Message, "검색 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _watch.Stop();
            _elapsedTimer.Stop();
            SetBusy(false);
        }
    }

    private async Task DeepScanAllIdxAsync()
    {
        if (!Directory.Exists(_client.Text))
        {
            MessageBox.Show(this, "클라이언트 폴더를 먼저 선택하세요.", "확인",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        _watch.Restart();
        _elapsedTimer.Start();

        try
        {
            int gfx = (int)_gfx.Value;
            SetProgress(1, "클라이언트 전체 IDX 검색 중...");

            string root = Path.GetFullPath(_client.Text);
            var idxFiles = Directory.EnumerateFiles(root, "*.idx", SearchOption.AllDirectories)
                .Where(p => !p.Contains("SpriteStudio_Backup_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Log($"[전체IDX] 검색 시작: {root}");
            Log($"[전체IDX] IDX 파일 {idxFiles.Count}개 발견");

            var hits = new List<(string Dir, string Idx, string Format, int Count, List<int> Parts)>();
            int done = 0;

            await Task.Run(() =>
            {
                foreach (var idx in idxFiles)
                {
                    done++;
                    ReportFromWorker(1 + (int)(88.0 * done / Math.Max(1, idxFiles.Count)),
                        $"전체 IDX 검색 중... {done}/{idxFiles.Count}");

                    try
                    {
                        string pak = Path.ChangeExtension(idx, ".pak");
                        if (!File.Exists(pak)) continue;

                        using var sp = new AnyPakScanner(idx);
                        var rx = new Regex($"^{gfx}-(\\d+)\\.spr$",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                        var parts = new List<int>();
                        foreach (var e in sp.Entries)
                        {
                            var m = rx.Match(Path.GetFileName(e.FileName));
                            if (m.Success && int.TryParse(m.Groups[1].Value, out int part))
                                parts.Add(part);
                        }

                        if (parts.Count > 0)
                        {
                            lock (hits)
                            {
                                hits.Add((Path.GetDirectoryName(idx) ?? "", idx, sp.Format + (sp.DesEncrypted ? "+DES" : ""), parts.Count, parts));
                            }
                        }
                    }
                    catch
                    {
                        // Not every IDX in the client is a supported _EXT sprite index.
                    }
                }
            });

            var groups = hits
                .GroupBy(h => h.Dir, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Dir = g.Key,
                    IdxCount = g.Count(),
                    EntryCount = g.Sum(x => x.Count),
                    Parts = g.SelectMany(x => x.Parts).Distinct().OrderBy(x => x).ToList(),
                    Files = g.Select(x => $"{Path.GetFileName(x.Idx)}[{x.Format}]").OrderBy(x => x).ToList()
                })
                .OrderByDescending(g => g.EntryCount)
                .ThenBy(g => g.Dir, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groups.Count == 0)
            {
                Log($"[전체IDX] GFX {gfx}의 SPR을 포함한 IDX/PAK 쌍을 찾지 못했습니다.");
            }
            else
            {
                foreach (var g in groups)
                {
                    string coverage = g.Parts.Count == 0 ? "-" :
                        $"{g.Parts.First()}~{g.Parts.Last()} / 고유 {g.Parts.Count}개";
                    Log($"[전체IDX] 폴더: {g.Dir}");
                    Log($"[전체IDX]   GFX {gfx}: SPR {g.EntryCount}개 / 고유동작 {g.Parts.Count}개 / IDX {g.IdxCount}개 / 범위 {coverage}");
                    Log($"[전체IDX]   IDX: {string.Join(", ", g.Files.Take(20))}{(g.Files.Count > 20 ? " ..." : "")}");
                }
            }

            string mjlin = Path.Combine(root, "mjlin.bin");
            string jungden = Path.Combine(root, "jungden.DLL");
            if (!File.Exists(jungden))
            {
                string alt = Directory.EnumerateFiles(root, "jungden*.DLL", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? "";
                if (!string.IsNullOrEmpty(alt)) jungden = alt;
            }

            Log($"[실행경로] Sprite Studio 선택 폴더: {root}");
            if (File.Exists(mjlin))
                Log($"[실행경로] mjlin.bin: {mjlin} / SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mjlin))).Substring(0, 16)}...");
            else
                Log("[실행경로] mjlin.bin 없음");

            if (File.Exists(jungden))
                Log($"[실행경로] jungden.DLL: {jungden} / SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(jungden))).Substring(0, 16)}...");
            else
                Log("[실행경로] jungden.DLL 없음");

            SetProgress(100, $"전체 IDX 검색 완료 · 후보 폴더 {groups.Count}개");

            if (groups.Count > 1)
            {
                MessageBox.Show(this,
                    $"GFX {gfx}가 들어 있는 리소스 폴더를 {groups.Count}곳 찾았습니다.\n\n로그의 [전체IDX] 항목을 확인하세요.\n게임이 다른 폴더의 Sprite를 읽고 있을 가능성이 있습니다.",
                    "중복 Sprite 후보 발견", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this,
                    $"전체 클라이언트에서 GFX {gfx}가 들어 있는 IDX/PAK 폴더를 {groups.Count}곳 찾았습니다.\n\n로그의 [전체IDX]와 [실행경로]를 확인해 주세요.",
                    "전체 IDX 검색 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            SetProgress(0, "전체 IDX 검색 실패");
            Log("[전체IDX 오류] " + ex);
            MessageBox.Show(this, ex.Message, "전체 IDX 검색 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _watch.Stop();
            _elapsedTimer.Stop();
            SetBusy(false);
        }
    }

    private async Task TraceGfxMappingAsync()
    {
        if (!Directory.Exists(_client.Text))
        {
            MessageBox.Show(this, "클라이언트 폴더를 먼저 선택하세요.", "확인",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true);
        _watch.Restart();
        _elapsedTimer.Start();

        try
        {
            int gfx = (int)_gfx.Value;
            string root = Path.GetFullPath(_client.Text);
            SetProgress(1, "list.spr / wlist.spr 검색 중...");
            Log($"[실제매핑] GFX {gfx} 추적 시작");
            Log("[실제매핑] 이 기능은 GFX 전체 검색 성공 여부와 무관하게 단독 실행할 수 있습니다.");

            var candidates = new List<(string Source, byte[] Data)>();

            foreach (var name in new[] { "list.spr", "wlist.spr", "list.spz", "wlist.spz" })
            {
                try
                {
                    foreach (var p in Directory.EnumerateFiles(root, name, SearchOption.AllDirectories)
                        .Where(p => !p.Contains("SpriteStudio_Backup_", StringComparison.OrdinalIgnoreCase)))
                    {
                        candidates.Add(($"파일: {p}", File.ReadAllBytes(p)));
                    }
                }
                catch { }
            }

            var idxFiles = Directory.EnumerateFiles(root, "*.idx", SearchOption.AllDirectories)
                .Where(p => !p.Contains("SpriteStudio_Backup_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            int done = 0;
            foreach (var idx in idxFiles)
            {
                done++;
                SetProgress(5 + (int)(45.0 * done / Math.Max(1, idxFiles.Count)),
                    $"매핑 테이블 검색 중... {done}/{idxFiles.Count}");

                try
                {
                    if (!File.Exists(Path.ChangeExtension(idx, ".pak"))) continue;
                    using var pak = new AnyPakScanner(idx);
                    foreach (var e in pak.Entries)
                    {
                        string fn = Path.GetFileName(e.FileName);
                        if (!fn.Equals("list.spr", StringComparison.OrdinalIgnoreCase) &&
                            !fn.Equals("wlist.spr", StringComparison.OrdinalIgnoreCase) &&
                            !fn.Equals("list.spz", StringComparison.OrdinalIgnoreCase) &&
                            !fn.Equals("wlist.spz", StringComparison.OrdinalIgnoreCase))
                            continue;

                        candidates.Add(($"PAK[{pak.Format}]: {idx} -> {e.FileName}", pak.Extract(e)));
                    }
                }
                catch
                {
                    // 다른 IDX 형식은 건너뜀
                }
            }

            candidates = candidates
                .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (candidates.Count == 0)
            {
                Log("[실제매핑] list.spr / wlist.spr / list.spz를 찾지 못했습니다.");
                MessageBox.Show(this,
                    "클라이언트와 지원되는 IDX/PAK 안에서 list.spr / wlist.spr / list.spz를 찾지 못했습니다.\n로그를 보내주세요.",
                    "GFX 실제 매핑", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetProgress(100, "매핑 테이블 없음");
                return;
            }

            Log($"[실제매핑] list/wlist 후보 {candidates.Count}개 발견");

            var archiveEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var idx in Directory.EnumerateFiles(root, "Sprite*.idx", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var p = new AnyPakScanner(idx);
                    foreach (var e in p.Entries) archiveEntries.Add(Path.GetFileName(e.FileName));
                }
                catch { }
            }

            int foundEntries = 0;
            foreach (var candidate in candidates)
            {
                byte[] data = candidate.Data;
                if (SpriteCodec.IsZlib(data))
                {
                    try { data = SpriteCodec.DecodeIfNeeded(data); } catch { }
                }

                string textContent;
                try
                {
                    textContent = new UTF8Encoding(false, true).GetString(data);
                }
                catch
                {
                    textContent = Encoding.Default.GetString(data);
                }

                var lines = textContent.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                int start = -1;
                Match? headerMatch = null;
                var headerRx = new Regex(@"^\s*#(?<id>\d+)\s+(?<count>\d+)(?:=(?<linked>\d+))?(?:\s+(?<name>.*))?$",
                    RegexOptions.CultureInvariant);

                for (int i = 0; i < lines.Length; i++)
                {
                    var m = headerRx.Match(lines[i]);
                    if (!m.Success) continue;
                    if (int.Parse(m.Groups["id"].Value) != gfx) continue;
                    start = i;
                    headerMatch = m;
                    break;
                }

                if (start < 0 || headerMatch == null)
                {
                    Log($"[실제매핑] {candidate.Source}: #{gfx} 항목 없음");
                    continue;
                }

                foundEntries++;
                int imageCount = int.Parse(headerMatch.Groups["count"].Value);
                int spriteId = headerMatch.Groups["linked"].Success
                    ? int.Parse(headerMatch.Groups["linked"].Value)
                    : gfx;
                string entryName = headerMatch.Groups["name"].Value.Trim();

                var block = new StringBuilder();
                block.AppendLine(lines[start]);
                for (int i = start + 1; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("#")) break;
                    block.AppendLine(lines[i]);
                }

                var actionRx = new Regex(@"(?<aid>-?\d+)\.(?<name>[a-zA-Z_][a-zA-Z0-9_\s-]*)?\((?<dir>-?\d+)\s+(?<fc>-?\d+),(?<frames>[^)]*)\)",
                    RegexOptions.CultureInvariant);
                var frameRx = new Regex(@"(?<img>-?\d+)\.(?<frame>-?\d+):(?<dur>-?\d+)",
                    RegexOptions.CultureInvariant);

                var actions = actionRx.Matches(block.ToString()).Cast<Match>().ToList();
                var actualSubIds = new HashSet<int>();
                var imageIds = new HashSet<int>();

                foreach (var action in actions)
                {
                    int directional = int.Parse(action.Groups["dir"].Value);
                    string framesText = action.Groups["frames"].Value;
                    foreach (Match fm in frameRx.Matches(framesText))
                    {
                        int imageId = int.Parse(fm.Groups["img"].Value);
                        if (imageId < 0) continue;
                        imageIds.Add(imageId);
                        if (directional == 1)
                        {
                            for (int d = 0; d < 8; d++) actualSubIds.Add(imageId + d);
                        }
                        else
                        {
                            actualSubIds.Add(imageId);
                        }
                    }
                }

                var actualFiles = actualSubIds
                    .OrderBy(x => x)
                    .Select(x => $"{spriteId}-{x}.spr")
                    .ToList();
                int existing = actualFiles.Count(f => archiveEntries.Contains(f));

                Log($"[실제매핑] 소스: {candidate.Source}");
                Log($"[실제매핑] #{gfx}: SpriteId={spriteId} / ImageCount={imageCount} / 이름={entryName}");
                Log($"[실제매핑] 동작 {actions.Count}개 / ImageId {imageIds.Count}종 / 실제 SPR 후보 {actualFiles.Count}개 / 아카이브 존재 {existing}개");
                if (actualFiles.Count > 0)
                {
                    Log($"[실제매핑] 실제 파일 예: {string.Join(", ", actualFiles.Take(16))}{(actualFiles.Count > 16 ? " ..." : "")}");
                }

                if (spriteId != gfx)
                {
                    Log($"[핵심] 서버 GFX {gfx}는 파일 {gfx}-*.spr이 아니라 SpriteId {spriteId}-*.spr을 사용합니다.");
                    Log($"[핵심] 지금까지 {gfx}-*.spr을 바꿨다면 게임 화면이 그대로였던 이유와 일치합니다.");
                }
                else
                {
                    Log($"[실제매핑] SpriteId가 GFX와 동일합니다. 다음으로 list.spr의 ImageId/동작 매핑과 적용 포맷을 확인해야 합니다.");
                }
            }

            SetProgress(100, $"GFX 매핑 추적 완료 · #{gfx} 항목 {foundEntries}개");

            MessageBox.Show(this,
                $"GFX {gfx} 실제 매핑 추적 완료\n\nlist/wlist 후보: {candidates.Count}개\n#{gfx} 항목 발견: {foundEntries}개\n\n로그의 [실제매핑]과 [핵심] 줄을 보내주세요.",
                "GFX 실제 매핑", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetProgress(0, "GFX 매핑 추적 실패");
            Log("[실제매핑 오류] " + ex);
            MessageBox.Show(this, ex.Message, "GFX 실제 매핑 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _watch.Stop();
            _elapsedTimer.Stop();
            SetBusy(false);
        }
    }

    private void LoadPngMap()
    {
        _pngByPart.Clear();
        if (!Directory.Exists(_png.Text)) return;
        int gfx = (int)_gfx.Value;
        var allPng = Directory.EnumerateFiles(_png.Text, "*.png", SearchOption.AllDirectories)
            .OrderBy(x => x, NaturalPathComparer.Instance).ToList();

        var rx = new Regex($"^{gfx}-(?<part>\\d+).*?(?:frame|프레임)[ _-]?(?<frame>\\d+).*\\.png$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        int matched = 0;
        foreach (var f in allPng)
        {
            var m = rx.Match(Path.GetFileName(f));
            if (!m.Success) continue;
            int part = int.Parse(m.Groups["part"].Value);
            if (!_pngByPart.TryGetValue(part, out var list)) _pngByPart[part] = list = new List<string>();
            list.Add(f);
            matched++;
        }

        foreach (var pair in _pngByPart)
            pair.Value.Sort(NaturalPathComparer.Instance);

        // 파일명이 다르더라도 총 프레임 수가 원본과 정확히 같으면
        // 원본 동작별 프레임 수를 기준으로 자연 정렬 순서대로 자동 배분한다.
        int originalTotal = _originalFrames.Values.Sum();
        if (matched == 0 && allPng.Count > 0 && originalTotal > 0 && allPng.Count == originalTotal)
        {
            _pngByPart.Clear();
            int cursor = 0;
            foreach (var part in _originalFrames.Keys.OrderBy(x => x))
            {
                int count = _originalFrames[part];
                _pngByPart[part] = allPng.Skip(cursor).Take(count).ToList();
                cursor += count;
            }
            matched = allPng.Count;
            Log($"[PNG] 파일명 패턴은 다르지만 총 {allPng.Count}장이 원본 {originalTotal}프레임과 일치하여 순차 자동 배분했습니다.");
        }
        else
        {
            Log($"[PNG] 선택 폴더(하위폴더 포함) PNG {allPng.Count}개 / 파일명 매칭 {matched}개");
            if (allPng.Count > 0 && matched == 0)
                Log($"[PNG] 첫 파일: {Path.GetFileName(allPng[0])}");
        }

        RebuildGrid();
    }

    private void RebuildGrid()
    {
        if (InvokeRequired) { BeginInvoke(RebuildGrid); return; }
        _grid.Rows.Clear();
        foreach (var t in _targets)
        {
            int frames = _originalFrames.TryGetValue(t.Part, out var fc) ? fc : 0;
            int sourcePart = GetMappedSourcePart(t.Part);
            int png = GetMappedPngFiles(t.Part)?.Count ?? 0;
            int expected = SpritePak.ExpectedPakIndex(t.Entry.FileName);
            string dist = t.PakIndex < 0 ? "단일/기본 PAK" : (expected == t.PakIndex ? "OK" : $"예상 {expected:00}");
            string mapText = sourcePart >= 0 ? $"{(int)_gfx.Value}-{sourcePart}.spr" : "없음";
            int row = _grid.Rows.Add(t.Part, t.Entry.FileName, Path.GetFileName(t.Pak.PakPath), frames, png, mapText, dist);
            if (png != frames) _grid.Rows[row].DefaultCellStyle.BackColor = Color.MistyRose;
        }
    }

    private async Task ApplyAsync()
    {
        if (_targets.Count == 0) { await ScanAsync(); if (_targets.Count == 0) return; }
        LoadPngMap();

        var missing = _targets.Where(t => GetMappedPngFiles(t.Part) is not { Count: > 0 }).ToList();
        var mismatch = _targets.Where(t =>
        {
            var p = GetMappedPngFiles(t.Part);
            return p is { Count: > 0 } && _originalFrames.TryGetValue(t.Part, out var f) && p.Count != f;
        }).ToList();
        if (missing.Count > 0 || mismatch.Count > 0)
        {
            var details = mismatch.Take(12).Select(t =>
            {
                int original = _originalFrames.TryGetValue(t.Part, out var f) ? f : 0;
                int sourcePart = GetMappedSourcePart(t.Part);
                int png = GetMappedPngFiles(t.Part)?.Count ?? 0;
                return $"{t.Entry.FileName} ← {(int)_gfx.Value}-{sourcePart}.spr : 원본 {original} / 수정 {png}";
            }).ToList();

            foreach (var t in missing)
                Log($"[매핑 누락] {t.Entry.FileName}");
            foreach (var t in mismatch)
            {
                int original = _originalFrames.TryGetValue(t.Part, out var f) ? f : 0;
                int sourcePart = GetMappedSourcePart(t.Part);
                int png = GetMappedPngFiles(t.Part)?.Count ?? 0;
                Log($"[프레임 불일치] {t.Entry.FileName} ← {(int)_gfx.Value}-{sourcePart}.spr : 원본 {original} / 수정 {png}");
            }

            string more = mismatch.Count > details.Count ? $"\n외 {mismatch.Count - details.Count}개" : "";
            string msg = $"적용을 중단했습니다.\n\nPNG가 없는 동작: {missing.Count}개\n원본 프레임 수와 다른 동작: {mismatch.Count}개\n\n"
                       + (details.Count > 0 ? string.Join("\n", details) + more : "")
                       + "\n\n매핑을 맞춘 뒤 다시 적용하세요.";
            MessageBox.Show(this, msg, "프레임 대응 확인", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Log($"[중단] 누락={missing.Count}, 프레임수 불일치={mismatch.Count}");
            return;
        }

        if (MessageBox.Show(this,
            $"GFX {(int)_gfx.Value}의 {_targets.Count}개 SPR / {_pngByPart.Values.Sum(x => x.Count)}개 PNG를 적용합니다.\n\n클라이언트를 완전히 종료한 상태에서 진행하세요.",
            "적용 확인", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

        SetBusy(true);
        _watch.Restart();
        _elapsedTimer.Start();

        string? backupDir = null;
        var originals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var generated = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            SetProgress(2, "적용 준비 중...");
            var uniquePaks = _targets.Select(t => t.Pak).Distinct().ToList();

            foreach (var pak in uniquePaks)
                originals[pak.PakPath] = new FileInfo(pak.PakPath).Length;

            if (_backup.Checked)
            {
                backupDir = Path.Combine(_client.Text, "SpriteStudio_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(backupDir);

                foreach (string idxPath in uniquePaks.Select(p => p.IdxPath).Distinct(StringComparer.OrdinalIgnoreCase))
                    File.Copy(idxPath, Path.Combine(backupDir, Path.GetFileName(idxPath)), true);

                foreach (string pakPath in uniquePaks.Select(p => p.PakPath).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    SetProgress(4, $"원본 전체 백업 중... {Path.GetFileName(pakPath)}");
                    await Task.Run(() => File.Copy(pakPath, Path.Combine(backupDir, Path.GetFileName(pakPath)), true));
                }

                var manifest = originals.ToDictionary(k => Path.GetFileName(k.Key), v => v.Value);
                File.WriteAllText(Path.Combine(backupDir, "pak_lengths.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                File.WriteAllText(Path.Combine(backupDir, "README_RESTORE.txt"),
                    "Lineage Sprite Studio V2.3.3 full backup\r\n" +
                    "IDX와 PAK 원본 전체가 들어 있습니다. 프로그램의 '원본 복원' 버튼으로 복원할 수 있습니다.\r\n");
                Log($"[백업] 원본 IDX/PAK 전체 백업 완료: {backupDir}");
            }

            int done = 0;
            int total = _targets.Count;
            foreach (var t in _targets)
            {
                var pngs = GetMappedPngFiles(t.Part) ?? throw new InvalidDataException($"동작 {t.Part}의 PNG를 찾지 못했습니다.");
                SetProgress(8 + (int)(42.0 * done / total), $"SPR 생성 중... {done + 1}/{total} · {t.Entry.FileName}");
                var originalFormat = _originalFormat.TryGetValue(t.Part, out var fmt)
                    ? fmt
                    : new SprFormatInfo(pngs.Count, false, 0, 0);

                byte[] rawSpr = await Task.Run(() =>
                    SprEncoder.CreateFromPngs(pngs, originalFormat.IsPalette, originalFormat.FrameType));

                var generatedFormat = SprInfo.Analyze(rawSpr);
                if (generatedFormat.FrameCount != pngs.Count)
                    throw new InvalidDataException($"생성 SPR 프레임 검증 실패: {t.Entry.FileName}");

                // 자체 decoder로 첫/마지막 프레임까지 실제 해석해 본다.
                using (var firstFrame = SprDecoder.DecodeFrame(rawSpr, 0)) { }
                if (pngs.Count > 1)
                    using (var lastFrame = SprDecoder.DecodeFrame(rawSpr, pngs.Count - 1)) { }

                bool useZlib = _originalZlib.TryGetValue(t.Part, out var z) && z;
                byte[] storedSpr = useZlib ? await Task.Run(() => SpriteCodec.EncodeZlib(rawSpr)) : rawSpr;
                generated[t.Entry.FileName] = storedSpr;
                done++;
            }

            done = 0;
            foreach (var group in _targets
                .GroupBy(t => t.IdxPath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                var pak = group.First().Pak;

                if (pak.IndexFormat.Equals("LEGACY28", StringComparison.OrdinalIgnoreCase) && !pak.IsDesEncrypted)
                {
                    // 3.8 구형 클라이언트는 단순 append보다 실제 묶기와 같은 compact rebuild가 안전하다.
                    var replacements = group.ToDictionary(
                        t => t.Entry.FileName,
                        t => generated[t.Entry.FileName],
                        StringComparer.OrdinalIgnoreCase);

                    SetProgress(52 + (int)(28.0 * done / total),
                        $"3.8 안전 재묶기 중... {Path.GetFileName(pak.PakPath)}");
                    Log($"[3.8 재묶기] {Path.GetFileName(pak.IdxPath)} + {Path.GetFileName(pak.PakPath)} / 교체 {replacements.Count}개");
                    await Task.Run(() => pak.RebuildLegacyPak(replacements));
                    done += group.Count();
                }
                else
                {
                    foreach (var t in group)
                    {
                        var spr = generated[t.Entry.FileName];
                        SetProgress(52 + (int)(28.0 * done / total),
                            $"PAK 적용 중... {done + 1}/{total} · {Path.GetFileName(t.Pak.PakPath)}");
                        long offset = await Task.Run(() => pak.AppendRaw(spr));
                        t.Entry.Offset = offset;
                        t.Entry.FileSize = spr.Length;
                        t.Entry.CompressedSize = 0;
                        t.Entry.Flags = 0;
                        done++;
                    }

                    await Task.Run(pak.SaveIndex);
                }
            }

            SetProgress(82, "적용 결과 재검증 중...");
            int verified = 0;
            foreach (var group in _targets.GroupBy(t => t.IdxPath, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                string idx = group.First().IdxPath;
                using var check = new SpritePak(idx);
                foreach (var t in group)
                {
                    var e = check.Entries.FirstOrDefault(x => x.FileName.Equals(t.Entry.FileName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException($"검증 중 {t.Entry.FileName}을 찾지 못했습니다.");
                    byte[] actual = await Task.Run(() => check.Extract(e));
                    byte[] expected = generated[t.Entry.FileName];
                    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(actual), SHA256.HashData(expected)))
                        throw new InvalidDataException($"검증 실패: {t.Entry.FileName}의 저장 데이터가 생성 데이터와 다릅니다.");
                    verified++;
                    SetProgress(82 + (int)(17.0 * verified / total), $"검증 중... {verified}/{total} · {t.Entry.FileName}");
                }
            }

            SetProgress(100, $"완료 · {verified}개 SPR 검증 성공");
            Log($"[완료] GFX {(int)_gfx.Value}: {verified}/{total} SPR 재추출 해시 검증 성공");
            Log($"[완료] 새 PNG {_pngByPart.Values.Sum(x => x.Count)}프레임 적용");
            Log("[안내] 3.8 LEGACY28은 프로그램 내부에서 안전 재묶기까지 완료했습니다. 외부 eat 묶기를 다시 하지 마세요.");
            MessageBox.Show(this,
                $"적용 및 재검증 완료\n\nSPR: {verified}/{total}\nPNG: {_pngByPart.Values.Sum(x => x.Count)}프레임\n\n3.8 클라이언트는 안전 재묶기까지 완료했습니다.\n외부 eat로 다시 묶지 말고 바로 게임을 실행해서 확인하세요.",
                "V2.3.3 적용 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("[오류] " + ex);
            SetProgress(0, "실패 · 자동 복구 중...");
            try
            {
                foreach (var kv in originals)
                {
                    if (File.Exists(kv.Key))
                    {
                        using var fs = new FileStream(kv.Key, FileMode.Open, FileAccess.Write, FileShare.None);
                        if (fs.Length >= kv.Value) fs.SetLength(kv.Value);
                    }
                }

                if (backupDir != null)
                {
                    var backupPaks = Directory.EnumerateFiles(backupDir, "Sprite*.pak", SearchOption.TopDirectoryOnly).ToList();
                    if (backupPaks.Count > 0)
                    {
                        foreach (var f in backupPaks)
                            File.Copy(f, Path.Combine(_client.Text, Path.GetFileName(f)), true);
                    }

                    foreach (var f in Directory.EnumerateFiles(backupDir, "Sprite*.idx", SearchOption.TopDirectoryOnly))
                        File.Copy(f, Path.Combine(_client.Text, Path.GetFileName(f)), true);
                }
                Log("[복구] 적용 전 원본 IDX/PAK 상태로 복구했습니다.");
            }
            catch (Exception rollback)
            {
                Log("[복구 오류] " + rollback);
            }
            MessageBox.Show(this, ex.Message, "적용 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _watch.Stop();
            _elapsedTimer.Stop();
            SetBusy(false);
        }
    }

    private async Task ExtractAllSprAsync()
    {
        if (!Directory.Exists(_client.Text))
        {
            MessageBox.Show(this, "클라이언트 폴더를 먼저 선택하세요.", "SPR 전체 추출",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var folder = new FolderBrowserDialog
        {
            Description = "전체 SPR을 저장할 폴더를 선택하세요."
        };

        if (folder.ShowDialog(this) != DialogResult.OK)
            return;

        string root = Path.GetFullPath(_client.Text);
        string outputRoot = Path.Combine(
            folder.SelectedPath,
            "SpriteStudio_AllSPR_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));

        Directory.CreateDirectory(outputRoot);

        SetBusy(true);
        _watch.Restart();
        _elapsedTimer.Start();

        try
        {
            var idxFiles = Directory.EnumerateFiles(root, "Sprite*.idx", SearchOption.TopDirectoryOnly)
                .Where(p => !p.Contains("SpriteStudio_Backup_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (idxFiles.Count == 0)
                throw new InvalidDataException("선택한 클라이언트 폴더에서 Sprite*.idx를 찾지 못했습니다.");

            int totalSpr = 0;
            int decodedZlib = 0;
            int failed = 0;
            var manifest = new List<string>
            {
                "IDX\tPAK\tENTRY\tOUTPUT\tZLIB_DECODED\tSIZE"
            };

            Log($"[SPR 전체 추출] 시작: {root}");
            Log($"[SPR 전체 추출] Sprite IDX {idxFiles.Count}개 발견");

            for (int idxNo = 0; idxNo < idxFiles.Count; idxNo++)
            {
                string idxPath = idxFiles[idxNo];
                string idxName = Path.GetFileNameWithoutExtension(idxPath);
                string archiveDir = Path.Combine(outputRoot, idxName);
                Directory.CreateDirectory(archiveDir);

                try
                {
                    using var pak = new SpritePak(idxPath);
                    var sprEntries = pak.Entries
                        .Where(e => Path.GetExtension(e.FileName).Equals(".spr", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    Log($"[SPR 전체 추출] {Path.GetFileName(idxPath)} = {pak.IndexFormat} / SPR {sprEntries.Count:N0}개");

                    int localDone = 0;
                    foreach (var e in sprEntries)
                    {
                        localDone++;
                        try
                        {
                            byte[] data = await Task.Run(() => pak.Extract(e));
                            bool wasZlib = SpriteCodec.IsZlib(data);
                            if (wasZlib)
                            {
                                data = await Task.Run(() => SpriteCodec.DecodeIfNeeded(data));
                                decodedZlib++;
                            }

                            // 아카이브별 하위폴더를 사용해 같은 파일명 충돌을 막는다.
                            // 엔트리의 디렉터리 경로는 제거하고 실제 파일명만 저장한다.
                            string outName = Path.GetFileName(e.FileName);
                            string outPath = Path.Combine(archiveDir, outName);

                            if (File.Exists(outPath))
                            {
                                string stem = Path.GetFileNameWithoutExtension(outName);
                                string ext = Path.GetExtension(outName);
                                int dup = 2;
                                do
                                {
                                    outPath = Path.Combine(archiveDir, $"{stem}__dup{dup}{ext}");
                                    dup++;
                                }
                                while (File.Exists(outPath));
                            }

                            await File.WriteAllBytesAsync(outPath, data);

                            manifest.Add(string.Join("\t",
                                Path.GetFileName(idxPath),
                                Path.GetFileName(pak.PakPath),
                                e.FileName,
                                Path.GetRelativePath(outputRoot, outPath),
                                wasZlib ? "YES" : "NO",
                                data.Length.ToString()));

                            totalSpr++;

                            if (localDone % 100 == 0 || localDone == sprEntries.Count)
                            {
                                int percent = 5 + (int)(90.0 *
                                    (idxNo + localDone / (double)Math.Max(1, sprEntries.Count)) /
                                    Math.Max(1, idxFiles.Count));

                                SetProgress(percent,
                                    $"SPR 추출 중... {Path.GetFileName(idxPath)} {localDone:N0}/{sprEntries.Count:N0}");
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            Log($"[SPR 추출 실패] {Path.GetFileName(idxPath)} / {e.FileName}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Log($"[IDX 추출 건너뜀] {Path.GetFileName(idxPath)}: {ex.Message}");
                }
            }

            string manifestPath = Path.Combine(outputRoot, "SPR_MANIFEST.tsv");
            await File.WriteAllLinesAsync(manifestPath, manifest, Encoding.UTF8);

            string readmePath = Path.Combine(outputRoot, "README.txt");
            await File.WriteAllTextAsync(readmePath,
                "Lineage Sprite Studio V2.3.3 - SPR 전체 추출\r\n" +
                "==============================================\r\n" +
                $"클라이언트: {root}\r\n" +
                $"추출 SPR: {totalSpr:N0}개\r\n" +
                $"ZLIB 해제: {decodedZlib:N0}개\r\n" +
                $"실패: {failed:N0}개\r\n\r\n" +
                "각 Sprite IDX별 하위폴더에 실제 .spr 파일을 저장합니다.\r\n" +
                "ZLIB로 감싸져 있던 SPR은 자동으로 해제하여 일반 SPR 데이터로 저장합니다.\r\n" +
                "SPR_MANIFEST.tsv에서 원래 IDX/PAK/엔트리 위치를 확인할 수 있습니다.\r\n",
                Encoding.UTF8);

            SetProgress(100, $"SPR 전체 추출 완료 · {totalSpr:N0}개");
            Log($"[SPR 전체 추출] 완료: {totalSpr:N0}개 / ZLIB 해제 {decodedZlib:N0}개 / 실패 {failed:N0}개");
            Log($"[SPR 전체 추출] 저장 위치: {outputRoot}");

            MessageBox.Show(this,
                $"SPR 전체 추출 완료\n\nSPR: {totalSpr:N0}개\nZLIB 해제: {decodedZlib:N0}개\n실패: {failed:N0}개\n\n저장 위치:\n{outputRoot}",
                "SPR 전체 추출", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetProgress(0, "SPR 전체 추출 실패");
            Log("[SPR 전체 추출 오류] " + ex);
            MessageBox.Show(this, ex.Message, "SPR 전체 추출 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _watch.Stop();
            _elapsedTimer.Stop();
            SetBusy(false);
        }
    }

    private async Task RestoreLatestBackupAsync()
    {
        if (!Directory.Exists(_client.Text))
        {
            MessageBox.Show(this, "클라이언트 폴더를 먼저 선택하세요.", "원본 복원",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var backups = Directory.EnumerateDirectories(_client.Text, "SpriteStudio_Backup_*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (backups.Count == 0)
        {
            MessageBox.Show(this, "이 클라이언트 폴더에서 Sprite Studio 백업을 찾지 못했습니다.",
                "원본 복원", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string backupDir = backups[0];
        var fullPaks = Directory.EnumerateFiles(backupDir, "Sprite*.pak", SearchOption.TopDirectoryOnly).ToList();
        bool fullBackup = fullPaks.Count > 0;

        string note = fullBackup
            ? "IDX와 PAK 전체 원본을 복원합니다."
            : "이 백업은 구버전 백업(IDX + PAK 길이)입니다.\n외부 eat로 PAK 전체를 다시 묶은 뒤라면 완전 복원이 보장되지 않습니다.";

        if (MessageBox.Show(this,
            $"가장 최근 백업을 복원합니다.\n\n{Path.GetFileName(backupDir)}\n\n{note}\n\n게임을 완전히 종료한 뒤 진행하세요.",
            "원본 복원 확인", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        SetBusy(true);
        _watch.Restart();
        _elapsedTimer.Start();

        try
        {
            SetProgress(5, "원본 복원 준비 중...");

            foreach (var p in _opened) p.Dispose();
            _opened.Clear();

            if (fullBackup)
            {
                int i = 0;
                foreach (var pak in fullPaks)
                {
                    i++;
                    SetProgress(10 + (int)(65.0 * i / Math.Max(1, fullPaks.Count)),
                        $"PAK 원본 복원 중... {Path.GetFileName(pak)}");
                    string dst = Path.Combine(_client.Text, Path.GetFileName(pak));
                    await Task.Run(() => File.Copy(pak, dst, true));
                }
            }
            else
            {
                string lengthsPath = Path.Combine(backupDir, "pak_lengths.json");
                if (File.Exists(lengthsPath))
                {
                    var lengths = JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(lengthsPath))
                        ?? new Dictionary<string, long>();

                    foreach (var kv in lengths)
                    {
                        string dst = Path.Combine(_client.Text, kv.Key);
                        if (!File.Exists(dst)) continue;

                        using var fs = new FileStream(dst, FileMode.Open, FileAccess.Write, FileShare.None);
                        if (fs.Length >= kv.Value)
                            fs.SetLength(kv.Value);
                    }
                }
            }

            foreach (var idx in Directory.EnumerateFiles(backupDir, "Sprite*.idx", SearchOption.TopDirectoryOnly))
                File.Copy(idx, Path.Combine(_client.Text, Path.GetFileName(idx)), true);

            SetProgress(100, "원본 복원 완료");
            Log($"[원본 복원] {backupDir}");
            Log(fullBackup
                ? "[원본 복원] IDX + PAK 전체 원본 복원 완료"
                : "[원본 복원] 구버전 백업으로 IDX/PAK 길이 복원 완료");

            MessageBox.Show(this,
                fullBackup
                    ? "원본 IDX/PAK 전체 복원이 완료되었습니다."
                    : "구버전 백업 복원이 완료되었습니다.\n단, eat로 PAK 전체를 다시 묶은 상태였다면 원본 클라이언트의 PAK로 다시 복사하는 것이 가장 안전합니다.",
                "원본 복원", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetProgress(0, "원본 복원 실패");
            Log("[원본 복원 오류] " + ex);
            MessageBox.Show(this, ex.Message, "원본 복원 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _watch.Stop();
            _elapsedTimer.Stop();
            SetBusy(false);
        }
    }

    private void ShowFirstPngPreview()
    {
        if (!Directory.Exists(_png.Text)) return;
        try
        {
            var first = Directory.EnumerateFiles(_png.Text, "*.png", SearchOption.AllDirectories)
                .OrderBy(x => x, NaturalPathComparer.Instance)
                .FirstOrDefault();
            if (first == null)
            {
                Log("[PNG] 선택한 폴더와 하위폴더에서 PNG 파일을 찾지 못했습니다.");
                return;
            }

            using var src = System.Drawing.Image.FromFile(first);
            var copy = new Bitmap(src);
            var old = _newPreview.Image;
            _newPreview.Image = copy;
            old?.Dispose();
            Log($"[미리보기] {Path.GetFileName(first)}");
        }
        catch (Exception ex)
        {
            Log("[미리보기 오류] " + ex.Message);
        }
    }

    private void SelectPreviewFromGrid()
    {
        if (_grid.SelectedRows.Count == 0) return;
        if (!int.TryParse(Convert.ToString(_grid.SelectedRows[0].Cells[0].Value), out int part)) return;

        _previewPart = part;
        int mappedPart = GetMappedSourcePart(part);
        if (mappedPart >= 0)
            _sourceGroup.Value = Math.Clamp(mappedPart / 8, (int)_sourceGroup.Minimum, (int)_sourceGroup.Maximum);
        int originalCount = _originalFrames.TryGetValue(part, out var ofc) ? ofc : 0;
        int newCount = GetMappedPngFiles(part)?.Count ?? 0;
        int max = Math.Max(0, Math.Max(originalCount, newCount) - 1);

        _changingPreviewFrame = true;
        _previewFrame.Maximum = max;
        _previewFrame.Value = 0;
        _changingPreviewFrame = false;

        ShowSelectedPreview();
    }

    private void ShowSelectedPreview()
    {
        if (_previewPart < 0) return;

        int frame = (int)_previewFrame.Value;
        var target = _targets.FirstOrDefault(t => t.Part == _previewPart);
        int originalCount = _originalFrames.TryGetValue(_previewPart, out var ofc) ? ofc : 0;
        var files = GetMappedPngFiles(_previewPart);
        int newCount = files?.Count ?? 0;

        try
        {
            if (target != null && frame < originalCount)
            {
                var stored = target.Pak.Extract(target.Entry);
                var raw = SpriteCodec.DecodeIfNeeded(stored);
                var bmp = SprDecoder.DecodeFrame(raw, frame);
                var old = _originalPreview.Image;
                _originalPreview.Image = bmp;
                old?.Dispose();
            }
            else
            {
                var old = _originalPreview.Image;
                _originalPreview.Image = null;
                old?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log($"[원본 미리보기 오류] {_previewPart}-{frame}: {ex.Message}");
        }

        try
        {
            if (files != null && frame < files.Count)
            {
                using var src = System.Drawing.Image.FromFile(files[frame]);
                var copy = new Bitmap(src);
                var old = _newPreview.Image;
                _newPreview.Image = copy;
                old?.Dispose();
            }
            else
            {
                var old = _newPreview.Image;
                _newPreview.Image = null;
                old?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log($"[수정본 미리보기 오류] {_previewPart}-{frame}: {ex.Message}");
        }

        string fileName = target?.Entry.FileName ?? $"{(int)_gfx.Value}-{_previewPart}.spr";
        string directionText = _directionMap.SelectedItem?.ToString() ?? "방향 그대로";
        int mappedPart = GetMappedSourcePart(_previewPart);
        string overrideText = _groupMapOverrides.ContainsKey(_previewPart / 8) ? " · 수동그룹" : "";
        _previewInfo.Text = $"  {fileName} ← {(int)_gfx.Value}-{mappedPart}.spr · 프레임 {frame + 1} / 원본 {originalCount} · 수정 {newCount} · {directionText}{overrideText}";
    }

    private int GroupCount
    {
        get
        {
            int maxPart = Math.Max(
                _targets.Count == 0 ? 0 : _targets.Max(t => t.Part),
                _pngByPart.Count == 0 ? 0 : _pngByPart.Keys.Max());
            return Math.Max(1, maxPart / 8 + 1);
        }
    }

    private int GetMappedSourcePart(int targetPart)
    {
        if (_pngByPart.Count == 0) return -1;

        int groupCount = GroupCount;
        int targetGroup = targetPart / 8;
        int dir = targetPart % 8;

        int sourceGroup;
        if (_groupMapOverrides.TryGetValue(targetGroup, out int manualGroup))
            sourceGroup = Mod(manualGroup, groupCount);
        else
            sourceGroup = Mod(targetGroup + (int)_groupOffset.Value, groupCount);

        int sourceDir = dir;
        if (_directionMap.SelectedIndex == 1)
            sourceDir = (8 - dir) % 8;
        else if (_directionMap.SelectedIndex >= 2)
            sourceDir = (dir + (_directionMap.SelectedIndex - 1)) % 8;

        return sourceGroup * 8 + sourceDir;
    }

    private List<string>? GetMappedPngFiles(int targetPart)
    {
        int sourcePart = GetMappedSourcePart(targetPart);
        return sourcePart >= 0 && _pngByPart.TryGetValue(sourcePart, out var files) ? files : null;
    }

    private static int Mod(int value, int mod)
    {
        int r = value % mod;
        return r < 0 ? r + mod : r;
    }

    private void AutoFindFrameMapping()
    {
        if (_targets.Count == 0 || _pngByPart.Count == 0)
        {
            MessageBox.Show(this, "먼저 GFX 검색과 PNG 폴더 선택을 완료하세요.", "자동 맞춤",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int oldDirection = _directionMap.SelectedIndex;
        decimal oldOffset = _groupOffset.Value;
        _groupMapOverrides.Clear();

        int bestDirection = 0;
        int bestOffset = 0;
        int bestMismatch = int.MaxValue;
        int bestMissing = int.MaxValue;

        int groupCount = GroupCount;
        for (int direction = 0; direction < _directionMap.Items.Count; direction++)
        {
            _directionMap.SelectedIndex = direction;
            for (int offset = 0; offset < groupCount; offset++)
            {
                _groupOffset.Value = Math.Clamp(offset, (int)_groupOffset.Minimum, (int)_groupOffset.Maximum);

                int mismatch = 0;
                int missing = 0;
                foreach (var t in _targets)
                {
                    int original = _originalFrames.TryGetValue(t.Part, out var f) ? f : 0;
                    var files = GetMappedPngFiles(t.Part);
                    if (files == null || files.Count == 0) { missing++; continue; }
                    if (files.Count != original) mismatch++;
                }

                if (missing < bestMissing || (missing == bestMissing && mismatch < bestMismatch))
                {
                    bestMissing = missing;
                    bestMismatch = mismatch;
                    bestDirection = direction;
                    bestOffset = offset;
                }
            }
        }

        _directionMap.SelectedIndex = bestDirection;
        _groupOffset.Value = bestOffset;
        RebuildGrid();
        SelectPreviewFromGrid();

        Log($"[자동 맞춤] 방향={_directionMap.SelectedItem}, 그룹 오프셋={bestOffset}, 누락={bestMissing}, 프레임 불일치={bestMismatch}");
        MessageBox.Show(this,
            $"프레임 수 기준 자동 맞춤 결과\n\n방향: {_directionMap.SelectedItem}\n동작 그룹 오프셋: {bestOffset}\nPNG 누락: {bestMissing}\n프레임 불일치: {bestMismatch}\n\n"
            + "이 기능은 프레임 수만 비교합니다. 맨손/검/활 같은 실제 동작 내용은 오른쪽 미리보기로 확인하고 필요하면 선택 그룹 연결을 사용하세요.",
            "자동 맞춤 결과", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void MapSelectedGroup()
    {
        if (_previewPart < 0)
        {
            MessageBox.Show(this, "표에서 먼저 원본 SPR 행을 선택하세요.", "그룹 연결",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int targetGroup = _previewPart / 8;
        int sourceGroup = (int)_sourceGroup.Value;
        _groupMapOverrides[targetGroup] = sourceGroup;
        Log($"[그룹 연결] 원본 그룹 {targetGroup} ({(int)_gfx.Value}-{targetGroup * 8}~{(int)_gfx.Value}-{targetGroup * 8 + 7})"
            + $" ← 수정 그룹 {sourceGroup} ({(int)_gfx.Value}-{sourceGroup * 8}~{(int)_gfx.Value}-{sourceGroup * 8 + 7})");
        RebuildGrid();
        ReselectPart(_previewPart);
    }

    private void ClearSelectedGroupMap()
    {
        if (_previewPart < 0) return;
        int targetGroup = _previewPart / 8;
        if (_groupMapOverrides.Remove(targetGroup))
            Log($"[그룹 연결 해제] 원본 그룹 {targetGroup}");
        RebuildGrid();
        ReselectPart(_previewPart);
    }

    private void ReselectPart(int part)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (Convert.ToInt32(row.Cells[0].Value) != part) continue;
            _grid.ClearSelection();
            row.Selected = true;
            _grid.CurrentCell = row.Cells[0];
            break;
        }
        SelectPreviewFromGrid();
    }

    private void ToggleAutoPlay()
    {
        if (_autoPlayTimer.Enabled)
        {
            _autoPlayTimer.Stop();
            _autoPlayButton.Text = "▶ 자동재생";
            return;
        }

        if (_previewPart < 0) return;
        _autoPlayTimer.Start();
        _autoPlayButton.Text = "⏸ 정지";
    }

    private void AdvanceAutoPlay()
    {
        if (_previewPart < 0)
        {
            _autoPlayTimer.Stop();
            _autoPlayButton.Text = "▶ 자동재생";
            return;
        }

        // 현재 SPR만 반복할지, 61-0 ~ 마지막 SPR까지 순서대로 모두 재생할지 선택.
        if (!_playAllActions.Checked)
        {
            if (_previewFrame.Maximum <= 0)
            {
                ShowSelectedPreview();
                return;
            }

            if (_previewFrame.Value >= _previewFrame.Maximum)
                _previewFrame.Value = 0;
            else
                _previewFrame.Value++;
            return;
        }

        if (_previewFrame.Value < _previewFrame.Maximum)
        {
            _previewFrame.Value++;
            return;
        }

        // 현재 SPR의 마지막 프레임 다음에는 다음 SPR 행으로 이동.
        int currentIndex = _targets.FindIndex(t => t.Part == _previewPart);
        if (currentIndex < 0 || _targets.Count == 0)
            return;

        int nextIndex = (currentIndex + 1) % _targets.Count;
        int nextPart = _targets[nextIndex].Part;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (Convert.ToInt32(row.Cells[0].Value) != nextPart) continue;
            _grid.ClearSelection();
            row.Selected = true;
            _grid.CurrentCell = row.Cells[0];
            _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, row.Index - 2);
            break;
        }

        SelectPreviewFromGrid();
    }

    private static string FindCaseInsensitive(string dir, string name)
    {
        string direct = Path.Combine(dir, name);
        if (File.Exists(direct)) return direct;
        return Directory.EnumerateFiles(dir, "*.idx", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(x => Path.GetFileName(x).Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    private void SetBusy(bool busy)
    {
        if (InvokeRequired) { BeginInvoke(() => SetBusy(busy)); return; }
        _scan.Enabled = !busy;
        _deepScan.Enabled = !busy;
        _traceMap.Enabled = !busy;
        _apply.Enabled = !busy && _targets.Count > 0 && _pngByPart.Count > 0;
        _restore.Enabled = !busy;
        _extractAllSpr.Enabled = !busy;
        _backup.Enabled = !busy;
        _client.Enabled = !busy;
        _png.Enabled = !busy;
        _gfx.Enabled = !busy;
        _directionMap.Enabled = !busy;
        _groupOffset.Enabled = !busy;
        _sourceGroup.Enabled = !busy;
        _autoMap.Enabled = !busy;
        _playAllActions.Enabled = !busy;
        _mapSelectedGroup.Enabled = !busy;
        _clearSelectedGroupMap.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void SetProgress(int value, string text)
    {
        if (InvokeRequired) { BeginInvoke(() => SetProgress(value, text)); return; }
        _progress.Value = Math.Clamp(value, 0, 100);
        _status.Text = text;
    }

    private void ReportFromWorker(int value, string text) => SetProgress(value, text);

    private void Log(string line)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(line)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}

internal sealed class AnimatedProgressBar : Control
{
    private int _value;
    private int _shine;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 35 };

    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    public AnimatedProgressBar()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        _timer.Tick += (_, _) => { _shine = (_shine + 7) % Math.Max(1, Width + 80); Invalidate(); };
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        rect.Inflate(-1, -1);
        using var bg = new SolidBrush(Color.FromArgb(44, 48, 56));
        g.FillRectangle(bg, rect);

        int w = (int)Math.Round(rect.Width * (_value / 100.0));
        if (w > 0)
        {
            var fill = new Rectangle(rect.X, rect.Y, w, rect.Height);
            using var b = new System.Drawing.Drawing2D.LinearGradientBrush(fill, Color.FromArgb(55, 125, 245), Color.FromArgb(40, 190, 145), 0f);
            g.FillRectangle(b, fill);

            int sx = rect.X + _shine - 80;
            var shine = new Rectangle(sx, rect.Y, 80, rect.Height);
            using var sb = new System.Drawing.Drawing2D.LinearGradientBrush(shine,
                Color.FromArgb(0, Color.White), Color.FromArgb(100, Color.White), 0f);
            var blend = new System.Drawing.Drawing2D.ColorBlend
            {
                Colors = new[] { Color.FromArgb(0, Color.White), Color.FromArgb(90, Color.White), Color.FromArgb(0, Color.White) },
                Positions = new[] { 0f, .5f, 1f }
            };
            sb.InterpolationColors = blend;
            g.SetClip(fill);
            g.FillRectangle(sb, shine);
            g.ResetClip();
        }

        using var pen = new Pen(Color.FromArgb(80, 90, 105));
        g.DrawRectangle(pen, rect);
        string s = $"{_value}%";
        var size = g.MeasureString(s, Font);
        using var tb = new SolidBrush(Color.White);
        g.DrawString(s, Font, tb, rect.Left + (rect.Width - size.Width) / 2, rect.Top + (rect.Height - size.Height) / 2);
    }
}

internal sealed class NaturalPathComparer : IComparer<string>
{
    public static readonly NaturalPathComparer Instance = new();
    private static readonly Regex Token = new(@"\d+|\D+", RegexOptions.Compiled);

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        var a = Token.Matches(Path.GetFileName(x));
        var b = Token.Matches(Path.GetFileName(y));
        int n = Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++)
        {
            string sa = a[i].Value, sb = b[i].Value;
            int c;
            if (long.TryParse(sa, out var na) && long.TryParse(sb, out var nb)) c = na.CompareTo(nb);
            else c = string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return a.Count.CompareTo(b.Count);
    }
}
