using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace LineageSpriteStudio;

internal sealed class SprEditorPanel : UserControl
{
    private readonly PictureBox originalPreview = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(28, 31, 38)
    };

    private readonly PictureBox modifiedPreview = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(28, 31, 38)
    };

    private readonly Label originalInfo = new()
    {
        Dock = DockStyle.Bottom,
        Height = 78,
        Padding = new Padding(8),
        AutoEllipsis = true
    };

    private readonly Label modifiedInfo = new()
    {
        Dock = DockStyle.Bottom,
        Height = 78,
        Padding = new Padding(8),
        AutoEllipsis = true
    };

    private readonly NumericUpDown frame = new() { Minimum = 0, Maximum = 0, Width = 82 };
    private readonly Button autoPlay = new() { Text = "자동 ▶", AutoSize = true };
    private readonly Button extractFrame = new() { Text = "현재 프레임 PNG 추출", AutoSize = true };
    private readonly Button extractAll = new() { Text = "전체 프레임 PNG 추출", AutoSize = true };
    private readonly Button loadFramePng = new() { Text = "현재 프레임 PNG 불러오기", AutoSize = true };
    private readonly Button loadPngFolder = new() { Text = "PNG 폴더로 SPR 재생성", AutoSize = true };
    private readonly Button loadModifiedSpr = new() { Text = "수정 SPR 불러오기", AutoSize = true };
    private readonly Button tintAll = new() { Text = "전체 SPR 색상 테스트", AutoSize = true };
    private readonly Button reset = new() { Text = "수정본 초기화", AutoSize = true };
    private readonly Button saveBack = new() { Text = "현재 수정 SPR 원본에 저장", AutoSize = true };
    private readonly Label frameInfo = new() { AutoSize = true, Margin = new Padding(8, 7, 0, 0) };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 120 };

    private byte[]? originalStored;
    private byte[]? originalDecoded;
    private byte[]? modifiedDecoded;
    private SprFormatInfo originalFormat = new(0, false, 0, 0);
    private bool originalWasZlib;
    private string displayName = "selected.spr";
    private string sourceInfo = "";
    private bool rendering;

    public Func<byte[], Task>? SaveBackAsync { get; set; }

    public SprEditorPanel()
    {
        Dock = DockStyle.Fill;
        Visible = false;

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 82,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoScroll = true,
            Padding = new Padding(6)
        };

        toolbar.Controls.Add(new Label { Text = "프레임", AutoSize = true, Margin = new Padding(3, 8, 2, 0) });
        toolbar.Controls.Add(frame);
        toolbar.Controls.Add(autoPlay);
        toolbar.Controls.Add(frameInfo);
        toolbar.Controls.Add(extractFrame);
        toolbar.Controls.Add(extractAll);
        toolbar.Controls.Add(loadFramePng);
        toolbar.Controls.Add(loadPngFolder);
        toolbar.Controls.Add(loadModifiedSpr);
        toolbar.Controls.Add(tintAll);
        toolbar.Controls.Add(reset);
        toolbar.Controls.Add(saveBack);

        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(originalPreview);
        left.Controls.Add(originalInfo);

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(modifiedPreview);
        right.Controls.Add(modifiedInfo);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 350
        };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        Controls.Add(split);
        Controls.Add(toolbar);

        frame.ValueChanged += async (_, _) => await RenderAsync();
        autoPlay.Click += (_, _) => ToggleAutoPlay();
        timer.Tick += (_, _) => AdvanceFrame();

        extractFrame.Click += async (_, _) => await ExtractCurrentFrameAsync();
        extractAll.Click += async (_, _) => await ExtractAllFramesAsync();
        loadFramePng.Click += async (_, _) => await ReplaceCurrentFrameFromPngAsync();
        loadPngFolder.Click += async (_, _) => await RebuildFromPngFolderAsync();
        loadModifiedSpr.Click += async (_, _) => await LoadModifiedSprAsync();
        tintAll.Click += (_, _) => TintWholeSpr();
        reset.Click += async (_, _) => await ResetModifiedAsync();
        saveBack.Click += async (_, _) => await SaveBackToOriginalAsync();
    }

    public void LoadSpr(byte[] stored, string name, string source)
    {
        StopAutoPlay();

        originalStored = (byte[])stored.Clone();
        originalWasZlib = SpriteCodec.IsZlib(stored);
        originalDecoded = SpriteCodec.DecodeIfNeeded(stored);
        modifiedDecoded = (byte[])originalDecoded.Clone();

        originalFormat = SprInfo.Analyze(originalDecoded);
        displayName = string.IsNullOrWhiteSpace(name) ? "selected.spr" : Path.GetFileName(name);
        sourceInfo = source;

        frame.Minimum = 0;
        frame.Maximum = Math.Max(0, originalFormat.FrameCount - 1);
        frame.Value = 0;

        UpdateInfoLabels();
        Visible = true;
        BringToFront();

        _ = RenderAsync();

        if (originalFormat.FrameCount > 1)
            StartAutoPlay();
    }

    public void ClearSpr()
    {
        StopAutoPlay();
        originalStored = null;
        originalDecoded = null;
        modifiedDecoded = null;
        originalFormat = new SprFormatInfo(0, false, 0, 0);

        originalPreview.Image?.Dispose();
        originalPreview.Image = null;
        modifiedPreview.Image?.Dispose();
        modifiedPreview.Image = null;

        originalInfo.Text = "";
        modifiedInfo.Text = "";
        frameInfo.Text = "";
        Visible = false;
    }

    private void UpdateInfoLabels()
    {
        if (originalStored == null || originalDecoded == null || modifiedDecoded == null)
            return;

        var mod = SprInfo.Analyze(modifiedDecoded);

        originalInfo.Text =
            $"원본 SPR | {originalFormat.FrameCount}프레임 | " +
            $"{(originalFormat.IsPalette ? $"Palette {originalFormat.PaletteSize}" : "RGB555")} | " +
            $"Type {originalFormat.FrameType} | {(originalWasZlib ? "ZLIB" : "RAW")} | " +
            $"{originalStored.LongLength:N0} bytes\r\n위치: {sourceInfo}";

        byte[] modifiedStored = GetModifiedStoredBytes();
        modifiedInfo.Text =
            $"수정 SPR | {mod.FrameCount}프레임 | " +
            $"{(mod.IsPalette ? $"Palette {mod.PaletteSize}" : "RGB555")} | " +
            $"Type {mod.FrameType} | 저장 {(originalWasZlib ? "ZLIB" : "RAW")} | " +
            $"{modifiedStored.LongLength:N0} bytes";

        frameInfo.Text = $"0 / {Math.Max(0, originalFormat.FrameCount - 1)}";
    }

    private byte[] GetModifiedStoredBytes()
    {
        if (modifiedDecoded == null)
            return Array.Empty<byte>();

        return originalWasZlib
            ? SpriteCodec.EncodeZlib(modifiedDecoded)
            : (byte[])modifiedDecoded.Clone();
    }

    private async Task RenderAsync()
    {
        if (originalDecoded == null || modifiedDecoded == null || rendering)
            return;

        rendering = true;
        try
        {
            int index = (int)frame.Value;
            byte[] org = originalDecoded;
            byte[] mod = modifiedDecoded;

            var pair = await Task.Run(() =>
            {
                Bitmap a = SprDecoder.DecodeFrame(org, index);
                Bitmap b = SprDecoder.DecodeFrame(mod, index);
                return (a, b);
            });

            originalPreview.Image?.Dispose();
            originalPreview.Image = pair.a;
            modifiedPreview.Image?.Dispose();
            modifiedPreview.Image = pair.b;

            frameInfo.Text = $"{index} / {Math.Max(0, originalFormat.FrameCount - 1)}";
        }
        catch (Exception ex)
        {
            modifiedInfo.Text = "SPR 프레임 표시 실패: " + ex.Message;
            StopAutoPlay();
        }
        finally
        {
            rendering = false;
        }
    }

    private void ToggleAutoPlay()
    {
        if (timer.Enabled) StopAutoPlay();
        else StartAutoPlay();
    }

    private void StartAutoPlay()
    {
        if (frame.Maximum <= 0)
            return;

        timer.Start();
        autoPlay.Text = "정지 ■";
    }

    private void StopAutoPlay()
    {
        timer.Stop();
        autoPlay.Text = "자동 ▶";
    }

    private void AdvanceFrame()
    {
        if (rendering || originalDecoded == null)
            return;

        decimal next = frame.Value + 1;
        if (next > frame.Maximum)
            next = frame.Minimum;
        frame.Value = next;
    }

    private string GetSprExtractFolder()
    {
        string stem = Path.GetFileNameWithoutExtension(displayName);
        foreach (char c in Path.GetInvalidFileNameChars())
            stem = stem.Replace(c, '_');

        return Path.Combine(AppContext.BaseDirectory, "Extracted", "SPR", stem);
    }

    private async Task ExtractCurrentFrameAsync()
    {
        if (originalDecoded == null)
            return;

        try
        {
            string folder = GetSprExtractFolder();
            Directory.CreateDirectory(folder);
            int index = (int)frame.Value;
            string path = Path.Combine(folder, $"frame_{index:D3}.png");

            using Bitmap bmp = await Task.Run(() => SprDecoder.DecodeFrame(originalDecoded, index));
            bmp.Save(path, ImageFormat.Png);

            MessageBox.Show(this,
                $"현재 원본 프레임을 추출했습니다.\n\n{path}",
                "SPR 프레임 추출",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SPR 프레임 추출 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ExtractAllFramesAsync()
    {
        if (originalDecoded == null)
            return;

        try
        {
            string folder = GetSprExtractFolder();
            Directory.CreateDirectory(folder);

            int count = originalFormat.FrameCount;
            for (int i = 0; i < count; i++)
            {
                using Bitmap bmp = await Task.Run(() => SprDecoder.DecodeFrame(originalDecoded, i));
                bmp.Save(Path.Combine(folder, $"frame_{i:D3}.png"), ImageFormat.Png);
            }

            MessageBox.Show(this,
                $"원본 SPR 전체 {count:N0}프레임을 PNG로 추출했습니다.\n\n{folder}",
                "SPR 전체 프레임 추출",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "SPR 전체 추출 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ReplaceCurrentFrameFromPngAsync()
    {
        if (modifiedDecoded == null || originalDecoded == null)
            return;

        using var dlg = new OpenFileDialog
        {
            Title = "현재 프레임에 사용할 PNG 선택",
            Filter = "PNG 이미지 (*.png)|*.png|이미지 파일|*.png;*.bmp;*.jpg;*.jpeg"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        int index = (int)frame.Value;

        try
        {
            using Bitmap expected = SprDecoder.DecodeFrame(originalDecoded, index);
            using var supplied = new Bitmap(dlg.FileName);

            if (supplied.Width != expected.Width || supplied.Height != expected.Height)
            {
                MessageBox.Show(this,
                    $"프레임 크기가 다릅니다.\n원본: {expected.Width}×{expected.Height}\n수정: {supplied.Width}×{supplied.Height}",
                    "PNG 적용 중지",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string temp = Path.Combine(Path.GetTempPath(), "LineageInspectorSpr_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            try
            {
                var files = new List<string>(originalFormat.FrameCount);

                for (int i = 0; i < originalFormat.FrameCount; i++)
                {
                    string p = Path.Combine(temp, $"frame_{i:D3}.png");
                    if (i == index)
                    {
                        supplied.Save(p, ImageFormat.Png);
                    }
                    else
                    {
                        using Bitmap bmp = SprDecoder.DecodeFrame(modifiedDecoded, i);
                        bmp.Save(p, ImageFormat.Png);
                    }
                    files.Add(p);
                }

                modifiedDecoded = await Task.Run(() =>
                    SprEncoder.CreateFromPngs(files, originalFormat.IsPalette, originalFormat.FrameType));

                UpdateInfoLabels();
                await RenderAsync();
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "현재 프레임 PNG 적용 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RebuildFromPngFolderAsync()
    {
        if (originalDecoded == null)
            return;

        using var dlg = new FolderBrowserDialog
        {
            Description = $"PNG {originalFormat.FrameCount}장을 포함한 폴더를 선택하세요."
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var files = Directory.EnumerateFiles(dlg.SelectedPath)
                .Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, NaturalPathComparer.Instance)
                .ToList();

            if (files.Count != originalFormat.FrameCount)
            {
                MessageBox.Show(this,
                    $"PNG 수가 원본 SPR 프레임 수와 다릅니다.\n원본: {originalFormat.FrameCount}프레임\nPNG: {files.Count}장",
                    "PNG 폴더 적용 중지",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            for (int i = 0; i < files.Count; i++)
            {
                using Bitmap expected = SprDecoder.DecodeFrame(originalDecoded, i);
                using var supplied = new Bitmap(files[i]);

                if (expected.Width != supplied.Width || expected.Height != supplied.Height)
                {
                    MessageBox.Show(this,
                        $"frame {i} 크기가 다릅니다.\n원본: {expected.Width}×{expected.Height}\nPNG: {supplied.Width}×{supplied.Height}\n파일: {files[i]}",
                        "PNG 폴더 적용 중지",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            var answer = MessageBox.Show(this,
                "PNG 전체 프레임으로 SPR을 다시 생성합니다.\n" +
                "원본의 Palette/RGB555 방식과 FrameType은 유지합니다.\n진행할까요?",
                "SPR 재생성",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
                return;

            modifiedDecoded = await Task.Run(() =>
                SprEncoder.CreateFromPngs(files, originalFormat.IsPalette, originalFormat.FrameType));

            UpdateInfoLabels();
            await RenderAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "PNG 폴더 SPR 재생성 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task LoadModifiedSprAsync()
    {
        if (originalDecoded == null)
            return;

        using var dlg = new OpenFileDialog
        {
            Title = "수정된 SPR 선택",
            Filter = "SPR 파일 (*.spr)|*.spr|모든 파일|*.*"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            byte[] raw = await File.ReadAllBytesAsync(dlg.FileName);
            byte[] decoded = SpriteCodec.DecodeIfNeeded(raw);
            var info = SprInfo.Analyze(decoded);

            if (info.FrameCount != originalFormat.FrameCount)
            {
                MessageBox.Show(this,
                    $"프레임 수가 다릅니다.\n원본: {originalFormat.FrameCount}\n수정 SPR: {info.FrameCount}",
                    "수정 SPR 적용 중지",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            modifiedDecoded = decoded;
            UpdateInfoLabels();
            await RenderAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "수정 SPR 읽기 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void TintWholeSpr()
    {
        if (originalDecoded == null)
            return;

        using var dlg = new ColorDialog
        {
            FullOpen = true,
            Color = Color.CornflowerBlue
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            modifiedDecoded = SprBinaryColorEditor.TintAll(originalDecoded, dlg.Color, 0.42f);
            UpdateInfoLabels();
            _ = RenderAsync();

            modifiedInfo.Text += $"\r\n전체 색상 테스트: {ColorTranslator.ToHtml(dlg.Color)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "SPR 색상 테스트 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ResetModifiedAsync()
    {
        if (originalDecoded == null)
            return;

        modifiedDecoded = (byte[])originalDecoded.Clone();
        UpdateInfoLabels();
        await RenderAsync();
    }

    private async Task SaveBackToOriginalAsync()
    {
        if (modifiedDecoded == null || SaveBackAsync == null)
            return;

        var answer = MessageBox.Show(this,
            $"현재 수정 SPR을 원본에 저장합니다.\n\n{displayName}\n\n저장 전에 원본 백업을 생성합니다.",
            "현재 수정 SPR 원본에 저장",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        try
        {
            StopAutoPlay();
            byte[] stored = GetModifiedStoredBytes();
            await SaveBackAsync(stored);

            originalStored = (byte[])stored.Clone();
            originalDecoded = (byte[])modifiedDecoded.Clone();
            originalFormat = SprInfo.Analyze(originalDecoded);

            UpdateInfoLabels();
            await RenderAsync();

            MessageBox.Show(this,
                "현재 수정 SPR을 원본에 저장했습니다.",
                "SPR 저장 완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "SPR 원본 저장 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop();
            timer.Dispose();
            originalPreview.Image?.Dispose();
            modifiedPreview.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class NaturalPathComparer : IComparer<string>
    {
        public static readonly NaturalPathComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            x ??= "";
            y ??= "";

            string ax = Path.GetFileName(x);
            string ay = Path.GetFileName(y);

            var a = Regex.Split(ax, "(\\d+)");
            var b = Regex.Split(ay, "(\\d+)");
            int count = Math.Min(a.Length, b.Length);

            for (int i = 0; i < count; i++)
            {
                bool an = long.TryParse(a[i], out long av);
                bool bn = long.TryParse(b[i], out long bv);

                int cmp;
                if (an && bn) cmp = av.CompareTo(bv);
                else cmp = string.Compare(a[i], b[i], StringComparison.OrdinalIgnoreCase);

                if (cmp != 0) return cmp;
            }

            return a.Length.CompareTo(b.Length);
        }
    }
}

internal static class SprBinaryColorEditor
{
    public static byte[] TintAll(byte[] source, Color tint, float blend)
    {
        if (source == null || source.Length == 0)
            throw new InvalidDataException("SPR 데이터가 비어 있습니다.");

        byte[] data = (byte[])source.Clone();
        int p = 0;
        int first = data[p++];

        if (first == 255)
        {
            if (p >= data.Length)
                throw new InvalidDataException("SPR palette header가 잘못되었습니다.");

            int storedCount = data[p++];
            int paletteCount = storedCount == 0 ? 256 : storedCount;

            if (p + paletteCount * 2 > data.Length)
                throw new InvalidDataException("SPR palette 범위가 잘못되었습니다.");

            for (int i = 0; i < paletteCount; i++)
            {
                ushort c = BitConverter.ToUInt16(data, p);
                ushort changed = Tint555(c, tint, blend);
                data[p] = (byte)(changed & 0xFF);
                data[p + 1] = (byte)(changed >> 8);
                p += 2;
            }

            return data;
        }

        int frameCount = first;
        if (frameCount <= 0 || frameCount > 254)
            throw new InvalidDataException("SPR 프레임 수가 잘못되었습니다.");

        for (int i = 0; i < frameCount; i++)
        {
            if (p + 14 > data.Length)
                throw new InvalidDataException("SPR frame header가 잘렸습니다.");

            int blockCount = BitConverter.ToUInt16(data, p + 12);
            p += 14;

            int defsSize = checked(blockCount * 5);
            if (p + defsSize > data.Length)
                throw new InvalidDataException("SPR frame block 정의가 잘렸습니다.");

            p += defsSize;
        }

        if (p + 4 > data.Length)
            throw new InvalidDataException("SPR block table이 없습니다.");

        int blockCountTotal = BitConverter.ToInt32(data, p);
        p += 4;

        if (blockCountTotal < 0 || blockCountTotal > 1_000_000)
            throw new InvalidDataException("SPR block table 크기가 잘못되었습니다.");

        if (p + blockCountTotal * 4L + 4 > data.Length)
            throw new InvalidDataException("SPR block offset table이 잘렸습니다.");

        int[] offsets = new int[blockCountTotal];
        for (int i = 0; i < blockCountTotal; i++)
        {
            offsets[i] = BitConverter.ToInt32(data, p);
            p += 4;
        }

        _ = BitConverter.ToInt32(data, p);
        p += 4;
        int dataStart = p;

        foreach (int off in offsets)
        {
            int q = checked(dataStart + off);
            if (q < dataStart || q + 4 > data.Length)
                continue;

            _ = data[q++];
            _ = data[q++];
            _ = data[q++];
            int lineCount = data[q++];

            for (int line = 0; line < lineCount; line++)
            {
                if (q >= data.Length) break;
                int segCount = data[q++];

                for (int seg = 0; seg < segCount; seg++)
                {
                    if (q + 2 > data.Length) break;

                    _ = data[q++];
                    int pixelCount = data[q++];

                    for (int px = 0; px < pixelCount; px++)
                    {
                        if (q + 2 > data.Length) break;

                        ushort c = BitConverter.ToUInt16(data, q);
                        ushort changed = Tint555(c, tint, blend);
                        data[q] = (byte)(changed & 0xFF);
                        data[q + 1] = (byte)(changed >> 8);
                        q += 2;
                    }
                }
            }
        }

        return data;
    }

    private static ushort Tint555(ushort c, Color tint, float blend)
    {
        int r5 = (c >> 10) & 0x1F;
        int g5 = (c >> 5) & 0x1F;
        int b5 = c & 0x1F;

        int r = (r5 << 3) | (r5 >> 2);
        int g = (g5 << 3) | (g5 >> 2);
        int b = (b5 << 3) | (b5 >> 2);

        int nr = (int)Math.Clamp(r * (1f - blend) + tint.R * blend, 0, 255);
        int ng = (int)Math.Clamp(g * (1f - blend) + tint.G * blend, 0, 255);
        int nb = (int)Math.Clamp(b * (1f - blend) + tint.B * blend, 0, 255);

        return (ushort)(((nr >> 3) << 10) | ((ng >> 3) << 5) | (nb >> 3));
    }
}
