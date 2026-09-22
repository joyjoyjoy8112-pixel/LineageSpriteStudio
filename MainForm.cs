using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LineageSpriteStudio;

public sealed class MainForm : Form
{
    private readonly TextBox _client = new() { Dock = DockStyle.Fill };
    private readonly TextBox _png = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _gfx = new() { Minimum = 0, Maximum = 999999, Value = 61, Width = 100 };
    private readonly Button _scan = new() { Text = "GFX 전체 검색", AutoSize = true };
    private readonly Button _apply = new() { Text = "백업 후 클라이언트에 적용", AutoSize = true, Enabled = false };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(24, 26, 31), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.None };
    private readonly Label _status = new() { Text = "대기 중", AutoSize = true };
    private readonly Label _elapsed = new() { Text = "00:00", AutoSize = true };
    private readonly AnimatedProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 28 };
    private readonly CheckBox _backup = new() { Text = "적용 전 복구용 백업 생성", Checked = true, AutoSize = true };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(18, 20, 24) };

    private readonly List<SpriteTarget> _targets = new();
    private readonly Dictionary<int, List<string>> _pngByPart = new();
    private readonly Dictionary<int, int> _originalFrames = new();
    private readonly List<SpritePak> _opened = new();
    private readonly Stopwatch _watch = new();
    private readonly System.Windows.Forms.Timer _elapsedTimer = new() { Interval = 250 };

    public MainForm()
    {
        Text = "리니지 Sprite Studio V2.2";
        Width = 1260;
        Height = 820;
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildUi();
        _elapsedTimer.Tick += (_, _) => _elapsed.Text = _watch.Elapsed.ToString(@"mm\:ss");
        _scan.Click += async (_, _) => await ScanAsync();
        _apply.Click += async (_, _) => await ApplyAsync();
        _grid.SelectionChanged += (_, _) => ShowSelectedPreview();

        FormClosed += (_, _) =>
        {
            foreach (var p in _opened) p.Dispose();
            _preview.Image?.Dispose();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var title = new Label
        {
            Text = "Lineage Sprite Studio V2.2  ·  전체 Sprite00~15 자동 추적/검증",
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
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(10, 0, 0, 0) };
        buttons.Controls.Add(_scan);
        buttons.Controls.Add(_apply);
        buttons.Controls.Add(_backup);
        settings.Controls.Add(buttons, 3, 1);
        settings.SetColumnSpan(buttons, 2);

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
            }
        };
        root.Controls.Add(settings, 0, 1);

        _grid.Columns.Add("Part", "동작");
        _grid.Columns.Add("File", "SPR 파일");
        _grid.Columns.Add("Pak", "실제 PAK");
        _grid.Columns.Add("Frames", "원본 프레임");
        _grid.Columns.Add("Png", "새 PNG");
        _grid.Columns.Add("Hash", "분배 검사");
        root.Controls.Add(_grid, 0, 2);

        var lower = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 840 };
        lower.Panel1.Controls.Add(_log);
        lower.Panel2.Controls.Add(_preview);
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
            SetProgress(1, "IDX 분석 중...");
            _targets.Clear();
            _originalFrames.Clear();
            foreach (var p in _opened) p.Dispose();
            _opened.Clear();

            await Task.Run(() =>
            {
                for (int n = 0; n < 16; n++)
                {
                    string idx = FindCaseInsensitive(_client.Text, $"Sprite{n:00}.idx");
                    if (string.IsNullOrEmpty(idx)) continue;
                    var pak = new SpritePak(idx);
                    _opened.Add(pak);
                    var rx = new Regex($"^{gfx}-(\\d+)\\.spr$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                    foreach (var e in pak.Entries)
                    {
                        var m = rx.Match(e.FileName);
                        if (!m.Success) continue;
                        int part = int.Parse(m.Groups[1].Value);
                        _targets.Add(new SpriteTarget(n, idx, pak, e, part));
                    }
                    ReportFromWorker(3 + n * 3, $"Sprite{n:00}.idx 분석 중...");
                }

                foreach (var t in _targets)
                {
                    var raw = t.Pak.Extract(t.Entry);
                    _originalFrames[t.Part] = SprInfo.FrameCount(raw);
                }
            });

            _targets.Sort((a, b) => a.Part.CompareTo(b.Part));
            LoadPngMap();
            RebuildGrid();

            int totalFrames = _originalFrames.Values.Sum();
            int pngCount = _pngByPart.Values.Sum(x => x.Count);
            Log($"[검색] GFX {gfx}: {_targets.Count}개 동작 SPR 찾음");
            Log($"[검색] 원본 프레임 합계: {totalFrames} / 새 PNG 인식: {pngCount}");

            var misplaced = _targets.Where(t => SpritePak.ExpectedPakIndex(t.Entry.FileName) != t.PakIndex).ToList();
            if (misplaced.Count == 0)
                Log("[분배] 모든 SPR이 파일명 byte합 % 16 규칙과 일치합니다.");
            else
                Log($"[경고] PAK 분배 불일치 {misplaced.Count}개 발견");

            SetProgress(100, $"검색 완료 · {_targets.Count}개 SPR");
            _apply.Enabled = _targets.Count > 0 && _pngByPart.Count > 0;
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

    private void LoadPngMap()
    {
        _pngByPart.Clear();
        if (!Directory.Exists(_png.Text)) return;
        int gfx = (int)_gfx.Value;
        var rx = new Regex($"^{gfx}-(?<part>\\d+).*?frame[_-]?(?<frame>\\d+)\\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (var f in Directory.EnumerateFiles(_png.Text, "*.png", SearchOption.TopDirectoryOnly))
        {
            var m = rx.Match(Path.GetFileName(f));
            if (!m.Success) continue;
            int part = int.Parse(m.Groups["part"].Value);
            if (!_pngByPart.TryGetValue(part, out var list)) _pngByPart[part] = list = new List<string>();
            list.Add(f);
        }

        foreach (var pair in _pngByPart)
            pair.Value.Sort(NaturalPathComparer.Instance);

        RebuildGrid();
    }

    private void RebuildGrid()
    {
        if (InvokeRequired) { BeginInvoke(RebuildGrid); return; }
        _grid.Rows.Clear();
        foreach (var t in _targets)
        {
            int frames = _originalFrames.TryGetValue(t.Part, out var fc) ? fc : 0;
            int png = _pngByPart.TryGetValue(t.Part, out var list) ? list.Count : 0;
            int expected = SpritePak.ExpectedPakIndex(t.Entry.FileName);
            string dist = expected == t.PakIndex ? "OK" : $"예상 {expected:00}";
            int row = _grid.Rows.Add(t.Part, t.Entry.FileName, $"Sprite{t.PakIndex:00}.pak", frames, png, dist);
            if (png != frames) _grid.Rows[row].DefaultCellStyle.BackColor = Color.MistyRose;
        }
    }

    private async Task ApplyAsync()
    {
        if (_targets.Count == 0) { await ScanAsync(); if (_targets.Count == 0) return; }
        LoadPngMap();

        var missing = _targets.Where(t => !_pngByPart.TryGetValue(t.Part, out var p) || p.Count == 0).ToList();
        var mismatch = _targets.Where(t => _pngByPart.TryGetValue(t.Part, out var p) && _originalFrames.TryGetValue(t.Part, out var f) && p.Count != f).ToList();
        if (missing.Count > 0 || mismatch.Count > 0)
        {
            string msg = $"적용을 중단했습니다.\n\nPNG가 없는 동작: {missing.Count}개\n원본 프레임 수와 다른 동작: {mismatch.Count}개\n\n전체 168개 동작을 정확히 대응시킨 뒤 적용해야 게임에서 원본이 남지 않습니다.";
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

            if (_backup.Checked)
            {
                backupDir = Path.Combine(_client.Text, "SpriteStudio_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(backupDir);
                foreach (var pak in uniquePaks)
                {
                    File.Copy(pak.IdxPath, Path.Combine(backupDir, Path.GetFileName(pak.IdxPath)), true);
                    originals[pak.PakPath] = new FileInfo(pak.PakPath).Length;
                }
                var manifest = originals.ToDictionary(k => Path.GetFileName(k.Key), v => v.Value);
                File.WriteAllText(Path.Combine(backupDir, "pak_lengths.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                Log($"[백업] {backupDir}");
            }
            else
            {
                foreach (var pak in uniquePaks) originals[pak.PakPath] = new FileInfo(pak.PakPath).Length;
            }

            int done = 0;
            int total = _targets.Count;
            foreach (var t in _targets)
            {
                var pngs = _pngByPart[t.Part];
                SetProgress(8 + (int)(42.0 * done / total), $"SPR 생성 중... {done + 1}/{total} · {t.Entry.FileName}");
                byte[] spr = await Task.Run(() => SprEncoder.CreateFromPngs(pngs));
                generated[t.Entry.FileName] = spr;
                done++;
            }

            done = 0;
            foreach (var group in _targets.GroupBy(t => t.PakIndex).OrderBy(g => g.Key))
            {
                var pak = group.First().Pak;
                foreach (var t in group)
                {
                    var spr = generated[t.Entry.FileName];
                    SetProgress(52 + (int)(28.0 * done / total), $"PAK 적용 중... {done + 1}/{total} · Sprite{t.PakIndex:00}.pak");
                    long offset = await Task.Run(() => pak.AppendRaw(spr));
                    t.Entry.Offset = offset;
                    t.Entry.FileSize = spr.Length;
                    t.Entry.CompressedSize = 0;
                    t.Entry.Flags = 0;
                    done++;
                }
                await Task.Run(pak.SaveIndex);
            }

            SetProgress(82, "적용 결과 재검증 중...");
            int verified = 0;
            foreach (var group in _targets.GroupBy(t => t.PakIndex).OrderBy(g => g.Key))
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
            MessageBox.Show(this,
                $"적용 및 재검증 완료\n\nSPR: {verified}/{total}\nPNG: {_pngByPart.Values.Sum(x => x.Count)}프레임\n\n이제 게임을 완전히 종료 후 다시 실행해서 확인하세요.",
                "V2.2 적용 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                    foreach (var f in Directory.EnumerateFiles(backupDir, "Sprite*.idx"))
                    {
                        string dst = Path.Combine(_client.Text, Path.GetFileName(f));
                        File.Copy(f, dst, true);
                    }
                }
                Log("[복구] 적용 전 상태로 IDX/PAK 길이를 복구했습니다.");
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

    private void ShowSelectedPreview()
    {
        if (_grid.SelectedRows.Count == 0) return;
        if (!int.TryParse(Convert.ToString(_grid.SelectedRows[0].Cells[0].Value), out int part)) return;
        if (!_pngByPart.TryGetValue(part, out var files) || files.Count == 0) return;
        try
        {
            using var src = Image.FromFile(files[0]);
            var copy = new Bitmap(src);
            var old = _preview.Image;
            _preview.Image = copy;
            old?.Dispose();
        }
        catch { }
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
        _apply.Enabled = !busy && _targets.Count > 0 && _pngByPart.Count > 0;
        _client.Enabled = !busy;
        _png.Enabled = !busy;
        _gfx.Enabled = !busy;
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
