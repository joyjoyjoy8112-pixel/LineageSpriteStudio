using System.ComponentModel;
using System.Diagnostics;
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
    private readonly Button btnServerPack = new() { Text = "서버팩 폴더 선택", AutoSize = true };
    private readonly TextBox txtSearch = new() { Width = 260, PlaceholderText = "통합 검색 (예: 3000209 / autohunt / 61-)" };
    private readonly Button btnGlobalSearch = new() { Text = "통합 검색", AutoSize = true };
    private readonly ComboBox cboType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly Button btnSave = new() { Text = "선택 추출", AutoSize = true };
    private readonly Button btnOpenExtract = new() { Text = "추출 폴더 열기", AutoSize = true };
    private readonly Button btnHash = new() { Text = "SHA-256", AutoSize = true };
    private readonly Label lblCount = new() { AutoSize = true, Padding = new Padding(8, 7, 0, 0) };

    private readonly ContextMenuStrip filePathMenu = new();
    private ViewRow? contextMenuRow;
    private string? editFilePath;
    private Encoding? editEncoding;
    private bool editEmitBom;
    private string editOriginalText = "";
    private bool editMode;

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
        BackColor = Color.FromArgb(28, 31, 38)
    };

    private readonly PictureBox imageModifiedPreview = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(28, 31, 38)
    };

    private readonly Panel imageCompareHost = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label lblImageOriginalInfo = new()
    {
        Dock = DockStyle.Bottom,
        Height = 66,
        Padding = new Padding(8),
        AutoEllipsis = true
    };
    private readonly Label lblImageModifiedInfo = new()
    {
        Dock = DockStyle.Bottom,
        Height = 66,
        Padding = new Padding(8),
        AutoEllipsis = true
    };
    private readonly FlowLayoutPanel imageToolBar = new()
    {
        Dock = DockStyle.Top,
        Height = 46,
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        Padding = new Padding(8, 7, 8, 5),
        AutoScroll = true
    };
    private readonly Button btnLoadModifiedImage = new() { Text = "수정 이미지 불러오기", AutoSize = true };
    private readonly Button btnColorTest = new() { Text = "원본 색상 변경 테스트", AutoSize = true };
    private readonly Button btnResetModifiedImage = new() { Text = "수정본 초기화", AutoSize = true };
    private readonly Button btnApplyModifiedImage = new() { Text = "현재 수정본 원본에 저장", AutoSize = true };
    private readonly Label lblColorTestGuide = new()
    {
        Text = "※ 왼쪽 원본 유지 / 오른쪽 수정본에만 색상 적용",
        AutoSize = true,
        Margin = new Padding(12, 7, 0, 0)
    };

    private Bitmap? currentOriginalBitmap;
    private Bitmap? currentModifiedBitmap;
    private byte[]? currentModifiedImageBytes;
    private string currentModifiedImageFormat = "";

    private readonly SprEditorPanel sprEditor = new() { Dock = DockStyle.Fill, Visible = false };

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
    private readonly NumericUpDown numSprFrame = new() { Minimum = 0, Maximum = 0, Width = 88, Left = 70, Top = 8 };
    private readonly Button btnSprAuto = new() { Text = "자동 ▶", Width = 72, Height = 26, Left = 166, Top = 6 };
    private readonly Label lblSpr = new() { AutoSize = true, Left = 248, Top = 11 };
    private readonly System.Windows.Forms.Timer sprAutoTimer = new() { Interval = 120 };
    private byte[]? currentSprDecoded;
    private bool sprRenderBusy;

    private readonly ToolStripStatusLabel status = new() { Text = "준비" };
    private readonly ToolStripStatusLabel statusClient = new() { Text = "클라: 대기" };
    private readonly ToolStripStatusLabel statusServer = new() { Text = "서버: 대기" };
    private readonly ToolStripStatusLabel statusDb = new() { Text = "DB: 대기" };
    private readonly ToolStripProgressBar progress = new() { Minimum = 0, Maximum = 100, Width = 180 };

    private readonly List<ViewRow> allRows = new();
    private List<ViewRow> currentRows = new();
    private List<ViewRow>? globalSearchResults;
    private readonly Dictionary<string, AnyPakScanner> scanners = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ViewRow> externalBackupRows = new();
    private readonly List<ViewRow> externalServerRows = new();

    private string? rootPath;
    private string? serverRootPath;
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

        Text = "Lineage Client Inspector V3.4 - SPR 최소변경 저장";
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
            ColumnCount = 12,
            Padding = new Padding(6)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 1; i < 12; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
        top.Controls.Add(btnOpenExtract, 10, 0);
        top.Controls.Add(lblCount, 11, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 780
        };

        split.Panel1.Controls.Add(grid);

        var originalImagePanel = new Panel { Dock = DockStyle.Fill };
        originalImagePanel.Controls.Add(imagePreview);
        originalImagePanel.Controls.Add(lblImageOriginalInfo);

        var modifiedImagePanel = new Panel { Dock = DockStyle.Fill };
        modifiedImagePanel.Controls.Add(imageModifiedPreview);
        modifiedImagePanel.Controls.Add(lblImageModifiedInfo);

        var imageSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 360
        };
        imageSplit.Panel1.Controls.Add(originalImagePanel);
        imageSplit.Panel2.Controls.Add(modifiedImagePanel);

        imageToolBar.Controls.Add(btnLoadModifiedImage);
        imageToolBar.Controls.Add(btnColorTest);
        imageToolBar.Controls.Add(btnResetModifiedImage);
        imageToolBar.Controls.Add(btnApplyModifiedImage);
        imageToolBar.Controls.Add(lblColorTestGuide);

        imageCompareHost.Controls.Add(imageSplit);
        imageCompareHost.Controls.Add(imageToolBar);
        imageToolBar.BringToFront();

        previewHost.Controls.Add(textPreview);
        previewHost.Controls.Add(imageCompareHost);
        previewHost.Controls.Add(sprEditor);
        previewHost.Controls.Add(sprPreview);
        previewHost.Controls.Add(hexPreview);

        sprBar.Controls.Add(new Label { Text = "프레임", AutoSize = true, Left = 12, Top = 11 });
        sprBar.Controls.Add(numSprFrame);
        sprBar.Controls.Add(btnSprAuto);
        sprBar.Controls.Add(lblSpr);

        var right = new Panel { Dock = DockStyle.Fill };
        right.Controls.Add(previewHost);
        right.Controls.Add(sprBar);
        right.Controls.Add(lblInfo);
        split.Panel2.Controls.Add(right);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(status);
        statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        statusStrip.Items.Add(statusClient);
        statusStrip.Items.Add(new ToolStripSeparator());
        statusStrip.Items.Add(statusServer);
        statusStrip.Items.Add(new ToolStripSeparator());
        statusStrip.Items.Add(statusDb);
        statusStrip.Items.Add(new ToolStripSeparator());
        statusStrip.Items.Add(progress);

        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(statusStrip);

        var copyNameItem = new ToolStripMenuItem("이름 복사");
        copyNameItem.Click += (_, _) => CopyContextFileName();

        var editOriginalItem = new ToolStripMenuItem("원본 파일 수정");
        editOriginalItem.Click += async (_, _) => await BeginEditOriginalAsync();

        var restoreOriginalItem = new ToolStripMenuItem("원본 복원 (.bak)");
        restoreOriginalItem.Click += async (_, _) => await RestoreOriginalAsync();

        var compileJavaItem = new ToolStripMenuItem("이 Java 파일 컴파일");
        compileJavaItem.Click += async (_, _) => await CompileSelectedJavaAsync();

        var compileServerItem = new ToolStripMenuItem("서버 전체 컴파일 + JAR 생성");
        compileServerItem.Click += async (_, _) => await CompileWholeServerAsync();

        filePathMenu.Items.Add(copyNameItem);
        filePathMenu.Items.Add(new ToolStripSeparator());
        filePathMenu.Items.Add(editOriginalItem);
        filePathMenu.Items.Add(restoreOriginalItem);
        filePathMenu.Items.Add(new ToolStripSeparator());
        filePathMenu.Items.Add(compileJavaItem);
        filePathMenu.Items.Add(compileServerItem);
        filePathMenu.Opening += (_, _) =>
        {
            editOriginalItem.Enabled = contextMenuRow != null && CanEditOriginal(contextMenuRow);
            restoreOriginalItem.Enabled = contextMenuRow != null && CanRestoreOriginal(contextMenuRow);
            compileJavaItem.Enabled = contextMenuRow != null && CanCompileJava(contextMenuRow);
            compileServerItem.Enabled = CanCompileWholeServer();
        };

        grid.CellMouseDown += Grid_CellMouseDown;
        textPreview.KeyDown += TextPreview_KeyDown;

        btnBrowse.Click += (_, _) => ChooseFolder();
        btnScan.Click += async (_, _) => await ScanAsync();
        btnDbBackup.Click += async (_, _) => await ChooseDbBackupsAsync();
        btnServerPack.Click += async (_, _) => await ChooseServerFolderAsync();
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
        btnSprAuto.Click += (_, _) => ToggleSprAuto();
        sprAutoTimer.Tick += (_, _) => AdvanceSprFrame();
        btnSave.Click += async (_, _) => await SaveSelectedAsync();
        btnOpenExtract.Click += (_, _) => OpenExtractFolder();
        btnLoadModifiedImage.Click += async (_, _) => await LoadModifiedImageAsync();
        btnColorTest.Click += (_, _) => ApplyColorTest();
        btnResetModifiedImage.Click += (_, _) => ResetModifiedImage();
        btnApplyModifiedImage.Click += async (_, _) => await ApplyModifiedImageAsync();
        sprEditor.SaveBackAsync = SaveModifiedSprBackAsync;
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
            sprAutoTimer.Stop();
            foreach (var scanner in scanners.Values) scanner.Dispose();
            imagePreview.Image?.Dispose();
            imageModifiedPreview.Image?.Dispose();
            currentOriginalBitmap?.Dispose();
            currentModifiedBitmap?.Dispose();
            sprPreview.Image?.Dispose();
        };
    }

    private void Grid_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        var column = grid.Columns[e.ColumnIndex];
        if (column == null || !string.Equals(column.DataPropertyName, nameof(ViewRow.Path), StringComparison.Ordinal))
            return;

        grid.ClearSelection();
        grid.Rows[e.RowIndex].Selected = true;
        grid.CurrentCell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
        contextMenuRow = grid.Rows[e.RowIndex].DataBoundItem as ViewRow;

        if (contextMenuRow != null)
            filePathMenu.Show(Cursor.Position);
    }

    private void CopyContextFileName()
    {
        if (contextMenuRow == null || string.IsNullOrWhiteSpace(contextMenuRow.Path))
            return;

        string value = contextMenuRow.Path.TrimEnd('\\', '/');
        string name = Path.GetFileName(value);

        if (string.IsNullOrWhiteSpace(name))
            name = value;

        if (string.IsNullOrWhiteSpace(name))
            return;

        Clipboard.SetText(name);
        status.Text = $"이름 복사 완료: {name}";
    }

    private bool CanRestoreOriginal(ViewRow row)
    {
        string? path = GetOriginalFilePath(row);
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return File.Exists(path + ".bak");
    }

    private async Task RestoreOriginalAsync()
    {
        if (contextMenuRow == null)
            return;

        string? path = GetOriginalFilePath(contextMenuRow);
        if (string.IsNullOrWhiteSpace(path))
            return;

        string backup = path + ".bak";
        if (!File.Exists(backup))
        {
            MessageBox.Show(this, "복원할 .bak 백업 파일이 없습니다.",
                "원본 복원", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            $"백업 파일로 원본을 복원합니다.\n\n원본: {path}\n백업: {backup}\n\n현재 파일은 .before_restore 로 한 번 더 보관합니다.",
            "원본 복원 확인",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        try
        {
            string beforeRestore = path + ".before_restore";

            if (File.Exists(path))
                File.Copy(path, beforeRestore, overwrite: true);

            byte[] backupBytes = await File.ReadAllBytesAsync(backup);
            await File.WriteAllBytesAsync(path, backupBytes);

            currentRaw = backupBytes;

            try
            {
                contextMenuRow.SizeBytes = backupBytes.LongLength;

                if (TextExtensions.Contains(contextMenuRow.Extension))
                    contextMenuRow.SearchText = DecodeText(backupBytes).Text;
            }
            catch { }

            if (currentRow == contextMenuRow)
                await PreviewSelectedAsync();

            grid.Refresh();
            status.Text = $"원본 복원 완료: {path} | 복원 전 파일: {beforeRestore}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "원본 복원 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool CanEditOriginal(ViewRow row)
    {
        string? path = GetOriginalFilePath(row);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        string ext = Path.GetExtension(path);

        if (IsKnownBinaryExtension(ext))
            return false;

        try
        {
            var fi = new FileInfo(path);
            if (fi.Length > 20L * 1024 * 1024)
                return false;

            if (TextExtensions.Contains(ext))
                return true;

            int sampleLength = (int)Math.Min(fi.Length, 64 * 1024);
            byte[] sample = new byte[sampleLength];

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int read = fs.Read(sample, 0, sample.Length);

            if (read != sample.Length)
                Array.Resize(ref sample, read);

            return LooksLikeEditableText(sample);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsKnownBinaryExtension(string ext)
    {
        return ext.Equals(".class", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".pak", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".idx", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".spr", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".psc", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".nb3", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
               ImageExtensions.Contains(ext);
    }

    private static bool LooksLikeEditableText(byte[] data)
    {
        if (data.Length == 0)
            return true;

        if (data.Length >= 2 &&
            ((data[0] == 0xFF && data[1] == 0xFE) ||
             (data[0] == 0xFE && data[1] == 0xFF)))
            return true;

        int zero = data.Count(b => b == 0);
        if (zero > data.Length / 20)
            return false;

        try
        {
            string text = DecodeText(data).Text;
            if (text.Length == 0)
                return true;

            int badControls = text.Count(c =>
                char.IsControl(c) && c != '\r' && c != '\n' && c != '\t' && c != '\f');

            return badControls <= Math.Max(2, text.Length / 100);
        }
        catch
        {
            return false;
        }
    }

    private bool CanCompileJava(ViewRow row)
    {
        if (editMode)
            return false;

        if (row.Source != "서버팩")
            return false;

        if (!row.Extension.Equals(".java", StringComparison.OrdinalIgnoreCase))
            return false;

        return File.Exists(row.SourceKey) &&
               !string.IsNullOrWhiteSpace(serverRootPath) &&
               Directory.Exists(Path.Combine(serverRootPath, "src"));
    }

    private bool CanCompileWholeServer()
    {
        if (editMode)
            return false;

        if (string.IsNullOrWhiteSpace(serverRootPath))
            return false;

        return Directory.Exists(Path.Combine(serverRootPath, "src")) &&
               Directory.Exists(Path.Combine(serverRootPath, "lib"));
    }

    private async Task CompileSelectedJavaAsync()
    {
        if (contextMenuRow == null || !CanCompileJava(contextMenuRow))
            return;

        string root = serverRootPath!;
        string javaFile = contextMenuRow.SourceKey;
        string binDir = Path.Combine(root, "bin");
        string tempDir = Path.Combine(root, ".inspector_compile_one");

        Directory.CreateDirectory(binDir);

        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            ShowBuildOutput(
                $"[단일 Java 컴파일]\r\n파일: {javaFile}\r\n\r\nJDK 확인 중...\r\n");

            string? javac = FindJavaTool("javac.exe");
            if (javac == null)
                throw new InvalidOperationException(
                    "javac.exe를 찾을 수 없습니다. JDK 8을 설치하고 JAVA_HOME 또는 PATH를 설정하세요.");

            string classPath = BuildServerClassPath(root, includeBin: true);

            var args = new List<string>
            {
                "-encoding", "EUC-KR",
                "-source", "1.8",
                "-target", "1.8",
                "-implicit:none",
                "-classpath", classPath,
                "-sourcepath", Path.Combine(root, "src"),
                "-d", tempDir,
                javaFile
            };

            SetBusy(true, "Java 단일 파일 컴파일 중...");
            var result = await RunProcessAsync(javac, args, root, AppendBuildOutput);

            if (result.ExitCode != 0)
            {
                AppendBuildOutput($"\r\n[실패] javac 종료코드 {result.ExitCode}\r\n");
                status.Text = "Java 컴파일 실패 - 원본/class 변경 없음";
                return;
            }

            var classFiles = Directory.EnumerateFiles(tempDir, "*.class", SearchOption.AllDirectories).ToList();
            if (classFiles.Count == 0)
                throw new InvalidOperationException("컴파일은 성공했지만 생성된 .class 파일을 찾지 못했습니다.");

            int copied = 0;
            foreach (string compiled in classFiles)
            {
                string rel = Path.GetRelativePath(tempDir, compiled);
                string target = Path.Combine(binDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (File.Exists(target))
                {
                    string backup = target + ".bak";
                    if (!File.Exists(backup))
                        File.Copy(target, backup, overwrite: false);
                }

                File.Copy(compiled, target, overwrite: true);
                copied++;
                AppendBuildOutput($"적용: bin\\{rel}\r\n");
            }

            AppendBuildOutput($"\r\n[성공] {copied}개 class 적용 완료\r\n");
            status.Text = $"Java 컴파일 성공: {Path.GetFileName(javaFile)} → {copied}개 class";
        }
        catch (Exception ex)
        {
            AppendBuildOutput($"\r\n[오류] {ex}\r\n");
            MessageBox.Show(this, ex.Message, "Java 컴파일 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            status.Text = "Java 컴파일 실패";
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }

            SetBusy(false);
        }
    }

    private async Task CompileWholeServerAsync()
    {
        if (!CanCompileWholeServer())
            return;

        string root = serverRootPath!;
        string srcDir = Path.Combine(root, "src");
        string tempDir = Path.Combine(root, ".inspector_full_build");
        string tempJar = Path.Combine(root, "l1jserver.jar.inspector_new");
        string finalJar = Path.Combine(root, "l1jserver.jar");
        string manifest = Path.Combine(srcDir, "META-INF", "MANIFEST.MF");

        var answer = MessageBox.Show(this,
            "서버 전체 Java 소스를 컴파일하고 l1jserver.jar를 새로 만듭니다.\n\n" +
            "컴파일/새 JAR 생성이 모두 성공한 뒤에만 기존 JAR를 교체합니다.\n진행할까요?",
            "서버 전체 컴파일",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            if (File.Exists(tempJar))
                File.Delete(tempJar);

            string? javac = FindJavaTool("javac.exe");
            string? jarTool = FindJavaTool("jar.exe");

            if (javac == null || jarTool == null)
                throw new InvalidOperationException(
                    "JDK의 javac.exe / jar.exe를 찾을 수 없습니다. JDK 8을 설치하고 JAVA_HOME 또는 PATH를 설정하세요.");

            var javaFiles = Directory.EnumerateFiles(srcDir, "*.java", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (javaFiles.Count == 0)
                throw new InvalidOperationException("src 폴더에서 Java 소스를 찾지 못했습니다.");

            ShowBuildOutput(
                $"[서버 전체 컴파일]\r\n서버팩: {root}\r\nJava 소스: {javaFiles.Count:N0}개\r\n\r\n");

            string argFile = Path.Combine(tempDir, "sources.txt");
            await File.WriteAllLinesAsync(
                argFile,
                javaFiles.Select(p => "\"" + p.Replace("\\", "\\\\") + "\""),
                new UTF8Encoding(false));

            string classPath = BuildServerClassPath(root, includeBin: false);

            var javacArgs = new List<string>
            {
                "-encoding", "EUC-KR",
                "-source", "1.8",
                "-target", "1.8",
                "-classpath", classPath,
                "-d", tempDir,
                "@" + argFile
            };

            SetBusy(true, "서버 전체 Java 컴파일 중...");
            var compileResult = await RunProcessAsync(javac, javacArgs, root, AppendBuildOutput);

            if (compileResult.ExitCode != 0)
            {
                AppendBuildOutput($"\r\n[컴파일 실패] 종료코드 {compileResult.ExitCode}\r\n기존 JAR는 변경하지 않았습니다.\r\n");
                status.Text = "서버 전체 컴파일 실패 - 기존 JAR 유지";
                return;
            }

            if (!File.Exists(manifest))
                throw new FileNotFoundException("MANIFEST.MF를 찾을 수 없습니다.", manifest);

            AppendBuildOutput("\r\nJava 컴파일 성공. JAR 생성 중...\r\n");

            var jarArgs = new List<string>
            {
                "cfm",
                tempJar,
                manifest,
                "-C",
                tempDir,
                "."
            };

            var jarResult = await RunProcessAsync(jarTool, jarArgs, root, AppendBuildOutput);

            if (jarResult.ExitCode != 0 || !File.Exists(tempJar))
            {
                AppendBuildOutput($"\r\n[JAR 생성 실패] 종료코드 {jarResult.ExitCode}\r\n기존 JAR는 변경하지 않았습니다.\r\n");
                status.Text = "JAR 생성 실패 - 기존 JAR 유지";
                return;
            }

            string backup = finalJar + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            if (File.Exists(finalJar))
                File.Copy(finalJar, backup, overwrite: false);

            File.Copy(tempJar, finalJar, overwrite: true);

            AppendBuildOutput(
                $"\r\n[성공] l1jserver.jar 생성 완료\r\n" +
                $"JAR: {finalJar}\r\n" +
                (File.Exists(backup) ? $"기존 JAR 백업: {backup}\r\n" : ""));

            status.Text = $"서버 전체 컴파일 성공: Java {javaFiles.Count:N0}개 → l1jserver.jar";
        }
        catch (Exception ex)
        {
            AppendBuildOutput($"\r\n[오류] {ex}\r\n");
            MessageBox.Show(this, ex.Message, "서버 전체 컴파일 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            status.Text = "서버 전체 컴파일 실패";
        }
        finally
        {
            try
            {
                if (File.Exists(tempJar))
                    File.Delete(tempJar);
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }

            SetBusy(false);
        }
    }

    private static string BuildServerClassPath(string root, bool includeBin)
    {
        var parts = new List<string>();

        if (includeBin)
        {
            string bin = Path.Combine(root, "bin");
            if (Directory.Exists(bin))
                parts.Add(bin);
        }

        string lib = Path.Combine(root, "lib");
        if (Directory.Exists(lib))
        {
            foreach (string jar in Directory.EnumerateFiles(lib, "*.jar", SearchOption.TopDirectoryOnly)
                         .Where(p => !Path.GetFileName(p).StartsWith("old_", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(jar);
            }
        }

        string serverJar = Path.Combine(root, "l1jserver.jar");
        if (includeBin && File.Exists(serverJar))
            parts.Add(serverJar);

        return string.Join(Path.PathSeparator, parts);
    }

    private static string? FindJavaTool(string toolName)
    {
        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");

        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            string candidate = Path.Combine(javaHome, "bin", toolName);
            if (File.Exists(candidate))
                return candidate;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim().Trim('"'), toolName);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch { }
            }
        }

        return null;
    }

    private async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        Action<string> onOutput)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string arg in arguments)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                BeginInvoke(() => onOutput(e.Data + Environment.NewLine));
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                BeginInvoke(() => onOutput(e.Data + Environment.NewLine));
        };

        if (!process.Start())
            throw new InvalidOperationException("프로세스를 시작할 수 없습니다: " + fileName);

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode);
    }

    private void ShowBuildOutput(string initial)
    {
        if (editMode)
            CancelOriginalEdit();

        ClearPreview();
        textPreview.ReadOnly = true;
        textPreview.Text = initial;
        textPreview.Visible = true;
        textPreview.BringToFront();
        lblInfo.Text = "컴파일 로그";
        Application.DoEvents();
    }

    private void AppendBuildOutput(string text)
    {
        if (textPreview.InvokeRequired)
        {
            textPreview.BeginInvoke(() => AppendBuildOutput(text));
            return;
        }

        textPreview.AppendText(text);
        textPreview.SelectionStart = textPreview.TextLength;
        textPreview.ScrollToCaret();
        Application.DoEvents();
    }

    private string? GetOriginalFilePath(ViewRow row)
    {
        if (row.Source == "실제파일")
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                return null;

            return Path.Combine(rootPath, row.SourceKey);
        }

        if (row.Source == "서버팩" || row.Source == "DB백업")
            return row.SourceKey;

        return null;
    }

    private async Task BeginEditOriginalAsync()
    {
        if (contextMenuRow == null || !CanEditOriginal(contextMenuRow))
        {
            MessageBox.Show(this,
                "이 항목은 원본 직접 수정 대상이 아닙니다.\n실제 텍스트 파일과 서버팩 폴더의 텍스트 파일만 직접 수정할 수 있습니다.",
                "원본 파일 수정", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string? path = GetOriginalFilePath(contextMenuRow);
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            byte[] data = await File.ReadAllBytesAsync(path);
            var info = DetectEditableEncoding(data);
            string text = DecodeWithEncoding(data, info);

            currentRow = contextMenuRow;
            currentRaw = data;
            currentName = Path.GetFileName(path);

            ClearPreview();

            editFilePath = path;
            editEncoding = info.Encoding;
            editEmitBom = info.EmitBom;
            editOriginalText = text;
            editMode = true;

            textPreview.ReadOnly = false;
            textPreview.Text = text;
            textPreview.Visible = true;
            textPreview.BringToFront();
            textPreview.Focus();

            grid.Enabled = false;
            lblInfo.Text =
                $"원본 편집 | {contextMenuRow.Path} | {info.Name} | Ctrl+S 저장 / Esc 취소";
            status.Text = "원본 편집 중 - Ctrl+S 저장 / Esc 취소";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "원본 파일 열기 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void TextPreview_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!editMode)
            return;

        if (e.Control && e.KeyCode == Keys.S)
        {
            e.SuppressKeyPress = true;
            await SaveOriginalEditAsync();
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            CancelOriginalEdit();
        }
    }

    private async Task SaveOriginalEditAsync()
    {
        if (!editMode || string.IsNullOrWhiteSpace(editFilePath) || editEncoding == null)
            return;

        try
        {
            string backup = editFilePath + ".bak";

            if (!File.Exists(backup))
                File.Copy(editFilePath, backup, overwrite: false);

            string text = textPreview.Text;
            byte[] body = editEncoding.GetBytes(text);

            byte[] output;
            byte[] preamble = editEmitBom ? editEncoding.GetPreamble() : Array.Empty<byte>();

            if (preamble.Length > 0)
            {
                output = new byte[preamble.Length + body.Length];
                Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
                Buffer.BlockCopy(body, 0, output, preamble.Length, body.Length);
            }
            else
            {
                output = body;
            }

            await File.WriteAllBytesAsync(editFilePath, output);

            currentRaw = output;
            editOriginalText = text;

            if (currentRow != null)
            {
                try
                {
                    currentRow.SizeBytes = output.LongLength;
                    currentRow.SearchText = text;
                }
                catch { }
            }

            string saved = editFilePath;
            ExitEditMode();
            status.Text = $"원본 저장 완료: {saved} | 백업: {backup}";
            grid.Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "원본 저장 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CancelOriginalEdit()
    {
        if (!editMode)
            return;

        textPreview.Text = editOriginalText;
        ExitEditMode();
        status.Text = "원본 수정 취소";
    }

    private void ExitEditMode()
    {
        editMode = false;
        editFilePath = null;
        editEncoding = null;
        editEmitBom = false;
        editOriginalText = "";

        textPreview.ReadOnly = true;
        grid.Enabled = true;
        grid.Focus();
    }

    private static EditableEncodingInfo DetectEditableEncoding(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return new EditableEncodingInfo(new UTF8Encoding(false), true, 3, "UTF-8 BOM");

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return new EditableEncodingInfo(new UnicodeEncoding(false, false), true, 2, "UTF-16 LE");

        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return new EditableEncodingInfo(new UnicodeEncoding(true, false), true, 2, "UTF-16 BE");

        try
        {
            _ = new UTF8Encoding(false, true).GetString(data);
            return new EditableEncodingInfo(new UTF8Encoding(false), false, 0, "UTF-8");
        }
        catch
        {
            return new EditableEncodingInfo(Encoding.GetEncoding(949), false, 0, "CP949/EUC-KR");
        }
    }

    private static string DecodeWithEncoding(byte[] data, EditableEncodingInfo info)
    {
        int offset = Math.Min(info.PreambleLength, data.Length);
        return info.Encoding.GetString(data, offset, data.Length - offset);
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

    private async Task ChooseServerFolderAsync()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "검색할 서버팩 폴더를 선택하세요.",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string serverRoot = dlg.SelectedPath;
        serverRootPath = serverRoot;

        SetBusy(true, "서버팩 폴더 분석 중...");
        try
        {
            externalServerRows.Clear();

            await Task.Run(() => IndexServerFolder(serverRoot, externalServerRows));

            foreach (var row in allRows.Where(x => x.Source == "서버팩").ToList())
                allRows.Remove(row);

            allRows.AddRange(externalServerRows);
            globalSearchResults = null;
            ApplyFilter();

            int textCount = externalServerRows.Count(x => !string.IsNullOrEmpty(x.SearchText));
            int pscCount = externalServerRows.Count(x => x.Extension.Equals(".psc", StringComparison.OrdinalIgnoreCase));

            status.Text =
                $"서버팩 폴더 추가 완료: {Path.GetFileName(serverRoot)} | 파일 {externalServerRows.Count:N0}개 | " +
                $"내용검색 {textCount:N0}개 | PSC {pscCount:N0}개";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "서버팩 폴더 분석 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            status.Text = "서버팩 폴더 분석 실패";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static void IndexServerFolder(string serverRoot, List<ViewRow> output)
    {
        foreach (string path in Directory.EnumerateFiles(serverRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            FileInfo fi;

            try
            {
                fi = new FileInfo(path);
            }
            catch
            {
                continue;
            }

            string rel;
            try
            {
                rel = Path.GetRelativePath(serverRoot, path);
            }
            catch
            {
                rel = fi.Name;
            }

            string ext = fi.Extension;

            var row = new ViewRow
            {
                Source = "서버팩",
                Type = DetectType(rel),
                Path = rel,
                Container = serverRoot,
                Extension = ext,
                SizeBytes = fi.Length,
                SourceKey = path
            };

            // 서버팩 폴더의 실제 소스/설정/스크립트는 내용까지 색인한다.
            // .class/.jar/.dll/.exe 등 바이너리는 파일명 검색만 허용한다.
            if (TextExtensions.Contains(ext) && fi.Length <= 4L * 1024 * 1024)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    row.SearchText = DecodeText(bytes).Text;
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

        currentRows.Sort(CompareRowsNatural);

        grid.DataSource = null;
        grid.DataSource = new BindingList<ViewRow>(currentRows);

        if (globalSearchResults != null)
            lblCount.Text = $"검색 {currentRows.Count:N0}/{globalSearchResults.Count:N0}";
        else
            lblCount.Text = $"{currentRows.Count:N0}/{allRows.Count:N0}";
    }

    private static int CompareRowsNatural(ViewRow? a, ViewRow? b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        string aName = Path.GetFileName(a.Path.TrimEnd('\\', '/'));
        string bName = Path.GetFileName(b.Path.TrimEnd('\\', '/'));

        int cmp = NaturalCompare(aName, bName);
        if (cmp != 0) return cmp;

        cmp = NaturalCompare(a.Path, b.Path);
        if (cmp != 0) return cmp;

        cmp = string.Compare(a.Source, b.Source, StringComparison.OrdinalIgnoreCase);
        if (cmp != 0) return cmp;

        return NaturalCompare(a.Container, b.Container);
    }

    private static int NaturalCompare(string? a, string? b)
    {
        a ??= "";
        b ??= "";

        int ia = 0;
        int ib = 0;

        while (ia < a.Length && ib < b.Length)
        {
            char ca = a[ia];
            char cb = b[ib];

            if (char.IsDigit(ca) && char.IsDigit(cb))
            {
                int aStart = ia;
                int bStart = ib;

                while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                while (ib < b.Length && char.IsDigit(b[ib])) ib++;

                string aNum = a[aStart..ia].TrimStart('0');
                string bNum = b[bStart..ib].TrimStart('0');

                if (aNum.Length == 0) aNum = "0";
                if (bNum.Length == 0) bNum = "0";

                if (aNum.Length != bNum.Length)
                    return aNum.Length.CompareTo(bNum.Length);

                int ncmp = string.Compare(aNum, bNum, StringComparison.Ordinal);
                if (ncmp != 0)
                    return ncmp;

                int rawLenA = ia - aStart;
                int rawLenB = ib - bStart;
                if (rawLenA != rawLenB)
                    return rawLenA.CompareTo(rawLenB);

                continue;
            }

            int ccmp = char.ToUpperInvariant(ca).CompareTo(char.ToUpperInvariant(cb));
            if (ccmp != 0)
                return ccmp;

            ia++;
            ib++;
        }

        return (a.Length - ia).CompareTo(b.Length - ib);
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
            ResetSearchSourceStatus();
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
        ResetSearchSourceStatus();

        try
        {
            var results = new List<ViewRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool truncated = false;
            bool shortNumeric = q.All(char.IsDigit) && q.Length <= 3;
            bool filePrefixOnly = IsNumericDashPrefixQuery(q);

            int clientHits = 0;
            int serverHits = 0;
            int dbHits = 0;

            void AddResult(ViewRow row, string area)
            {
                string key = row.Source + "\n" + row.Path + "\n" + row.Container + "\n" + row.Match;
                if (!seen.Add(key)) return;

                if (results.Count < MaxResults)
                {
                    results.Add(row);

                    if (area == "클라") clientHits++;
                    else if (area == "서버") serverHits++;
                    else if (area == "DB") dbHits++;
                }
                else
                {
                    truncated = true;
                }
            }

            async Task SearchRowsPhaseAsync(List<ViewRow> rows, string area)
            {
                int total = rows.Count;
                int done = 0;

                SetSourceSearching(area, clientHits, serverHits, dbHits);
                Application.DoEvents();

                foreach (var row in rows)
                {
                    if (results.Count >= MaxResults)
                    {
                        truncated = true;
                        break;
                    }

                    string? match = null;

                    if (FileNameStartsWith(row.Path, q))
                    {
                        match = "파일명 시작일치";
                    }
                    else if (!filePrefixOnly && !shortNumeric &&
                             !string.IsNullOrEmpty(row.SearchText) &&
                             StrictContains(row.SearchText, q))
                    {
                        match = "의미있는 내용";
                    }
                    else if (!filePrefixOnly && !shortNumeric &&
                             ShouldContentSearch(row) &&
                             await TextContentContainsAsync(row, q))
                    {
                        match = "텍스트 내용";
                    }

                    if (match != null)
                    {
                        row.Match = match;
                        AddResult(row, area);
                    }

                    done++;
                    if (done % 100 == 0 || done == total)
                    {
                        progress.Value = total == 0 ? 0 : Math.Min(100, (int)(done * 100L / total));
                        status.Text = $"{area} 검색 {done:N0}/{total:N0} | 전체 발견 {results.Count:N0}";
                        UpdateSourceHitDisplay(area, clientHits, serverHits, dbHits);
                        Application.DoEvents();
                    }
                }

                SetSourceCompleted(area, clientHits, serverHits, dbHits);
                Application.DoEvents();
            }

            // 1) Navicat / DB 백업
            SetSourceSearching("DB", clientHits, serverHits, dbHits);
            Application.DoEvents();

            if (!shortNumeric && !filePrefixOnly)
            {
                var dbPscRows = allRows
                    .Where(r => r.Source == "DB백업" &&
                                r.Extension.Equals(".psc", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var psc in dbPscRows)
                {
                    var pscHits = await SearchPscAsync(psc, q, Math.Min(50, MaxResults - results.Count));
                    foreach (var hit in pscHits)
                        AddResult(hit, "DB");

                    status.Text = $"DB PSC 검색: {psc.Path} | DB 발견 {dbHits:N0}";
                    UpdateSourceHitDisplay("DB", clientHits, serverHits, dbHits);
                    Application.DoEvents();

                    if (results.Count >= MaxResults)
                    {
                        truncated = true;
                        break;
                    }
                }

                if (results.Count < MaxResults)
                {
                    var sqlBackups = allRows
                        .Where(r => r.Source == "DB백업" &&
                                    r.Extension.Equals(".sql", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var sql in sqlBackups)
                    {
                        var sqlMatches = await Task.Run(() => SearchSqlDump(sql.SourceKey, q, MaxResults - results.Count));
                        foreach (var hit in sqlMatches)
                            AddResult(hit, "DB");

                        status.Text = $"DB SQL 검색: {Path.GetFileName(sql.SourceKey)} | DB 발견 {dbHits:N0}";
                        UpdateSourceHitDisplay("DB", clientHits, serverHits, dbHits);
                        Application.DoEvents();

                        if (results.Count >= MaxResults)
                        {
                            truncated = true;
                            break;
                        }
                    }
                }
            }

            if (results.Count < MaxResults)
            {
                var dbRows = allRows
                    .Where(r => r.Source == "DB백업" || r.Source == "PSC 내부")
                    .Where(r => !(r.Source == "DB백업" && r.Extension.Equals(".sql", StringComparison.OrdinalIgnoreCase)))
                    .Where(r => !(r.Source == "DB백업" && r.Extension.Equals(".psc", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                await SearchRowsPhaseAsync(dbRows, "DB");
            }
            else
            {
                SetSourceCompleted("DB", clientHits, serverHits, dbHits);
            }

            // 2) 서버팩 폴더
            if (results.Count < MaxResults)
            {
                SetSourceSearching("서버", clientHits, serverHits, dbHits);
                Application.DoEvents();

                if (!shortNumeric && !filePrefixOnly)
                {
                    var serverPscRows = allRows
                        .Where(r => r.Source == "서버팩" &&
                                    r.Extension.Equals(".psc", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var psc in serverPscRows)
                    {
                        var pscHits = await SearchPscAsync(psc, q, Math.Min(50, MaxResults - results.Count));
                        foreach (var hit in pscHits)
                            AddResult(hit, "서버");

                        status.Text = $"서버 PSC 검색: {psc.Path} | 서버 발견 {serverHits:N0}";
                        UpdateSourceHitDisplay("서버", clientHits, serverHits, dbHits);
                        Application.DoEvents();

                        if (results.Count >= MaxResults)
                        {
                            truncated = true;
                            break;
                        }
                    }
                }

                if (results.Count < MaxResults)
                {
                    var serverRows = allRows
                        .Where(r => r.Source == "서버팩")
                        .Where(r => !r.Extension.Equals(".psc", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    await SearchRowsPhaseAsync(serverRows, "서버");
                }
                else
                {
                    SetSourceCompleted("서버", clientHits, serverHits, dbHits);
                }
            }

            // 3) 클라이언트 + PAK 내부
            if (results.Count < MaxResults)
            {
                var clientRows = allRows
                    .Where(r => r.Source == "실제파일" || r.Source == "PAK 내부")
                    .ToList();

                await SearchRowsPhaseAsync(clientRows, "클라");
            }

            globalSearchResults = results;
            ApplyFilter();

            if (shortNumeric)
            {
                status.Text =
                    $"정확 검색 완료: '{q}' → {results.Count:N0}건 | " +
                    $"클라 {clientHits:N0} / 서버 {serverHits:N0} / DB {dbHits:N0} | " +
                    "1~3자리 숫자는 DB/본문 검색 제외";
            }
            else
            {
                status.Text = truncated
                    ? $"정확 검색: '{q}' → {results.Count:N0}건 표시 | 클라 {clientHits:N0} / 서버 {serverHits:N0} / DB {dbHits:N0} | 500건 초과"
                    : $"정확 검색 완료: '{q}' → {results.Count:N0}건 | 클라 {clientHits:N0} / 서버 {serverHits:N0} / DB {dbHits:N0}";
            }

            FinishSearchSourceStatus(clientHits, serverHits, dbHits);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "통합 검색 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            status.Text = "통합 검색 실패";
            statusClient.Text = "클라: 오류";
            statusServer.Text = "서버: 오류";
            statusDb.Text = "DB: 오류";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ResetSearchSourceStatus()
    {
        bool hasClient = allRows.Any(r => r.Source == "실제파일" || r.Source == "PAK 내부");
        bool hasServer = allRows.Any(r => r.Source == "서버팩");
        bool hasDb = allRows.Any(r => r.Source == "DB백업" || r.Source == "PSC 내부");

        statusClient.Text = hasClient ? "클라: 대기" : "클라: 미선택";
        statusServer.Text = hasServer ? "서버: 대기" : "서버: 미선택";
        statusDb.Text = hasDb ? "DB: 대기" : "DB: 미선택";
    }

    private void SetSourceSearching(string area, int clientHits, int serverHits, int dbHits)
    {
        if (area == "클라")
            statusClient.Text = $"클라: 검색중... ({clientHits:N0})";
        else if (area == "서버")
            statusServer.Text = $"서버: 검색중... ({serverHits:N0})";
        else if (area == "DB")
            statusDb.Text = $"DB: 검색중... ({dbHits:N0})";
    }

    private void UpdateSourceHitDisplay(string area, int clientHits, int serverHits, int dbHits)
    {
        SetSourceSearching(area, clientHits, serverHits, dbHits);
    }

    private void SetSourceCompleted(string area, int clientHits, int serverHits, int dbHits)
    {
        if (area == "클라")
            statusClient.Text = $"클라: 완료 {clientHits:N0}건";
        else if (area == "서버")
            statusServer.Text = $"서버: 완료 {serverHits:N0}건";
        else if (area == "DB")
            statusDb.Text = $"DB: 완료 {dbHits:N0}건";
    }

    private void FinishSearchSourceStatus(int clientHits, int serverHits, int dbHits)
    {
        bool hasClient = allRows.Any(r => r.Source == "실제파일" || r.Source == "PAK 내부");
        bool hasServer = allRows.Any(r => r.Source == "서버팩");
        bool hasDb = allRows.Any(r => r.Source == "DB백업" || r.Source == "PSC 내부");

        statusClient.Text = hasClient ? $"클라: 완료 {clientHits:N0}건" : "클라: 미선택";
        statusServer.Text = hasServer ? $"서버: 완료 {serverHits:N0}건" : "서버: 미선택";
        statusDb.Text = hasDb ? $"DB: 완료 {dbHits:N0}건" : "DB: 미선택";
    }

    private async Task<List<ViewRow>> SearchPscAsync(ViewRow sourceRow, string query, int limit)
    {
        var results = new List<ViewRow>();
        if (limit <= 0 || string.IsNullOrWhiteSpace(query))
            return results;

        try
        {
            byte[] compressed = await ReadBytesAsync(sourceRow);
            byte[] data = InflatePscIfNeeded(compressed);

            var offsets = FindStrictOffsets(data, query, limit);
            foreach (long offset in offsets)
            {
                string context = MakePscContext(data, offset, query);

                results.Add(new ViewRow
                {
                    Source = "PSC 검색",
                    Type = "DB백업",
                    Path = sourceRow.Path,
                    Container = sourceRow.Container,
                    Extension = ".psc",
                    SizeBytes = context.Length,
                    SourceKey = sourceRow.SourceKey,
                    SearchText = context,
                    Match = $"PSC 실제값 @ 0x{offset:X}"
                });
            }
        }
        catch
        {
            // 압축 형식이 다른 PSC는 일반 파일명 검색만 유지한다.
        }

        return results;
    }

    private static byte[] InflatePscIfNeeded(byte[] data)
    {
        if (data.Length < 2)
            return data;

        bool looksZlib =
            data[0] == 0x78 &&
            (data[1] == 0x01 || data[1] == 0x5E || data[1] == 0x9C || data[1] == 0xDA);

        if (!looksZlib)
            return data;

        using var input = new MemoryStream(data, writable: false);
        using var zs = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zs.CopyTo(output);
        return output.ToArray();
    }

    private static List<long> FindStrictOffsets(byte[] data, string query, int limit)
    {
        var result = new List<long>();
        if (data.Length == 0 || string.IsNullOrEmpty(query) || limit <= 0)
            return result;

        bool numeric = query.All(char.IsDigit);
        var patterns = BuildSearchPatterns(query);

        foreach (var pattern in patterns)
        {
            int start = 0;

            while (start <= data.Length - pattern.Length && result.Count < limit)
            {
                int idx = data.AsSpan(start).IndexOf(pattern);
                if (idx < 0) break;
                idx += start;

                bool valid = true;

                if (numeric && pattern.All(b => b >= (byte)'0' && b <= (byte)'9'))
                {
                    bool leftOk = idx == 0 || data[idx - 1] < (byte)'0' || data[idx - 1] > (byte)'9';
                    int after = idx + pattern.Length;
                    bool rightOk = after >= data.Length || data[after] < (byte)'0' || data[after] > (byte)'9';
                    valid = leftOk && rightOk;
                }

                if (valid && !result.Contains(idx))
                    result.Add(idx);

                start = idx + Math.Max(1, pattern.Length);
            }

            if (result.Count >= limit)
                break;
        }

        result.Sort();
        return result;
    }

    private static string MakePscContext(byte[] data, long offset, string query)
    {
        int center = (int)Math.Clamp(offset, 0, data.Length);
        int start = Math.Max(0, center - 700);
        int end = Math.Min(data.Length, center + Math.Max(query.Length, 1) + 1200);
        byte[] slice = data[start..end];

        Encoding enc;
        int probe = Math.Min(data.Length, 512);

        if (Encoding.ASCII.GetString(data, 0, probe).Contains("euckr", StringComparison.OrdinalIgnoreCase))
            enc = Encoding.GetEncoding(949);
        else
            enc = new UTF8Encoding(false, false);

        string text = enc.GetString(slice);

        var sb = new StringBuilder(text.Length);
        bool lastSep = false;

        foreach (char c in text)
        {
            bool control = char.IsControl(c) && c != '\r' && c != '\n' && c != '\t';

            if (control)
            {
                if (!lastSep)
                {
                    sb.Append(" · ");
                    lastSep = true;
                }
            }
            else
            {
                sb.Append(c);
                lastSep = false;
            }
        }

        return $"[PSC 압축해제 검색]\r\n원본 Offset: 0x{offset:X}\r\n\r\n" + sb;
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

    private static bool FileNameStartsWith(string path, string query)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(query))
            return false;

        string value = path.TrimEnd('\\', '/');
        string fileName = Path.GetFileName(value);

        if (string.IsNullOrEmpty(fileName))
            fileName = value;

        return fileName.StartsWith(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumericDashPrefixQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        int dash = query.IndexOf('-');
        if (dash <= 0)
            return false;

        for (int i = 0; i < dash; i++)
        {
            if (!char.IsDigit(query[i]))
                return false;
        }

        return true;
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
                string sourceText = row.Source == "PAK 내부"
                    ? $"PAK 내부 | {row.Container}"
                    : GetOriginalFilePath(row) ?? row.Source;

                sprEditor.LoadSpr(currentRaw, row.Path, sourceText);
                sprEditor.Visible = true;
                sprEditor.BringToFront();

                currentSprDecoded = SpriteCodec.DecodeIfNeeded(currentRaw);
                var info = SprInfo.Analyze(currentSprDecoded);

                lblInfo.Text += $" | {info.FrameCount}프레임 | " +
                                $"{(info.IsPalette ? "Palette " + info.PaletteSize : "RGB555")} | " +
                                $"Type {info.FrameType} | {(SpriteCodec.IsZlib(currentRaw) ? "ZLIB" : "RAW")}";
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

        if (row.Source == "서버팩")
            return await File.ReadAllBytesAsync(row.SourceKey);

        if (row.Source == "DB SQL" || row.Source == "PSC 검색")
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

    private async Task SaveModifiedSprBackAsync(byte[] storedSpr)
    {
        if (currentRow == null)
            throw new InvalidOperationException("현재 선택된 SPR이 없습니다.");

        if (!currentRow.Extension.Equals(".spr", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("현재 선택 항목이 SPR이 아닙니다.");

        if (currentRow.Source == "실제파일" || currentRow.Source == "서버팩")
        {
            string? path = GetOriginalFilePath(currentRow);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("원본 SPR 파일을 찾을 수 없습니다.", path);

            string firstBackup = path + ".spr.bak";
            if (!File.Exists(firstBackup))
                File.Copy(path, firstBackup, false);

            string before = path + ".spr.before_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(path, before, false);

            await File.WriteAllBytesAsync(path, storedSpr);

            currentRaw = (byte[])storedSpr.Clone();
            currentSprDecoded = SpriteCodec.DecodeIfNeeded(currentRaw);
            currentRow.SizeBytes = storedSpr.LongLength;
            grid.Refresh();

            status.Text = $"SPR 원본 저장 완료: {path} | 백업: {firstBackup}";
            return;
        }

        if (currentRow.Source == "PAK 내부")
        {
            string idxPath = currentRow.SourceKey;

            if (!scanners.TryGetValue(idxPath, out var scanner))
                throw new InvalidOperationException("선택한 SPR의 IDX/PAK 스캐너를 찾을 수 없습니다.");

            bool supportedPak =
                scanner.Format.Equals("LEGACY28", StringComparison.OrdinalIgnoreCase) ||
                scanner.Format.Equals("_EXT", StringComparison.OrdinalIgnoreCase);

            if (!supportedPak || scanner.DesEncrypted)
            {
                throw new InvalidOperationException(
                    $"현재 PAK 형식은 {scanner.Format}{(scanner.DesEncrypted ? " / DES" : "")} 입니다. " +
                    "V3.4에서는 비암호화 LEGACY28 및 비암호화 _EXT PAK의 SPR 최소변경 저장을 지원합니다.");
            }

            string pakPath = Path.ChangeExtension(idxPath, ".pak");
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string idxBackup = idxPath + ".sprbak_" + stamp;
            string pakBackup = pakPath + ".sprbak_" + stamp;

            File.Copy(idxPath, idxBackup, false);
            File.Copy(pakPath, pakBackup, false);

            try
            {
                string saveMode;
                using (var pak = new SpritePak(idxPath))
                {
                    saveMode = pak.ReplaceEntryMinimal(currentRow.Path, storedSpr);
                }

                // 전체 PAK을 재묶지 않고 대상 엔트리 하나만 최소 변경한 뒤 재검증한다.
                using (var verify = new AnyPakScanner(idxPath))
                {
                    var verifyEntry = verify.Entries.FirstOrDefault(x =>
                        x.FileName.Equals(currentRow.Path, StringComparison.OrdinalIgnoreCase));

                    if (verifyEntry == null)
                        throw new InvalidDataException("저장 후 SPR 엔트리를 다시 찾지 못했습니다.");

                    byte[] verifyBytes = verify.Extract(verifyEntry);
                    if (!verifyBytes.AsSpan().SequenceEqual(storedSpr))
                    {
                        throw new InvalidDataException(
                            $"저장 후 SPR 검증 실패: expected={storedSpr.Length:N0}, actual={verifyBytes.Length:N0}");
                    }
                }
            }
            catch
            {
                // 검증/저장 중 하나라도 실패하면 방금 만든 백업으로 IDX/PAK를 자동 복원한다.
                try { File.Copy(idxBackup, idxPath, true); } catch { }
                try { File.Copy(pakBackup, pakPath, true); } catch { }

                try
                {
                    scanner.Dispose();
                    scanners[idxPath] = new AnyPakScanner(idxPath);
                }
                catch { }

                status.Text = "SPR 저장 실패 - IDX/PAK 자동 복원 완료";
                throw;
            }

            scanner.Dispose();
            scanners[idxPath] = new AnyPakScanner(idxPath);

            currentRaw = (byte[])storedSpr.Clone();
            currentSprDecoded = SpriteCodec.DecodeIfNeeded(currentRaw);
            currentRow.SizeBytes = storedSpr.LongLength;
            grid.Refresh();

            status.Text =
                $"PAK SPR 최소변경 저장/검증 완료: {currentRow.Path} | mode={saveMode} | IDX/PAK 백업 생성 완료";
            return;
        }

        throw new InvalidOperationException(
            "이 위치의 SPR은 원본에 직접 저장할 수 없습니다. '선택 추출'로 수정 SPR을 저장해 사용할 수 있습니다.");
    }

    private void ShowImage(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        using var src = Image.FromStream(ms, useEmbeddedColorManagement: true, validateImageData: true);
        var original = new Bitmap(src);

        currentOriginalBitmap?.Dispose();
        currentOriginalBitmap = new Bitmap(original);

        currentModifiedBitmap?.Dispose();
        currentModifiedBitmap = new Bitmap(original);
        currentModifiedImageBytes = (byte[])data.Clone();
        currentModifiedImageFormat = DetectImageFormat(data);

        imagePreview.Image?.Dispose();
        imagePreview.Image = new Bitmap(currentOriginalBitmap);

        imageModifiedPreview.Image?.Dispose();
        imageModifiedPreview.Image = new Bitmap(currentModifiedBitmap);

        string sourceText = currentRow == null ? "" :
            currentRow.Source == "PAK 내부"
                ? $"PAK 내부 | {currentRow.Container}"
                : GetOriginalFilePath(currentRow) ?? currentRow.Source;

        lblImageOriginalInfo.Text =
            $"원본  {original.Width} × {original.Height}px | {DetectImageFormat(data)} | " +
            $"{original.PixelFormat} | {data.LongLength:N0} bytes | " +
            $"DPI {original.HorizontalResolution:0.#}×{original.VerticalResolution:0.#}\r\n" +
            $"위치: {sourceText}";

        lblImageModifiedInfo.Text =
            $"수정본  {original.Width} × {original.Height}px | 아직 원본과 동일";

        imageCompareHost.Visible = true;
        imageCompareHost.BringToFront();
        imageToolBar.Visible = true;
        imageToolBar.BringToFront();

        lblInfo.Text +=
            $" | 원본 이미지 {original.Width}x{original.Height} | {DetectImageFormat(data)} | {original.PixelFormat}";
    }

    private async Task RenderSprFrameAsync()
    {
        if (currentRaw == null || currentRow == null || currentSprDecoded == null) return;
        if (DetectTypeFromData(currentRow.Path, currentRaw) != "SPR") return;
        if (sprRenderBusy) return;

        sprRenderBusy = true;

        try
        {
            int frame = (int)numSprFrame.Value;
            byte[] spr = currentSprDecoded;

            var bmp = await Task.Run(() => SprDecoder.DecodeFrame(spr, frame));

            sprPreview.Image?.Dispose();
            sprPreview.Image = bmp;
        }
        catch (Exception ex)
        {
            StopSprAuto();
            lblSpr.Text = "SPR 표시 실패: " + ex.Message;
        }
        finally
        {
            sprRenderBusy = false;
        }
    }

    private void ToggleSprAuto()
    {
        if (sprAutoTimer.Enabled)
            StopSprAuto();
        else
            StartSprAuto();
    }

    private void StartSprAuto()
    {
        if (currentSprDecoded == null || numSprFrame.Maximum <= 0)
            return;

        sprAutoTimer.Start();
        btnSprAuto.Text = "정지 ■";
    }

    private void StopSprAuto()
    {
        sprAutoTimer.Stop();
        btnSprAuto.Text = "자동 ▶";
    }

    private void AdvanceSprFrame()
    {
        if (!sprBar.Visible || currentSprDecoded == null || sprRenderBusy)
            return;

        decimal next = numSprFrame.Value + 1;
        if (next > numSprFrame.Maximum)
            next = numSprFrame.Minimum;

        numSprFrame.Value = next;
    }

    private async Task SaveSelectedAsync()
    {
        if (currentRaw == null || currentRow == null)
        {
            MessageBox.Show(this, "먼저 파일을 선택하세요.");
            return;
        }

        string folder = GetExtractFolder();
        Directory.CreateDirectory(folder);

        string safeName = Path.GetFileName(currentRow.Path.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = string.IsNullOrWhiteSpace(currentName) ? "selected.bin" : currentName;

        string target = MakeUniquePath(Path.Combine(folder, safeName));
        await File.WriteAllBytesAsync(target, currentRaw);

        status.Text = $"추출 완료: {target}";
        MessageBox.Show(this,
            $"선택한 원본을 추출했습니다.\n\n{target}",
            "선택 추출 완료",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static string GetExtractFolder()
    {
        return Path.Combine(AppContext.BaseDirectory, "Extracted");
    }

    private void OpenExtractFolder()
    {
        string folder = GetExtractFolder();
        Directory.CreateDirectory(folder);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "추출 폴더 열기 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        string dir = Path.GetDirectoryName(path) ?? "";
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);

        for (int i = 1; i < 10000; i++)
        {
            string candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dir, $"{stem}_{DateTime.Now:yyyyMMdd_HHmmssfff}{ext}");
    }

    private async Task LoadModifiedImageAsync()
    {
        if (currentOriginalBitmap == null || currentRow == null)
        {
            MessageBox.Show(this, "먼저 원본 이미지를 선택하세요.");
            return;
        }

        using var dlg = new OpenFileDialog
        {
            Title = "수정 이미지 선택",
            Filter = "이미지 파일|*.png;*.bmp;*.jpg;*.jpeg;*.gif;*.tif;*.tiff;*.ico|모든 파일|*.*"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(dlg.FileName);
            using var ms = new MemoryStream(bytes, writable: false);
            using var img = Image.FromStream(ms, true, true);
            var bmp = new Bitmap(img);

            currentModifiedBitmap?.Dispose();
            currentModifiedBitmap = bmp;
            currentModifiedImageBytes = bytes;
            currentModifiedImageFormat = DetectImageFormat(bytes);

            imageModifiedPreview.Image?.Dispose();
            imageModifiedPreview.Image = new Bitmap(currentModifiedBitmap);

            bool sameSize =
                currentOriginalBitmap.Width == currentModifiedBitmap.Width &&
                currentOriginalBitmap.Height == currentModifiedBitmap.Height;

            lblImageModifiedInfo.Text =
                $"수정본  {bmp.Width} × {bmp.Height}px | {currentModifiedImageFormat} | {bmp.PixelFormat} | " +
                $"{bytes.LongLength:N0} bytes\r\n" +
                $"원본과 크기 {(sameSize ? "일치" : "불일치")} | 파일: {dlg.FileName}";

            status.Text = $"수정 이미지 불러오기 완료: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "수정 이미지 읽기 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyColorTest()
    {
        if (currentOriginalBitmap == null)
        {
            MessageBox.Show(this, "먼저 원본 이미지를 선택하세요.");
            return;
        }

        using var dlg = new ColorDialog
        {
            Color = Color.CornflowerBlue,
            FullOpen = true
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var tinted = new Bitmap(
            currentOriginalBitmap.Width,
            currentOriginalBitmap.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        Color tint = dlg.Color;
        const float blend = 0.42f;

        for (int y = 0; y < currentOriginalBitmap.Height; y++)
        {
            for (int x = 0; x < currentOriginalBitmap.Width; x++)
            {
                Color c = currentOriginalBitmap.GetPixel(x, y);
                if (c.A == 0)
                {
                    tinted.SetPixel(x, y, c);
                    continue;
                }

                int r = (int)Math.Clamp(c.R * (1f - blend) + tint.R * blend, 0, 255);
                int g = (int)Math.Clamp(c.G * (1f - blend) + tint.G * blend, 0, 255);
                int b = (int)Math.Clamp(c.B * (1f - blend) + tint.B * blend, 0, 255);

                tinted.SetPixel(x, y, Color.FromArgb(c.A, r, g, b));
            }
        }

        currentModifiedBitmap?.Dispose();
        currentModifiedBitmap = tinted;

        imageModifiedPreview.Image?.Dispose();
        imageModifiedPreview.Image = new Bitmap(tinted);

        currentModifiedImageBytes = EncodeBitmapForOriginal(tinted, currentModifiedImageFormat);
        currentModifiedImageFormat = DetectImageFormat(currentModifiedImageBytes);

        lblImageModifiedInfo.Text =
            $"수정본  {tinted.Width} × {tinted.Height}px | 색상 테스트 {ColorTranslator.ToHtml(tint)} | " +
            $"{currentModifiedImageFormat} | {currentModifiedImageBytes.LongLength:N0} bytes";

        status.Text = "원본 색상 변경 테스트 적용 - 왼쪽 원본은 유지, 오른쪽 수정본만 변경됨";
    }

    private void ResetModifiedImage()
    {
        if (currentOriginalBitmap == null || currentRaw == null)
            return;

        currentModifiedBitmap?.Dispose();
        currentModifiedBitmap = new Bitmap(currentOriginalBitmap);
        currentModifiedImageBytes = (byte[])currentRaw.Clone();
        currentModifiedImageFormat = DetectImageFormat(currentRaw);

        imageModifiedPreview.Image?.Dispose();
        imageModifiedPreview.Image = new Bitmap(currentModifiedBitmap);

        lblImageModifiedInfo.Text =
            $"수정본  {currentModifiedBitmap.Width} × {currentModifiedBitmap.Height}px | 원본으로 초기화";

        status.Text = "수정본을 원본 상태로 초기화했습니다.";
    }

    private async Task ApplyModifiedImageAsync()
    {
        if (currentRow == null || currentOriginalBitmap == null ||
            currentModifiedBitmap == null || currentModifiedImageBytes == null)
        {
            MessageBox.Show(this, "먼저 원본 이미지와 수정본을 준비하세요.");
            return;
        }

        if (currentOriginalBitmap.Width != currentModifiedBitmap.Width ||
            currentOriginalBitmap.Height != currentModifiedBitmap.Height)
        {
            MessageBox.Show(this,
                "수정 이미지의 가로/세로 크기가 원본과 다릅니다.\n게임 UI/GFX 안전성을 위해 동일 크기만 등록합니다.",
                "수정본 등록 중지",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        string originalFormat = DetectImageFormat(currentRaw ?? Array.Empty<byte>());
        if (originalFormat == "미확인")
        {
            MessageBox.Show(this,
                "선택한 원본은 표준 PNG/BMP/JPEG/GIF/ICO 이미지가 아닙니다.\n" +
                "현재 버전에서는 이 포맷을 안전하게 다시 인코딩할 수 없어 원본 등록을 하지 않습니다.",
                "수정본 등록 불가",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        byte[] replacement = EncodeBitmapForOriginal(currentModifiedBitmap, originalFormat);

        var answer = MessageBox.Show(this,
            $"수정 이미지를 원본에 등록합니다.\n\n" +
            $"대상: {currentRow.Path}\n" +
            $"크기: {currentModifiedBitmap.Width}×{currentModifiedBitmap.Height}\n" +
            $"포맷: {originalFormat}\n\n" +
            "적용 전에 자동 백업합니다. 계속할까요?",
            "현재 수정본 원본에 저장",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        try
        {
            if (currentRow.Source == "실제파일" || currentRow.Source == "서버팩")
            {
                string? path = GetOriginalFilePath(currentRow);
                if (string.IsNullOrWhiteSpace(path))
                    throw new InvalidOperationException("원본 파일 경로를 확인할 수 없습니다.");

                string backup = path + ".image.bak";
                if (!File.Exists(backup))
                    File.Copy(path, backup, false);

                await File.WriteAllBytesAsync(path, replacement);
                currentRaw = replacement;
                currentRow.SizeBytes = replacement.LongLength;

                status.Text = $"수정 이미지 등록 완료: {path} | 백업: {backup}";
            }
            else if (currentRow.Source == "PAK 내부")
            {
                if (!scanners.TryGetValue(currentRow.SourceKey, out var scanner))
                    throw new InvalidOperationException("선택한 PAK 정보를 찾지 못했습니다.");

                bool supportedPak =
                    scanner.Format.Equals("LEGACY28", StringComparison.OrdinalIgnoreCase) ||
                    scanner.Format.Equals("_EXT", StringComparison.OrdinalIgnoreCase);

                if (!supportedPak || scanner.DesEncrypted)
                {
                    MessageBox.Show(this,
                        $"현재 PAK 형식은 {scanner.Format}{(scanner.DesEncrypted ? " / DES" : "")} 입니다.\n" +
                        "V3.2에서는 비암호화 LEGACY28 및 비암호화 _EXT PAK만 직접 재등록합니다.\n" +
                        "수정본은 오른쪽에서 확인하거나 '선택 추출'로 저장할 수 있습니다.",
                        "PAK 직접 등록 제한",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string idxPath = currentRow.SourceKey;
                string pakPath = Path.ChangeExtension(idxPath, ".pak");
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string idxBackup = idxPath + ".imagebak_" + stamp;
                string pakBackup = pakPath + ".imagebak_" + stamp;

                File.Copy(idxPath, idxBackup, false);
                File.Copy(pakPath, pakBackup, false);

                using var pak = new SpritePak(idxPath);
                pak.RebuildPak(new Dictionary<string, byte[]>
                {
                    [currentRow.Path] = replacement
                });

                currentRaw = replacement;
                currentRow.SizeBytes = replacement.LongLength;

                if (scanners.TryGetValue(idxPath, out var oldScanner))
                    oldScanner.Dispose();
                scanners[idxPath] = new AnyPakScanner(idxPath);

                status.Text =
                    $"PAK 수정 이미지 등록 완료: {currentRow.Path} | 백업: {Path.GetFileName(idxBackup)}, {Path.GetFileName(pakBackup)}";
            }
            else
            {
                MessageBox.Show(this,
                    "이 위치의 이미지는 직접 원본 등록 대상이 아닙니다.\n수정본을 추출해서 사용할 수 있습니다.",
                    "현재 수정본 원본에 저장",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            currentModifiedImageBytes = (byte[])replacement.Clone();
            currentModifiedImageFormat = originalFormat;
            lblImageModifiedInfo.Text += "\r\n원본 등록 완료";

            MessageBox.Show(this,
                "현재 수정본을 원본에 저장했습니다.\n자동 백업도 생성했습니다.",
                "원본 저장 완료",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "현재 수정본 원본 저장 실패",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            status.Text = "현재 수정본 원본 저장 실패";
        }
    }

    private static byte[] EncodeBitmapForOriginal(Bitmap bitmap, string format)
    {
        using var ms = new MemoryStream();

        switch (format.ToUpperInvariant())
        {
            case "BMP":
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
                break;
            case "JPEG":
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                break;
            case "GIF":
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Gif);
                break;
            case "ICO":
                // System.Drawing은 Bitmap을 ICO로 안정적으로 재인코딩하지 못하므로 PNG로 보존하지 않고 차단.
                throw new NotSupportedException("ICO 수정본 직접 등록은 현재 지원하지 않습니다.");
            case "PNG":
            default:
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                break;
        }

        return ms.ToArray();
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
        StopSprAuto();
        currentSprDecoded = null;
        sprEditor.ClearSpr();

        if (!editMode)
            textPreview.ReadOnly = true;

        textPreview.Visible = false;
        imageCompareHost.Visible = false;
        sprPreview.Visible = false;
        hexPreview.Visible = false;
        sprBar.Visible = false;

        textPreview.Clear();
        hexPreview.Clear();
        imagePreview.Image?.Dispose();
        imagePreview.Image = null;
        imageModifiedPreview.Image?.Dispose();
        imageModifiedPreview.Image = null;

        currentOriginalBitmap?.Dispose();
        currentOriginalBitmap = null;
        currentModifiedBitmap?.Dispose();
        currentModifiedBitmap = null;
        currentModifiedImageBytes = null;
        currentModifiedImageFormat = "";
        lblImageOriginalInfo.Text = "";
        lblImageModifiedInfo.Text = "";

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
        btnOpenExtract.Enabled = !busy;
        btnLoadModifiedImage.Enabled = !busy;
        btnColorTest.Enabled = !busy;
        btnResetModifiedImage.Enabled = !busy;
        btnApplyModifiedImage.Enabled = !busy;
        btnHash.Enabled = !busy;

        progress.Value = busy ? 10 : 0;

        if (!string.IsNullOrWhiteSpace(message))
            status.Text = message;
    }

    private sealed record DecodedText(string Text, string EncodingName);
    private sealed record EditableEncodingInfo(Encoding Encoding, bool EmitBom, int PreambleLength, string Name);
    private sealed record ProcessResult(int ExitCode);

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
