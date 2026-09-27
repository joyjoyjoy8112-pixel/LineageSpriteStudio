using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;

namespace LineageSpriteStudio;

internal sealed class ClientInspectorForm : Form
{
    private readonly TextBox txtRoot = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Button btnBrowse = new() { Text = "클라 폴더 선택", AutoSize = true };
    private readonly Button btnScan = new() { Text = "전체 검사", AutoSize = true };
    private readonly Button btnKnight61 = new() { Text = "기사 61 바로 찾기", AutoSize = true };
    private readonly ComboBox cboGroup = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly TextBox txtSearch = new() { Width = 240, PlaceholderText = "예: 61-  /  .spr  /  14592" };
    private readonly Button btnSearch = new() { Text = "검색", AutoSize = true };
    private readonly Button btnAnalyzeSpr = new() { Text = "검색결과 SPR 분석", AutoSize = true };
    private readonly Button btnExtract = new() { Text = "선택 원본 추출", AutoSize = true };
    private readonly Button btnExport = new() { Text = "현재 목록 TSV 저장", AutoSize = true };
    private readonly Label lblSummary = new() { AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
    private readonly ToolStripStatusLabel status = new() { Text = "준비" };
    private readonly ToolStripProgressBar progress = new() { Minimum = 0, Maximum = 100, Width = 180 };

    private readonly DataGridView gridEntries = NewGrid();
    private readonly DataGridView gridPacks = NewGrid();
    private readonly DataGridView gridFiles = NewGrid();

    private readonly PictureBox preview = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(26, 29, 36)
    };
    private readonly NumericUpDown numFrame = new() { Minimum = 0, Maximum = 0, Width = 90 };
    private readonly Label lblPreviewInfo = new() { AutoSize = true, Padding = new Padding(6) };

    private readonly BindingList<PackRow> packRows = new();
    private readonly BindingList<FileRow> fileRows = new();
    private readonly List<EntryRow> allEntries = new();
    private List<EntryRow> currentEntries = new();
    private readonly Dictionary<string, AnyPakScanner> scanners = new(StringComparer.OrdinalIgnoreCase);

    private string? rootPath;

    public ClientInspectorForm()
    {
        Text = "Lineage Client Inspector - 현재 클라 전체 확인";
        Width = 1560;
        Height = 920;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Malgun Gothic", 9F);
        MinimumSize = new Size(1180, 720);

        cboGroup.Items.AddRange(new object[] { "전체", "Sprite", "Image", "Data", "Tile", "Text", "Sound", "기타" });
        cboGroup.SelectedIndex = 0;

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 42, ColumnCount = 9, Padding = new Padding(6) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 1; i < 9; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(txtRoot, 0, 0);
        top.Controls.Add(btnBrowse, 1, 0);
        top.Controls.Add(btnScan, 2, 0);
        top.Controls.Add(cboGroup, 3, 0);
        top.Controls.Add(txtSearch, 4, 0);
        top.Controls.Add(btnSearch, 5, 0);
        top.Controls.Add(btnKnight61, 6, 0);
        top.Controls.Add(btnAnalyzeSpr, 7, 0);
        top.Controls.Add(lblSummary, 8, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var tabEntries = new TabPage("IDX/PAK 내부 전체");
        var entrySplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 1120 };
        entrySplit.Panel1.Controls.Add(gridEntries);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var frameBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(4) };
        frameBar.Controls.Add(new Label { Text = "프레임:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        frameBar.Controls.Add(numFrame);
        frameBar.Controls.Add(new Label { Text = "  SPR 선택 시 미리보기", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        right.Controls.Add(frameBar, 0, 0);
        right.Controls.Add(preview, 0, 1);
        right.Controls.Add(lblPreviewInfo, 0, 2);
        var entryButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(4) };
        entryButtons.Controls.Add(btnExtract);
        entryButtons.Controls.Add(btnExport);
        right.Controls.Add(entryButtons, 0, 3);
        entrySplit.Panel2.Controls.Add(right);
        tabEntries.Controls.Add(entrySplit);

        var tabPacks = new TabPage("IDX/PAK 묶음");
        tabPacks.Controls.Add(gridPacks);

        var tabFiles = new TabPage("클라 실제 파일");
        var filePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        filePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        filePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        filePanel.Controls.Add(gridFiles, 0, 0);
        var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(4) };
        var btnHash = new Button { Text = "선택 파일 SHA-256", AutoSize = true };
        var btnExportFiles = new Button { Text = "실제 파일목록 TSV 저장", AutoSize = true };
        fileButtons.Controls.Add(btnHash);
        fileButtons.Controls.Add(btnExportFiles);
        filePanel.Controls.Add(fileButtons, 0, 1);
        tabFiles.Controls.Add(filePanel);

        tabs.TabPages.Add(tabEntries);
        tabs.TabPages.Add(tabPacks);
        tabs.TabPages.Add(tabFiles);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(status);
        statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        statusStrip.Items.Add(progress);

        Controls.Add(tabs);
        Controls.Add(top);
        Controls.Add(statusStrip);

        gridPacks.DataSource = packRows;
        gridFiles.DataSource = fileRows;

        btnBrowse.Click += (_, _) => ChooseFolder();
        btnScan.Click += async (_, _) => await ScanAsync();
        btnSearch.Click += async (_, _) => await ApplyFilterAsync(false);
        txtSearch.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ApplyFilterAsync(false); } };
        cboGroup.SelectedIndexChanged += async (_, _) => { if (rootPath != null) await ApplyFilterAsync(false); };
        btnKnight61.Click += async (_, _) =>
        {
            cboGroup.SelectedItem = "Sprite";
            txtSearch.Text = "61-";
            await ApplyFilterAsync(true);
        };
        btnAnalyzeSpr.Click += async (_, _) => await AnalyzeCurrentSprAsync();
        btnExtract.Click += async (_, _) => await ExtractSelectedAsync();
        btnExport.Click += (_, _) => ExportEntries();
        btnHash.Click += async (_, _) => await HashSelectedFilesAsync();
        btnExportFiles.Click += (_, _) => ExportFiles();
        gridEntries.SelectionChanged += async (_, _) => await PreviewSelectedAsync();
        numFrame.ValueChanged += async (_, _) => await PreviewSelectedAsync();

