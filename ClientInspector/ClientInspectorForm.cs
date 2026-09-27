using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;

namespace LineageSpriteStudio;

internal sealed class ClientInspectorForm : Form
{
    private readonly TextBox txtRoot = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Button btnBrowse = new() { Text = "클라 폴더 선택", AutoSize = true };
    private readonly Button btnScan = new() { Text = "전체 검사", AutoSize = true };
    private readonly Button btnDbBackup = new() { Text = "나비캣 PSC 선택", AutoSize = true };
    private readonly Button btnServerPack = new() { Text = "서버팩 ZIP 선택", AutoSize = true };
    private readonly TextBox txtSearch = new() { Width = 260, PlaceholderText = "통합 검색 (예: 3000209 / autohunt / 61-)" };
    private readonly Button btnGlobalSearch = new() { Text = "통합 검색", AutoSize = true };
    private readonly ComboBox cboType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly Button btnSave = new() { Text = "선택 원본 저장", AutoSize = true };
    private readonly Button btnHash = new() { Text = "SHA-256", AutoSize = true };
    private readonly Label lblCount = new() { AutoSize = true, Padding = new Padding(8, 7, 0, 0) };

    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToOrderColumns = true,
        AutoGenerateColumns = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
    };

    private readonly Label lblInfo = new()
    {
        Dock = DockStyle.Top,
        Height = 52,
        Padding = new Padding(8),
        AutoEllipsis = true
    };

    private readonly Panel previewHost = new() { Dock = DockStyle.Fill };

    private readonly RichTextBox textPreview = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        DetectUrls = false,
        WordWrap = false,
        Font = new Font("Consolas", 9F),
        BackColor = Color.White,
        Visible = false
    };

    private readonly PictureBox imagePreview = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(28, 31, 38),
        Visible = false
    };

    private readonly PictureBox sprPreview = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(28, 31, 38),
        Visible = false
    };

    private readonly RichTextBox hexPreview = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        DetectUrls = false,
        WordWrap = false,
        Font = new Font("Consolas", 9F),
        BackColor = Color.White,
        Visible = false
    };

    private readonly Panel sprBar = new() { Dock = DockStyle.Bottom, Height = 42, Visible = false };
    private readonly NumericUpDown numSprFrame = new() { Minimum = 0, Maximum = 0, Width = 100, Left = 70, Top = 8 };
    private readonly Label lblSpr = new() { AutoSize = true, Left = 182, Top = 11 };

    private readonly ToolStripStatusLabel status = new() { Text = "준비" };
    private readonly ToolStripProgressBar progress = new() { Minimum = 0, Maximum = 100, Width = 180 };

    private readonly List<ViewRow> allRows = new();
    private List<ViewRow> currentRows = new();
    private List<ViewRow>? globalSearchResults;
    private readonly Dictionary<string, AnyPakScanner> scanners = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ViewRow> externalBackupRows = new();
    private readonly List<ViewRow> externalServerRows = new();

    private string? rootPath;
    private byte[]? currentRaw;
    private string currentName = "selected.bin";
    private ViewRow? currentRow;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xml", ".txt", ".json", ".ini", ".cfg", ".conf",
        ".properties", ".js", ".css", ".lua", ".csv", ".log", ".md", ".yml", ".yaml", ".sql",
        ".java", ".bat", ".cmd", ".ps1", ".mf", ".prefs"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".bmp", ".jpg", ".jpeg", ".gif", ".ico", ".tif", ".tiff"
    };

    public ClientInspectorForm()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Text = "Lineage Client Inspector V2.0 - 클라 + DB + 서버팩 통합 검색";
        Width = 1560;
        Height = 920;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        Font = new Font("Malgun Gothic", 9F);

        cboType.Items.AddRange(new object[] { "전체", "서버팩", "이미지", "HTML/텍스트", "SPR", "DB백업", "기타" });
        cboType.SelectedIndex = 0;

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            ColumnCount = 11,
            Padding = new Padding(6)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 1; i < 11; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(txtRoot, 0, 0);
        top.Controls.Add(btnBrowse, 1, 0);
        top.Controls.Add(btnScan, 2, 0);
        top.Controls.Add(btnDbBackup, 3, 0);
        top.Controls.Add(btnServerPack, 4, 0);
        top.Controls.Add(cboType, 5, 0);
        top.Controls.Add(txtSearch, 6, 0);
        top.Controls.Add(btnGlobalSearch, 7, 0);
        top.Controls.Add(btnHash, 8, 0);
        top.Controls.Add(btnSave, 9, 0);
        top.Controls.Add(lblCount, 10, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 780
        };

        split.Panel1.Controls.Add(grid);

        previewHost.Controls.Add(textPreview);
        previewHost.Controls.Add(imagePreview);
        previewHost.Controls.Add(sprPreview);
        previewHost.Controls.Add(hexPreview);

        sprBar.Controls.Add(new Label { Text = "프레임", AutoSize = true, Left = 12, Top = 11 });
        sprBar.Controls.Add(numSprFrame);
        sprBar.Controls.Add(lblSpr);

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(previewHost);
        right.Controls.Add(sprBar);
        right.Controls.Add(lblInfo);
        split.Panel2.Controls.Add(right);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(status);
        statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        statusStrip.Items.Add(progress);

        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(statusStrip);

        btnBrowse.Click += (_, _) => ChooseFolder();
        btnScan.Click += async (_, _) => await ScanAsync();
        btnDbBackup.Click += async (_, _) => await ChooseDbBackupsAsync();
        btnServerPack.Click += async (_, _) => await ChooseServerPacksAsync();
        btnGlobalSearch.Click += async (_, _) => await GlobalSearchAsync();
        txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await GlobalSearchAsync();
            }
        };
        cboType.SelectedIndexChanged += (_, _) => ApplyFilter();
        grid.SelectionChanged += async (_, _) => await PreviewSelectedAsync();
        numSprFrame.ValueChanged += async (_, _) => await RenderSprFrameAsync();
        btnSave.Click += async (_, _) => await SaveSelectedAsync();
        btnHash.Click += async (_, _) => await HashSelectedAsync();

        grid.DataBindingComplete += (_, _) =>
        {
            if (grid.Columns[nameof(ViewRow.SourceKey)] != null)
                grid.Columns[nameof(ViewRow.SourceKey)]!.Visible = false;

            if (grid.Columns[nameof(ViewRow.Path)] != null)
            {
                grid.Columns[nameof(ViewRow.Path)]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                grid.Columns[nameof(ViewRow.Path)]!.MinimumWidth = 280;
            }
        };

        FormClosed += (_, _) =>
        {
            foreach (var scanner in scanners.Values) scanner.Dispose();
            imagePreview.Image?.Dispose();
            sprPreview.Image?.Dispose();
        };
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
            MessageBox.Show(this, "먼저 클라이언트 폴더를 선택하세요.");
            return;
        }

        SetBusy(true, "전체 검사 중...");

        try
        {
            foreach (var scanner in scanners.Values) scanner.Dispose();
            scanners.Clear();
            allRows.Clear();
            globalSearchResults = null;
            currentRaw = null;
            currentRow = null;
            ClearPreview();

            var result = await Task.Run(() => ScanClient(rootPath!));

            foreach (var kv in result.Scanners)
                scanners[kv.Key] = kv.Value;

            allRows.AddRange(result.Rows);
            allRows.AddRange(externalBackupRows);
            allRows.AddRange(externalServerRows);
            globalSearchResults = null;
            ApplyFilter();

            status.Text = $"완료: 전체 {allRows.Count:N0}개";
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
        var result = new ScanResult();

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fi = new FileInfo(path);
            string rel = Path.GetRelativePath(root, path);

            result.Rows.Add(new ViewRow
            {
                Source = "실제파일",
                Type = DetectType(rel),
                Path = rel,
                Container = "",
                Extension = fi.Extension,
                SizeBytes = fi.Length,
                SourceKey = rel
            });
        }

        foreach (var idx in Directory.EnumerateFiles(root, "*.idx", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string pak = Path.ChangeExtension(idx, ".pak");
            if (!File.Exists(pak)) continue;

            try
            {
                var scanner = new AnyPakScanner(idx);
                result.Scanners[idx] = scanner;

                foreach (var e in scanner.Entries)
                {
                    result.Rows.Add(new ViewRow
                    {
                        Source = "PAK 내부",
                        Type = DetectType(e.FileName),
                        Path = e.FileName,
                        Container = Path.GetRelativePath(root, idx) + " → " + Path.GetRelativePath(root, pak),
                        Extension = Path.GetExtension(e.FileName),
                        SizeBytes = e.FileSize,
                        SourceKey = idx
                    });
                }
            }
            catch
            {
                // 지원하지 않는 IDX는 실제 파일 목록에서 여전히 확인 가능.
            }
        }

        return result;
    }

    private async Task ChooseServerPacksAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "검색할 서버팩 ZIP 선택",
            Filter = "서버팩 ZIP (*.zip)|*.zip|모든 파일 (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        SetBusy(true, "서버팩 ZIP 분석 중...");
        try
        {
            externalServerRows.Clear();

            foreach (string zipPath in dlg.FileNames)
            {
                await Task.Run(() => IndexServerPack(zipPath, externalServerRows));
            }

            foreach (var row in allRows.Where(x => x.Source == "서버팩").ToList())
                allRows.Remove(row);

            allRows.AddRange(externalServerRows);
            globalSearchResults = null;
            ApplyFilter();

            int textCount = externalServerRows.Count(x => !string.IsNullOrEmpty(x.SearchText));
            status.Text = $"서버팩 추가 완료: {dlg.FileNames.Length}개 ZIP | 파일 {externalServerRows.Count:N0}개 | 내용검색 가능 {textCount:N0}개";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "서버팩 분석 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            status.Text = "서버팩 분석 실패";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static void IndexServerPack(string zipPath, List<ViewRow> output)
    {
        using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var za = new ZipArchive(fs, ZipArchiveMode.Read);

        foreach (var entry in za.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;

            string ext = Path.GetExtension(entry.FullName);

            var row = new ViewRow
            {
                Source = "서버팩",
                Type = DetectType(entry.FullName),
                Path = entry.FullName,
                Container = Path.GetFileName(zipPath),
                Extension = ext,
                SizeBytes = entry.Length,
                SourceKey = "SERVERZIP|" + zipPath + "|" + entry.FullName
            };

            // 서버팩의 실제 소스/설정/스크립트만 내용 색인.
            // .class/.jar/.dll/.exe 같은 바이너리는 파일명 검색만 허용한다.
            if (TextExtensions.Contains(ext) && entry.Length <= 4L * 1024 * 1024)
            {
                try
                {
                    using var es = entry.Open();
                    using var ms = new MemoryStream();
                    es.CopyTo(ms);
                    row.SearchText = DecodeText(ms.ToArray()).Text;
                }
                catch { }
            }

            output.Add(row);
        }
    }

    private async Task ChooseDbBackupsAsync()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Navicat/DB 백업 파일 선택",
            Filter = "Navicat PSC (*.psc)|*.psc|Navicat/DB 백업 (*.psc;*.nb3;*.sql;*.db;*.sqlite;*.bak;*.dump)|*.psc;*.nb3;*.sql;*.db;*.sqlite;*.bak;*.dump|모든 파일 (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        SetBusy(true, "DB 백업 분석 중...");
        try
        {
            externalBackupRows.Clear();

            foreach (string path in dlg.FileNames)
            {
                var fi = new FileInfo(path);
                string ext = fi.Extension;

                var parent = new ViewRow
                {
                    Source = "DB백업",
                    Type = ext.Equals(".sql", StringComparison.OrdinalIgnoreCase) ? "HTML/텍스트" : "DB백업",
                    Path = fi.Name,
                    Container = fi.DirectoryName ?? "",
                    Extension = ext,
                    SizeBytes = fi.Length,
                    SourceKey = path
                };

                if (fi.Length <= 10 * 1024 * 1024 && ext.Equals(".sql", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        byte[] bytes = await File.ReadAllBytesAsync(path);
                        parent.SearchText = DecodeText(bytes).Text;
                    }
                    catch { }
                }

                externalBackupRows.Add(parent);

                if (ext.Equals(".psc", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".nb3", StringComparison.OrdinalIgnoreCase))
                {
                    await AddArchiveEntriesAsync(path);
                }
            }

            foreach (var row in allRows.Where(x => x.Source == "DB백업" || x.Source == "PSC 내부").ToList())
                allRows.Remove(row);
            allRows.AddRange(externalBackupRows);

            globalSearchResults = null;
            ApplyFilter();
            status.Text = $"DB 백업 추가 완료: {dlg.FileNames.Length}개, 표시 항목 {externalBackupRows.Count:N0}개";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "DB 백업 분석 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task AddArchiveEntriesAsync(string backupPath)
    {
        try
        {
            using var fs = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var za = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

            foreach (var entry in za.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var row = new ViewRow
                {
                    Source = "PSC 내부",
                    Type = DetectType(entry.FullName),
                    Path = entry.FullName,
                    Container = Path.GetFileName(backupPath),
                    Extension = Path.GetExtension(entry.FullName),
                    SizeBytes = entry.Length,
                    SourceKey = "PSCZIP|" + backupPath + "|" + entry.FullName
                };

                if (entry.Length <= 4 * 1024 * 1024)
                {
                    try
                    {
                        using var es = entry.Open();
                        using var ms = new MemoryStream();
                        await es.CopyToAsync(ms);
                        byte[] bytes = ms.ToArray();

                        if (TextExtensions.Contains(row.Extension))
                        {
                            row.SearchText = DecodeText(bytes).Text;
                        }
                        else if (LooksLikeText(bytes))
                        {
                            row.SearchText = DecodeText(bytes).Text;
                        }
                        else
                        {
                            // 바이너리 항목은 raw byte 검색 대신 실제 ASCII 문자열만 색인한다.
                            row.SearchText = ExtractMeaningfulAsciiStrings(bytes, 250_000);
                        }
                    }
                    catch { }
                }

                externalBackupRows.Add(row);
            }
        }
        catch
        {
            // Navicat 버전/백업 방식에 따라 일반 ZIP으로 직접 열리지 않을 수 있음.
            // 이 경우 상위 PSC 파일 자체는 계속 HEX/문자열로 확인 가능.
        }
    }

    private void ApplyFilter()
    {
        string type = cboType.SelectedItem?.ToString() ?? "전체";
        IEnumerable<ViewRow> source = globalSearchResults ?? allRows;

        currentRows = source
            .Where(r => type == "전체" ||
                        (type == "서버팩" ? r.Source == "서버팩" : r.Type == type))
            .ToList();

        grid.DataSource = null;
        grid.DataSource = new BindingList<ViewRow>(currentRows);

        if (globalSearchResults != null)
            lblCount.Text = $"검색 {currentRows.Count:N0}/{globalSearchResults.Count:N0}";
        else
            lblCount.Text = $"{currentRows.Count:N0}/{allRows.Count:N0}";
    }

    private async Task GlobalSearchAsync()
    {
        string q = txtSearch.Text.Trim();

        foreach (var row in allRows)
            row.Match = "";

        if (q.Length == 0)
        {
            globalSearchResults = null;
            ApplyFilter();
            status.Text = "통합 검색 초기화";
            return;
        }

        if (q.Length == 1 && !char.IsDigit(q[0]))
        {
            MessageBox.Show(this, "한 글자 텍스트 검색은 잡결과가 너무 많아 제외했습니다.\n두 글자 이상 입력하세요.",
                "정확 검색", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        const int MaxResults = 500;
        SetBusy(true, $"정확 통합 검색 중: {q}");

        try
        {
            var results = new List<ViewRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool truncated = false;

            void AddResult(ViewRow row)
            {
                string key = row.Source + "\n" + row.Path + "\n" + row.Container + "\n" + row.Match;
                if (!seen.Add(key)) return;

                if (results.Count < MaxResults)
                    results.Add(row);
                else
                    truncated = true;
            }

            bool shortNumeric = q.All(char.IsDigit) && q.Length <= 3;

            var sqlBackups = shortNumeric
                ? new List<ViewRow>()
                : allRows
                    .Where(r => r.Source == "DB백업" && r.Extension.Equals(".sql", StringComparison.OrdinalIgnoreCase))
                    .ToList();

            foreach (var sql in sqlBackups)
            {
                var sqlMatches = await Task.Run(() => SearchSqlDump(sql.SourceKey, q, MaxResults - results.Count));
                foreach (var hit in sqlMatches)
                    AddResult(hit);

                if (results.Count >= MaxResults)
                {
                    truncated = true;
                    break;
                }

                status.Text = $"DB 정확 검색: {Path.GetFileName(sql.SourceKey)} | 발견 {results.Count:N0}";
                Application.DoEvents();
            }

            if (results.Count < MaxResults)
            {
                var searchableRows = allRows
                    .Where(r => !(r.Source == "DB백업" && r.Extension.Equals(".sql", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                int total = searchableRows.Count;
                int done = 0;

                foreach (var row in searchableRows)
                {
                    string? match = null;

                    // 경로 전체/확장자/컨테이너는 검색하지 않는다.
                    // 사용자가 실제로 보는 파일명 또는 내부 파일명에만 적용한다.
                    if (StrictContains(row.Path, q))
                    {
                        match = "파일명";
                    }
                    else if (!shortNumeric && !string.IsNullOrEmpty(row.SearchText) && StrictContains(row.SearchText, q))
                    {
                        match = "의미있는 내용";
                    }
                    else if (!shortNumeric && ShouldContentSearch(row) && await TextContentContainsAsync(row, q))
                    {
                        match = "텍스트 내용";
                    }

                    if (match != null)
                    {
                        row.Match = match;
                        AddResult(row);

                        if (results.Count >= MaxResults)
                        {
                            truncated = true;
                            break;
                        }
                    }

                    done++;
                    if (done % 100 == 0 || done == total)
                    {
                        progress.Value = total == 0 ? 0 : Math.Min(100, (int)(done * 100L / total));
                        status.Text = $"정확 통합 검색 {done:N0}/{total:N0} | 발견 {results.Count:N0}";
                        Application.DoEvents();
                    }
                }
            }

            globalSearchResults = results;
            ApplyFilter();

            if (shortNumeric)
            {
                status.Text = $"정확 검색 완료: '{q}' → {results.Count:N0}건 | 1~3자리 숫자는 DB/본문 검색 제외";
            }
            else
            {
                status.Text = truncated
                    ? $"정확 검색: '{q}' → {results.Count:N0}건 표시 (500건 초과, 검색어를 더 구체화하세요)"
                    : $"정확 검색 완료: '{q}' → {results.Count:N0}건";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "통합 검색 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            status.Text = "통합 검색 실패";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static List<ViewRow> SearchSqlDump(string path, string query, int limit)
    {
        var results = new List<ViewRow>();
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(query) || limit <= 0)
            return results;

        int lineNo = 0;

        using var sr = new StreamReader(path, new UTF8Encoding(false, false), detectEncodingFromByteOrderMarks: true);

        while (sr.ReadLine() is string line)
        {
            lineNo++;
            string trimmed = line.TrimStart();

            bool isInsert = trimmed.StartsWith("INSERT INTO ", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("REPLACE INTO ", StringComparison.OrdinalIgnoreCase);

            if (isInsert)
            {
                if (!StrictContains(line, query))
                    continue;

                string table = ExtractSqlTableName(trimmed);

                results.Add(new ViewRow
                {
                    Source = "DB SQL",
                    Type = "DB백업",
                    Path = string.IsNullOrWhiteSpace(table) ? $"SQL 행 {lineNo:N0}" : table,
                    Container = $"{Path.GetFileName(path)} | line {lineNo:N0}",
                    Extension = ".sql",
                    SizeBytes = Encoding.UTF8.GetByteCount(line),
                    SourceKey = path,
                    SearchText = line,
                    Match = $"DB 실제값 | {table} | line {lineNo:N0}"
                });
            }
            else if (trimmed.StartsWith("CREATE TABLE ", StringComparison.OrdinalIgnoreCase))
            {
                string table = ExtractSqlTableName(trimmed);
                if (string.IsNullOrWhiteSpace(table) ||
                    !table.Equals(query, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new ViewRow
                {
                    Source = "DB SQL",
                    Type = "DB백업",
                    Path = table,
                    Container = $"{Path.GetFileName(path)} | line {lineNo:N0}",
                    Extension = ".sql",
                    SizeBytes = Encoding.UTF8.GetByteCount(line),
                    SourceKey = path,
                    SearchText = line,
                    Match = $"DB 테이블명 정확일치 | line {lineNo:N0}"
                });
            }
            else
            {
                continue;
            }

            if (results.Count >= limit)
                break;
        }

        return results;
    }

    private static bool StrictContains(string text, string query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
            return false;

        bool numeric = query.All(char.IsDigit);
        int start = 0;

        while (start <= text.Length - query.Length)
        {
            int idx = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            if (!numeric)
                return true;

            bool leftOk = idx == 0 || !char.IsDigit(text[idx - 1]);
            int after = idx + query.Length;
            bool rightOk = after >= text.Length || !char.IsDigit(text[after]);

            if (leftOk && rightOk)
                return true;

            start = idx + 1;
        }

        return false;
    }

    private async Task<bool> TextContentContainsAsync(ViewRow row, string query)
    {
        try
        {
            byte[] data = await ReadBytesAsync(row);

            // 텍스트 파일만 이 경로로 들어온다. 바이너리 HEX는 검색하지 않는다.
            var decoded = DecodeText(data);
            return StrictContains(decoded.Text, query);
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractSqlTableName(string line)
    {
        char tick = (char)96;
        int first = line.IndexOf(tick);
        if (first < 0) return "";
        int second = line.IndexOf(tick, first + 1);
        if (second <= first) return "";
        return line[(first + 1)..second];
    }

    private static bool ShouldContentSearch(ViewRow row)
    {
        // 원시 바이너리 바이트는 검색하지 않는다.
        // HTML/SQL/TXT/XML/JSON 등 명확한 텍스트 형식만 실제 파일 내용을 검색한다.
        return TextExtensions.Contains(row.Extension);
    }

    private async Task<long> FindContentOffsetAsync(ViewRow row, string query)
    {
        var patterns = BuildSearchPatterns(query);
        if (patterns.Count == 0) return -1;

        if (row.Source == "실제파일" || row.Source == "DB백업")
        {
            string path = row.Source == "실제파일"
                ? Path.Combine(rootPath!, row.SourceKey)
                : row.SourceKey;

            return await Task.Run(() => FindInFile(path, patterns));
        }

        byte[] data;
        try
        {
            data = await ReadBytesAsync(row);
        }
        catch
        {
            return -1;
        }

        return FindInBytes(data, patterns);
    }

    private static List<byte[]> BuildSearchPatterns(string query)
    {
        var list = new List<byte[]>();

        void Add(byte[] bytes)
        {
            if (bytes.Length == 0) return;
            if (!list.Any(x => x.AsSpan().SequenceEqual(bytes)))
                list.Add(bytes);
        }

        Add(Encoding.UTF8.GetBytes(query));
        Add(Encoding.Unicode.GetBytes(query));

        try { Add(Encoding.GetEncoding(949).GetBytes(query)); } catch { }

        if (query.All(c => c <= 0x7F))
            Add(Encoding.ASCII.GetBytes(query));

        return list;
    }

    private static long FindInFile(string path, List<byte[]> patterns)
    {
        if (!File.Exists(path)) return -1;

        int maxPattern = patterns.Max(p => p.Length);
        int overlap = Math.Max(0, maxPattern - 1);
        const int ChunkSize = 1024 * 1024;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            ChunkSize, FileOptions.SequentialScan);

        byte[] buffer = new byte[ChunkSize + overlap];
        int carry = 0;
        long absolute = 0;

        while (true)
        {
            int read = fs.Read(buffer, carry, ChunkSize);
            if (read <= 0) break;

            int length = carry + read;
            var span = buffer.AsSpan(0, length);

            foreach (var pattern in patterns)
            {
                int idx = span.IndexOf(pattern);
                if (idx >= 0)
                    return absolute - carry + idx;
            }

            if (overlap > 0)
            {
                carry = Math.Min(overlap, length);
                Buffer.BlockCopy(buffer, length - carry, buffer, 0, carry);
            }
            else
            {
                carry = 0;
            }

            absolute += read;
        }

        return -1;
    }

    private static long FindInBytes(byte[] data, List<byte[]> patterns)
    {
        ReadOnlySpan<byte> span = data;

        foreach (var pattern in patterns)
        {
            int idx = span.IndexOf(pattern);
            if (idx >= 0) return idx;
        }

        return -1;
    }

    private async Task PreviewSelectedAsync()
    {
        if (grid.SelectedRows.Count != 1) return;
        if (grid.SelectedRows[0].DataBoundItem is not ViewRow row) return;

        try
        {
            currentRow = row;
            currentRaw = await ReadBytesAsync(row);
            currentName = Path.GetFileName(row.Path);

            string type = row.Source == "DB SQL"
                ? "HTML/텍스트"
                : DetectTypeFromData(row.Path, currentRaw);
            row.Type = row.Source == "DB SQL" ? "DB백업" : type;

            ClearPreview();

            lblInfo.Text = $"{row.Source} | {type} | {row.Path} | {currentRaw.Length:N0} bytes" +
                           (string.IsNullOrWhiteSpace(row.Container) ? "" : $" | {row.Container}");

            if (type == "HTML/텍스트")
            {
                var d = DecodeText(currentRaw);
                textPreview.Text = d.Text;
                textPreview.Visible = true;
                textPreview.BringToFront();
                lblInfo.Text += $" | {d.EncodingName}";
            }
            else if (type == "이미지")
            {
                ShowImage(currentRaw);
            }
            else if (type == "SPR")
            {
                byte[] spr = SpriteCodec.DecodeIfNeeded(currentRaw);
                var info = SprInfo.Analyze(spr);

                numSprFrame.Maximum = Math.Max(0, info.FrameCount - 1);
                if (numSprFrame.Value > numSprFrame.Maximum)
                    numSprFrame.Value = numSprFrame.Maximum;

                lblSpr.Text = $"총 {info.FrameCount}프레임 | " +
                              $"{(info.IsPalette ? "Palette " + info.PaletteSize : "RGB555")} | " +
                              $"Type {info.FrameType} | {(SpriteCodec.IsZlib(currentRaw) ? "ZLIB" : "RAW")}";

                sprBar.Visible = true;
                sprPreview.Visible = true;
                sprPreview.BringToFront();
                await RenderSprFrameAsync();
            }
            else if (type == "DB백업")
            {
                string strings = ExtractMeaningfulAsciiStrings(currentRaw, 400_000);
                hexPreview.Text =
                    "[PSC/DB 백업 - 읽을 수 있는 문자열]\r\n" +
                    strings +
                    "\r\n\r\n[HEX 앞부분]\r\n" +
                    MakeHexDump(currentRaw, 256 * 1024);
                hexPreview.Visible = true;
                hexPreview.BringToFront();
            }
            else
            {
                hexPreview.Text = MakeHexDump(currentRaw, 1024 * 1024);
                hexPreview.Visible = true;
                hexPreview.BringToFront();
                if (currentRaw.Length > 1024 * 1024)
                    lblInfo.Text += " | HEX 앞 1MB 표시";
            }

            status.Text = $"확인 완료: {row.Path}";
            grid.Refresh();
        }
        catch (Exception ex)
        {
            ClearPreview();
            hexPreview.Text = "미리보기 실패\r\n\r\n" + ex;
            hexPreview.Visible = true;
            hexPreview.BringToFront();
            status.Text = "미리보기 실패";
        }
    }

    private async Task<byte[]> ReadBytesAsync(ViewRow row)
    {
        if (row.Source == "실제파일")
        {
            string full = Path.Combine(rootPath!, row.SourceKey);
            return await File.ReadAllBytesAsync(full);
        }

        if (row.Source == "DB백업")
            return await File.ReadAllBytesAsync(row.SourceKey);

        if (row.Source == "서버팩" && row.SourceKey.StartsWith("SERVERZIP|", StringComparison.Ordinal))
        {
            string payload = row.SourceKey["SERVERZIP|".Length..];
            int split = payload.LastIndexOf('|');
            if (split <= 0) throw new InvalidDataException("서버팩 ZIP 내부 경로가 잘못되었습니다.");

            string zipPath = payload[..split];
            string entryName = payload[(split + 1)..];

            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var za = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = za.GetEntry(entryName) ?? throw new FileNotFoundException("서버팩 ZIP 내부 파일을 찾을 수 없습니다.", entryName);
            using var es = entry.Open();
            using var ms = new MemoryStream();
            await es.CopyToAsync(ms);
            return ms.ToArray();
        }

        if (row.Source == "DB SQL")
            return Encoding.UTF8.GetBytes(row.SearchText ?? "");

        if (row.Source == "PSC 내부" && row.SourceKey.StartsWith("PSCZIP|", StringComparison.Ordinal))
        {
            string payload = row.SourceKey["PSCZIP|".Length..];
            int split = payload.LastIndexOf('|');
            if (split <= 0) throw new InvalidDataException("PSC 내부 경로가 잘못되었습니다.");

            string backupPath = payload[..split];
            string entryName = payload[(split + 1)..];

            using var fs = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var za = new ZipArchive(fs, ZipArchiveMode.Read);
            var entry = za.GetEntry(entryName) ?? throw new FileNotFoundException("PSC 내부 항목을 찾을 수 없습니다.", entryName);
            using var es = entry.Open();
            using var ms = new MemoryStream();
            await es.CopyToAsync(ms);
            return ms.ToArray();
        }

        string idxFull = row.SourceKey;
        if (!scanners.TryGetValue(idxFull, out var scanner))
            throw new InvalidOperationException("해당 IDX를 읽을 수 없습니다.");

        var rec = scanner.Entries.FirstOrDefault(x => x.FileName.Equals(row.Path, StringComparison.OrdinalIgnoreCase));
        if (rec == null)
            throw new FileNotFoundException("PAK 내부 파일을 찾을 수 없습니다.", row.Path);

        return await Task.Run(() => scanner.Extract(rec));
    }

    private void ShowImage(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        using var src = Image.FromStream(ms, useEmbeddedColorManagement: true, validateImageData: true);
        var clone = new Bitmap(src);

        imagePreview.Image?.Dispose();
        imagePreview.Image = clone;
        imagePreview.Visible = true;
        imagePreview.BringToFront();

        lblInfo.Text += $" | {clone.Width}x{clone.Height} | {DetectImageFormat(data)}";
    }

    private async Task RenderSprFrameAsync()
    {
        if (currentRaw == null || currentRow == null) return;
        if (DetectTypeFromData(currentRow.Path, currentRaw) != "SPR") return;

        try
        {
            byte[] spr = SpriteCodec.DecodeIfNeeded(currentRaw);
            int frame = (int)numSprFrame.Value;

            var bmp = await Task.Run(() => SprDecoder.DecodeFrame(spr, frame));

            sprPreview.Image?.Dispose();
            sprPreview.Image = bmp;
        }
        catch (Exception ex)
        {
            lblSpr.Text = "SPR 표시 실패: " + ex.Message;
        }
    }

    private async Task SaveSelectedAsync()
    {
        if (currentRaw == null)
        {
            MessageBox.Show(this, "먼저 파일을 선택하세요.");
            return;
        }

        using var dlg = new SaveFileDialog
        {
            FileName = string.IsNullOrWhiteSpace(currentName) ? "selected.bin" : currentName,
            Filter = "모든 파일 (*.*)|*.*"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        await File.WriteAllBytesAsync(dlg.FileName, currentRaw);
        status.Text = "원본 저장 완료";
    }

    private async Task HashSelectedAsync()
    {
        if (currentRaw == null || currentRow == null)
        {
            MessageBox.Show(this, "먼저 파일을 선택하세요.");
            return;
        }

        string hash = await Task.Run(() => Convert.ToHexString(SHA256.HashData(currentRaw)));
        MessageBox.Show(this, hash, $"SHA-256 - {currentRow.Path}", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ClearPreview()
    {
        textPreview.Visible = false;
        imagePreview.Visible = false;
        sprPreview.Visible = false;
        hexPreview.Visible = false;
        sprBar.Visible = false;

        textPreview.Clear();
        hexPreview.Clear();
        imagePreview.Image?.Dispose();
        imagePreview.Image = null;
        sprPreview.Image?.Dispose();
        sprPreview.Image = null;
        lblSpr.Text = "";
    }

    private static string DetectType(string path)
    {
        string ext = Path.GetExtension(path);

        if (TextExtensions.Contains(ext)) return "HTML/텍스트";
        if (ImageExtensions.Contains(ext)) return "이미지";
        if (ext.Equals(".spr", StringComparison.OrdinalIgnoreCase)) return "SPR";
        if (ext.Equals(".psc", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".nb3", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".dump", StringComparison.OrdinalIgnoreCase))
            return "DB백업";
        return "기타";
    }

    private static string DetectTypeFromData(string path, byte[] data)
    {
        string ext = Path.GetExtension(path);

        if (ext.Equals(".spr", StringComparison.OrdinalIgnoreCase))
            return "SPR";

        if (TextExtensions.Contains(ext))
            return "HTML/텍스트";

        if (ImageExtensions.Contains(ext) || DetectImageFormat(data) != "미확인")
            return "이미지";

        if (ext.Equals(".psc", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".nb3", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".dump", StringComparison.OrdinalIgnoreCase))
            return "DB백업";

        return "기타";
    }

    private static string DetectImageFormat(byte[] data)
    {
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return "PNG";

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "JPEG";

        if (data.Length >= 6 &&
            data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' &&
            data[3] == (byte)'8' && (data[4] == (byte)'7' || data[4] == (byte)'9') && data[5] == (byte)'a')
            return "GIF";

        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M')
            return "BMP";

        if (data.Length >= 4 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0)
            return "ICO";

        return "미확인";
    }

    private static DecodedText DecodeText(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return new DecodedText(new UTF8Encoding(true).GetString(data), "UTF-8 BOM");

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return new DecodedText(Encoding.Unicode.GetString(data), "UTF-16 LE");

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return new DecodedText(Encoding.BigEndianUnicode.GetString(data), "UTF-16 BE");

        try
        {
            return new DecodedText(new UTF8Encoding(false, true).GetString(data), "UTF-8");
        }
        catch
        {
            return new DecodedText(Encoding.GetEncoding(949).GetString(data), "CP949/EUC-KR");
        }
    }

    private static bool LooksLikeText(byte[] data)
    {
        if (data.Length == 0) return false;

        int sample = Math.Min(data.Length, 64 * 1024);
        int printable = 0;
        int zero = 0;

        for (int i = 0; i < sample; i++)
        {
            byte b = data[i];
            if (b == 0) zero++;
            if (b == 9 || b == 10 || b == 13 || (b >= 32 && b <= 126))
                printable++;
        }

        double printableRatio = printable / (double)sample;
        double zeroRatio = zero / (double)sample;

        return printableRatio >= 0.82 && zeroRatio < 0.05;
    }

    private static string ExtractMeaningfulAsciiStrings(byte[] data, int maxChars)
    {
        var sb = new StringBuilder(Math.Min(maxChars, 100_000));
        var current = new StringBuilder();

        static bool IsUseful(string value)
        {
            if (value.Length < 6) return false;

            int meaningful = 0;
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or '\\' or ':' or '@')
                    meaningful++;
            }

            return meaningful >= 4 && meaningful >= value.Length / 3;
        }

        void Flush()
        {
            if (current.Length > 0)
            {
                string value = current.ToString().Trim();
                if (IsUseful(value) && sb.Length + value.Length + 2 <= maxChars)
                    sb.AppendLine(value);
            }
            current.Clear();
        }

        foreach (byte b in data)
        {
            if (sb.Length >= maxChars) break;

            if (b == 9 || (b >= 32 && b <= 126))
                current.Append((char)b);
            else
                Flush();
        }

        Flush();
        return sb.ToString();
    }

    private static string MakeHexDump(byte[] data, int maxBytes)
    {
        int length = Math.Min(data.Length, maxBytes);
        var sb = new StringBuilder(length * 4);

        for (int offset = 0; offset < length; offset += 16)
        {
            int count = Math.Min(16, length - offset);
            sb.Append(offset.ToString("X8")).Append("  ");

            for (int i = 0; i < 16; i++)
            {
                if (i < count) sb.Append(data[offset + i].ToString("X2")).Append(' ');
                else sb.Append("   ");

                if (i == 7) sb.Append(' ');
            }

            sb.Append(" |");

            for (int i = 0; i < count; i++)
            {
                byte b = data[offset + i];
                sb.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }

            sb.AppendLine("|");
        }

        return sb.ToString();
    }

    private void SetBusy(bool busy, string? message = null)
    {
        UseWaitCursor = busy;
        btnBrowse.Enabled = !busy;
        btnScan.Enabled = !busy;
        btnDbBackup.Enabled = !busy;
        btnServerPack.Enabled = !busy;
        btnGlobalSearch.Enabled = !busy;
        btnSave.Enabled = !busy;
        btnHash.Enabled = !busy;

        progress.Value = busy ? 10 : 0;

        if (!string.IsNullOrWhiteSpace(message))
            status.Text = message;
    }

    private sealed record DecodedText(string Text, string EncodingName);

    private sealed class ScanResult
    {
        public List<ViewRow> Rows { get; } = new();
        public Dictionary<string, AnyPakScanner> Scanners { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class ViewRow
    {
        [DisplayName("위치")] public string Source { get; set; } = "";
        [DisplayName("종류")] public string Type { get; set; } = "";
        [DisplayName("파일/내부경로")] public string Path { get; set; } = "";
        [DisplayName("IDX/PAK")] public string Container { get; set; } = "";
        [DisplayName("확장자")] public string Extension { get; set; } = "";
        [DisplayName("크기")] public long SizeBytes { get; set; }
        [DisplayName("검색일치")] public string Match { get; set; } = "";
        [Browsable(false)] public string SourceKey { get; set; } = "";
        [Browsable(false)] public string SearchText { get; set; } = "";
    }
}