        FormClosed += (_, _) =>
        {
            foreach (var s in scanners.Values) s.Dispose();
            preview.Image?.Dispose();
        };

        ConfigureColumnsAfterBinding();
    }

    private static DataGridView NewGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToOrderColumns = true,
        AutoGenerateColumns = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
    };

    private void ConfigureColumnsAfterBinding()
    {
        gridEntries.DataBindingComplete += (_, _) =>
        {
            HideColumn(gridEntries, nameof(EntryRow.InternalKey));
            FillColumn(gridEntries, nameof(EntryRow.FileName));
        };
        gridPacks.DataBindingComplete += (_, _) => FillColumn(gridPacks, nameof(PackRow.IdxFile));
        gridFiles.DataBindingComplete += (_, _) => FillColumn(gridFiles, nameof(FileRow.RelativePath));
    }

    private static void HideColumn(DataGridView grid, string name)
    {
        if (grid.Columns[name] != null) grid.Columns[name]!.Visible = false;
    }

    private static void FillColumn(DataGridView grid, string name)
    {
        if (grid.Columns[name] != null)
        {
            grid.Columns[name]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[name]!.MinimumWidth = 220;
        }
    }

    private void ChooseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "현재 사용하는 리니지 클라이언트 폴더를 선택하세요.",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        rootPath = dlg.SelectedPath;
        txtRoot.Text = rootPath;
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            MessageBox.Show(this, "먼저 클라이언트 폴더를 선택하세요.", "확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetBusy(true, "클라이언트 전체 검사 중...");
        try
        {
            foreach (var s in scanners.Values) s.Dispose();
            scanners.Clear();
            allEntries.Clear();
            packRows.Clear();
            fileRows.Clear();
            preview.Image?.Dispose();
            preview.Image = null;

            var result = await Task.Run(() => ScanClient(rootPath!));

            foreach (var p in result.Packs) packRows.Add(p);
            allEntries.AddRange(result.Entries);
            foreach (var f in result.Files) fileRows.Add(f);
            foreach (var kv in result.Scanners) scanners[kv.Key] = kv.Value;

            await ApplyFilterAsync(false);
            status.Text = $"완료: IDX/PAK {packRows.Count:N0}개, 내부 항목 {allEntries.Count:N0}개, 실제 파일 {fileRows.Count:N0}개";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "검사 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            status.Text = "검사 실패";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private ScanResult ScanClient(string root)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var result = new ScanResult();
        var idxFiles = Directory.EnumerateFiles(root, "*.idx", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int done = 0;
        foreach (var idx in idxFiles)
        {
            string pak = Path.ChangeExtension(idx, ".pak");
            string relIdx = Path.GetRelativePath(root, idx);
            string group = DetectGroup(Path.GetFileNameWithoutExtension(idx));

            if (!File.Exists(pak))
            {
                result.Packs.Add(new PackRow
                {
                    Group = group, IdxFile = relIdx, PakFile = "(없음)",
                    Format = "PAIR 없음", EntryCount = 0, PakBytes = 0, Encrypted = false, Status = "PAK 없음"
                });
                continue;
            }

            try
            {
                var scanner = new AnyPakScanner(idx);
                result.Scanners[idx] = scanner;
                long pakLen = new FileInfo(pak).Length;

                result.Packs.Add(new PackRow
                {
                    Group = group,
                    IdxFile = relIdx,
                    PakFile = Path.GetRelativePath(root, pak),
                    Format = scanner.Format,
                    EntryCount = scanner.Entries.Count,
                    PakBytes = pakLen,
                    Encrypted = scanner.DesEncrypted,
                    Status = "정상"
                });

                int actualSpritePak = ParseSpritePakIndex(Path.GetFileNameWithoutExtension(idx));
                foreach (var e in scanner.Entries)
                {
                    int expected = e.FileName.EndsWith(".spr", StringComparison.OrdinalIgnoreCase)
                        ? SpritePak.ExpectedPakIndex(e.FileName) : -1;

                    result.Entries.Add(new EntryRow
                    {
                        Group = group,
                        IdxFile = relIdx,
                        PakFile = Path.GetRelativePath(root, pak),
                        Format = scanner.Format + (scanner.DesEncrypted ? "+DES" : ""),
                        FileName = e.FileName,
                        Offset = e.Offset,
                        FileSize = e.FileSize,
                        CompressedSize = e.CompressedSize,
                        Flags = e.Flags,
                        ActualSpritePak = actualSpritePak >= 0 ? actualSpritePak.ToString("00") : "",
                        ExpectedSpritePak = expected >= 0 ? expected.ToString("00") : "",
                        Distribution = (actualSpritePak >= 0 && expected >= 0) ? (actualSpritePak == expected ? "OK" : "확인필요") : "",
                        InternalKey = idx + "\n" + e.FileName
                    });
                }
            }
            catch (Exception ex)
            {
                result.Packs.Add(new PackRow
                {
                    Group = group,
                    IdxFile = relIdx,
                    PakFile = Path.GetRelativePath(root, pak),
                    Format = "미지원/오류",
                    EntryCount = 0,
                    PakBytes = new FileInfo(pak).Length,
                    Encrypted = false,
                    Status = ex.Message
                });
            }

            done++;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fi = new FileInfo(path);
            result.Files.Add(new FileRow
            {
                RelativePath = Path.GetRelativePath(root, path),
                Extension = fi.Extension,
                SizeBytes = fi.Length,
                Modified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Sha256 = ""
            });
        }

        return result;
    }

    private async Task ApplyFilterAsync(bool autoAnalyzeSpr)
    {
        string group = cboGroup.SelectedItem?.ToString() ?? "전체";
        string q = txtSearch.Text.Trim();

        currentEntries = allEntries
            .Where(x => group == "전체" || x.Group.Equals(group, StringComparison.OrdinalIgnoreCase))
            .Where(x => q.Length == 0 || x.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || x.IdxFile.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();

        gridEntries.DataSource = null;
        gridEntries.DataSource = new BindingList<EntryRow>(currentEntries);
        lblSummary.Text = $"표시 {currentEntries.Count:N0} / 전체 {allEntries.Count:N0}";

        if (autoAnalyzeSpr && currentEntries.Count > 0)
            await AnalyzeCurrentSprAsync();
    }

    private async Task AnalyzeCurrentSprAsync()
    {
        var targets = currentEntries.Where(x => x.FileName.EndsWith(".spr", StringComparison.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0)
        {
            status.Text = "현재 검색 결과에 SPR이 없습니다.";
            return;
        }

        if (targets.Count > 5000)
        {
            var dr = MessageBox.Show(this, $"SPR {targets.Count:N0}개를 분석합니다. 계속할까요?", "SPR 분석",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;
        }

        SetBusy(true, $"SPR {targets.Count:N0}개 분석 중...");
        try
        {
            int done = 0;
            foreach (var row in targets)
            {
                await Task.Run(() => AnalyzeSprRow(row));
                done++;
                if (done % 10 == 0 || done == targets.Count)
                {
                    progress.Value = Math.Min(100, (int)(done * 100L / targets.Count));
                    status.Text = $"SPR 분석 {done:N0}/{targets.Count:N0}";
                    Application.DoEvents();
                }
            }
            gridEntries.Refresh();
            status.Text = $"SPR 분석 완료: {targets.Count:N0}개";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void AnalyzeSprRow(EntryRow row)
    {
        string idxFull = Path.Combine(rootPath!, row.IdxFile);
        if (!scanners.TryGetValue(idxFull, out var scanner)) return;
        var rec = scanner.Entries.FirstOrDefault(e => e.FileName.Equals(row.FileName, StringComparison.OrdinalIgnoreCase));
        if (rec == null) return;

        try
        {
            byte[] extracted = scanner.Extract(rec);
            bool zlib = SpriteCodec.IsZlib(extracted);
            byte[] spr = SpriteCodec.DecodeIfNeeded(extracted);
            var info = SprInfo.Analyze(spr);
            row.SprFrames = info.FrameCount;
            row.SprPalette = info.IsPalette ? info.PaletteSize.ToString() : "RGB555";
            row.SprFrameType = info.FrameType;
            row.Zlib = zlib ? "ZLIB" : "";
            row.Analysis = info.FrameCount > 0 ? "SPR 정상" : "SPR 확인필요";
        }
        catch (Exception ex)
        {
            row.Analysis = "오류: " + ex.Message;
        }
    }

    private async Task PreviewSelectedAsync()
    {
        if (gridEntries.SelectedRows.Count != 1) return;
        if (gridEntries.SelectedRows[0].DataBoundItem is not EntryRow row) return;
        if (!row.FileName.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
        {
            preview.Image?.Dispose();
            preview.Image = null;
            lblPreviewInfo.Text = row.FileName;
            return;
        }

        try
        {
            string idxFull = Path.Combine(rootPath!, row.IdxFile);
            if (!scanners.TryGetValue(idxFull, out var scanner)) return;
            var rec = scanner.Entries.FirstOrDefault(e => e.FileName.Equals(row.FileName, StringComparison.OrdinalIgnoreCase));
            if (rec == null) return;

            byte[] extracted = await Task.Run(() => scanner.Extract(rec));
            bool zlib = SpriteCodec.IsZlib(extracted);
            byte[] spr = SpriteCodec.DecodeIfNeeded(extracted);
            var info = SprInfo.Analyze(spr);
            row.SprFrames = info.FrameCount;
            row.SprPalette = info.IsPalette ? info.PaletteSize.ToString() : "RGB555";
            row.SprFrameType = info.FrameType;
            row.Zlib = zlib ? "ZLIB" : "";
            row.Analysis = info.FrameCount > 0 ? "SPR 정상" : "SPR 확인필요";

            int max = Math.Max(0, info.FrameCount - 1);
            numFrame.Maximum = max;
            if (numFrame.Value > max) numFrame.Value = max;
            int frameIndex = (int)numFrame.Value;

            var bmp = await Task.Run(() => SprDecoder.DecodeFrame(spr, frameIndex));
            var old = preview.Image;
            preview.Image = bmp;
            old?.Dispose();

            lblPreviewInfo.Text =
                $"{row.FileName}\n{row.IdxFile} → {row.PakFile}\n" +
                $"Offset {row.Offset:N0} / Size {row.FileSize:N0} / Frame {info.FrameCount} / " +
                $"{(info.IsPalette ? "Palette " + info.PaletteSize : "RGB555")} / Type {info.FrameType} / {(zlib ? "ZLIB" : "RAW")}";
            gridEntries.Refresh();
        }
        catch (Exception ex)
        {
            lblPreviewInfo.Text = "미리보기 실패: " + ex.Message;
        }
    }

    private async Task ExtractSelectedAsync()
    {
        var rows = gridEntries.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as EntryRow)
            .Where(r => r != null)
            .Cast<EntryRow>()
            .Distinct()
            .ToList();

        if (rows.Count == 0)
        {
            MessageBox.Show(this, "추출할 항목을 선택하세요.");
            return;
        }

        using var dlg = new FolderBrowserDialog { Description = "원본 추출 폴더 선택", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string outDir = Path.Combine(dlg.SelectedPath, "ClientInspector_Extract");
        Directory.CreateDirectory(outDir);

        SetBusy(true, $"선택 {rows.Count:N0}개 추출 중...");
        try
        {
            int done = 0;
            foreach (var row in rows)
            {
                string idxFull = Path.Combine(rootPath!, row.IdxFile);
                if (!scanners.TryGetValue(idxFull, out var scanner)) continue;
                var rec = scanner.Entries.FirstOrDefault(e => e.FileName.Equals(row.FileName, StringComparison.OrdinalIgnoreCase));
                if (rec == null) continue;

                byte[] data = await Task.Run(() => scanner.Extract(rec));
                if (row.FileName.EndsWith(".spr", StringComparison.OrdinalIgnoreCase))
                    data = SpriteCodec.DecodeIfNeeded(data);

                string safe = row.FileName.Replace('\\', '_').Replace('/', '_');
                string dest = Path.Combine(outDir, safe);
                if (File.Exists(dest))
                {
                    string stem = Path.GetFileNameWithoutExtension(safe);
                    string ext = Path.GetExtension(safe);
                    dest = Path.Combine(outDir, $"{stem}_{Path.GetFileNameWithoutExtension(row.IdxFile)}{ext}");
                }
                await File.WriteAllBytesAsync(dest, data);

                done++;
                progress.Value = Math.Min(100, (int)(done * 100L / rows.Count));
            }

            status.Text = $"추출 완료: {outDir}";
            MessageBox.Show(this, $"추출 완료\n{outDir}", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task HashSelectedFilesAsync()
    {
        var rows = gridFiles.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as FileRow)
            .Where(r => r != null)
            .Cast<FileRow>()
            .Distinct()
            .ToList();

        if (rows.Count == 0) return;
        SetBusy(true, $"SHA-256 계산 중: {rows.Count:N0}개");
        try
        {
            int done = 0;
            foreach (var row in rows)
            {
                string full = Path.Combine(rootPath!, row.RelativePath);
                row.Sha256 = await Task.Run(() => Sha256File(full));
                done++;
                progress.Value = Math.Min(100, (int)(done * 100L / rows.Count));
            }
            gridFiles.Refresh();
            status.Text = "SHA-256 계산 완료";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ExportEntries()
    {
        if (currentEntries.Count == 0) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "TSV 파일 (*.tsv)|*.tsv|텍스트 파일 (*.txt)|*.txt",
            FileName = "ClientInspector_IDX_PAK_List.tsv"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        using var sw = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true));
        sw.WriteLine("Group\tIDX\tPAK\tFormat\tFileName\tOffset\tFileSize\tCompressedSize\tFlags\tActualSpritePak\tExpectedSpritePak\tDistribution\tSprFrames\tSprPalette\tSprFrameType\tZlib\tAnalysis");
        foreach (var x in currentEntries)
            sw.WriteLine($"{T(x.Group)}\t{T(x.IdxFile)}\t{T(x.PakFile)}\t{T(x.Format)}\t{T(x.FileName)}\t{x.Offset}\t{x.FileSize}\t{x.CompressedSize}\t{x.Flags}\t{T(x.ActualSpritePak)}\t{T(x.ExpectedSpritePak)}\t{T(x.Distribution)}\t{x.SprFrames}\t{T(x.SprPalette)}\t{x.SprFrameType}\t{T(x.Zlib)}\t{T(x.Analysis)}");
        status.Text = "TSV 저장 완료";
    }

    private void ExportFiles()
    {
        if (fileRows.Count == 0) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "TSV 파일 (*.tsv)|*.tsv|텍스트 파일 (*.txt)|*.txt",
            FileName = "ClientInspector_ClientFiles.tsv"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        using var sw = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true));
        sw.WriteLine("RelativePath\tExtension\tSizeBytes\tModified\tSHA256");
        foreach (var x in fileRows)
            sw.WriteLine($"{T(x.RelativePath)}\t{T(x.Extension)}\t{x.SizeBytes}\t{T(x.Modified)}\t{T(x.Sha256)}");
        status.Text = "실제 파일목록 TSV 저장 완료";
    }

    private static string T(string? s) => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    private static string Sha256File(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    private void SetBusy(bool busy, string? text = null)
    {
        UseWaitCursor = busy;
        btnBrowse.Enabled = !busy;
        btnScan.Enabled = !busy;
        btnSearch.Enabled = !busy;
        btnKnight61.Enabled = !busy;
        btnAnalyzeSpr.Enabled = !busy;
        btnExtract.Enabled = !busy;
        progress.Value = busy ? Math.Max(0, progress.Value) : 0;
        if (!string.IsNullOrWhiteSpace(text)) status.Text = text;
    }

    private static string DetectGroup(string name)
    {
        if (name.StartsWith("Sprite", StringComparison.OrdinalIgnoreCase)) return "Sprite";
        if (name.StartsWith("Image", StringComparison.OrdinalIgnoreCase)) return "Image";
        if (name.StartsWith("Data", StringComparison.OrdinalIgnoreCase)) return "Data";
        if (name.StartsWith("Tile", StringComparison.OrdinalIgnoreCase)) return "Tile";
        if (name.StartsWith("Text", StringComparison.OrdinalIgnoreCase)) return "Text";
        if (name.StartsWith("Sound", StringComparison.OrdinalIgnoreCase)) return "Sound";
        return "기타";
    }

    private static int ParseSpritePakIndex(string name)
    {
        if (!name.StartsWith("Sprite", StringComparison.OrdinalIgnoreCase)) return -1;
        string s = name["Sprite".Length..];
        return int.TryParse(s, out int n) && n >= 0 && n <= 15 ? n : -1;
    }

    private sealed class ScanResult
    {
        public List<PackRow> Packs { get; } = new();
        public List<EntryRow> Entries { get; } = new();
        public List<FileRow> Files { get; } = new();
        public Dictionary<string, AnyPakScanner> Scanners { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class PackRow
    {
        [DisplayName("구분")] public string Group { get; set; } = "";
        [DisplayName("IDX")] public string IdxFile { get; set; } = "";
        [DisplayName("PAK")] public string PakFile { get; set; } = "";
        [DisplayName("형식")] public string Format { get; set; } = "";
        [DisplayName("항목수")] public int EntryCount { get; set; }
        [DisplayName("PAK크기")] public long PakBytes { get; set; }
        [DisplayName("DES")] public bool Encrypted { get; set; }
        [DisplayName("상태")] public string Status { get; set; } = "";
    }

    internal sealed class EntryRow
    {
        [DisplayName("구분")] public string Group { get; set; } = "";
        [DisplayName("IDX")] public string IdxFile { get; set; } = "";
        [DisplayName("PAK")] public string PakFile { get; set; } = "";
        [DisplayName("형식")] public string Format { get; set; } = "";
        [DisplayName("내부파일")] public string FileName { get; set; } = "";
        [DisplayName("Offset")] public long Offset { get; set; }
        [DisplayName("원본크기")] public int FileSize { get; set; }
        [DisplayName("압축크기")] public int CompressedSize { get; set; }
        [DisplayName("Flags")] public int Flags { get; set; }
        [DisplayName("실제Sprite")] public string ActualSpritePak { get; set; } = "";
        [DisplayName("예상Sprite")] public string ExpectedSpritePak { get; set; } = "";
        [DisplayName("분배")] public string Distribution { get; set; } = "";
        [DisplayName("SPR프레임")] public int SprFrames { get; set; }
        [DisplayName("색상")] public string SprPalette { get; set; } = "";
        [DisplayName("FrameType")] public int SprFrameType { get; set; }
        [DisplayName("ZLIB")] public string Zlib { get; set; } = "";
        [DisplayName("분석")] public string Analysis { get; set; } = "";
        [Browsable(false)] public string InternalKey { get; set; } = "";
    }

    internal sealed class FileRow
    {
        [DisplayName("상대경로")] public string RelativePath { get; set; } = "";
        [DisplayName("확장자")] public string Extension { get; set; } = "";
        [DisplayName("크기")] public long SizeBytes { get; set; }
        [DisplayName("수정시각")] public string Modified { get; set; } = "";
        [DisplayName("SHA-256")] public string Sha256 { get; set; } = "";
    }
}
