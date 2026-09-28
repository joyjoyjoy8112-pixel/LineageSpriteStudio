using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LineageSpriteStudio;

internal sealed class PangPatcherForm : Form
{
    private const int TargetGfx = 267;
    private const int TargetNpcId = 460000129;
    private const string SheetFileName = "팡_몬스터_시트.png";
    private const string SqlFileName = "팡_DB등록.sql";

    private readonly TextBox txtClient = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Button btnBrowse = new() { Text = "클라이언트 폴더 선택", AutoSize = true };
    private readonly Button btnScan = new() { Text = "GFX 267 검사", AutoSize = true };
    private readonly Button btnApply = new() { Text = "팡 몬스터 패치 적용", AutoSize = true };
    private readonly Button btnRestore = new() { Text = "최근 팡 패치 원복", AutoSize = true };
    private readonly Button btnSql = new() { Text = "DB 등록 SQL 열기", AutoSize = true };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
    private readonly Label lblState = new() { AutoSize = true, Text = "대기 중", Padding = new Padding(6) };
    private readonly Label lblInfo = new()
    {
        AutoSize = true,
        Text = "몬스터: 팡  |  NPC ID: 460000129  |  GFX: 267  |  HP: 1,000,000  |  MP: 50",
        Padding = new Padding(6)
    };
    private readonly RichTextBox log = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = Color.FromArgb(24, 26, 31),
        ForeColor = Color.Gainsboro,
        Font = new Font("Consolas", 9F)
    };
    private readonly PictureBox preview = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BackColor = Color.FromArgb(18, 20, 24)
    };

    private string? clientRoot;

    public PangPatcherForm()
    {
        Text = "팡 몬스터 패처 V1.0 - GFX 267";
        Width = 1040;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(840, 620);
        Font = new Font("Segoe UI", 9F);

        BuildUi();

        btnBrowse.Click += (_, _) => BrowseClient();
        btnScan.Click += async (_, _) => await ScanAsync();
        btnApply.Click += async (_, _) => await ApplyAsync();
        btnRestore.Click += async (_, _) => await RestoreLatestAsync();
        btnSql.Click += (_, _) => OpenSql();

        Load += (_, _) => LoadSheetPreview();
        FormClosed += (_, _) => preview.Image?.Dispose();
    }

    private string SheetPath => Path.Combine(AppContext.BaseDirectory, SheetFileName);
    private string SqlPath => Path.Combine(AppContext.BaseDirectory, SqlFileName);

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 6
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 57));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var title = new Label
        {
            Text = "팡 몬스터 클라이언트 패치",
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            AutoSize = true,
            Padding = new Padding(4, 2, 4, 8)
        };
        root.Controls.Add(title, 0, 0);

        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(txtClient, 0, 0);
        pathRow.Controls.Add(btnBrowse, 1, 0);
        root.Controls.Add(pathRow, 0, 1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 6)
        };
        actions.Controls.Add(lblInfo);
        actions.Controls.Add(btnScan);
        actions.Controls.Add(btnApply);
        actions.Controls.Add(btnRestore);
        actions.Controls.Add(btnSql);
        root.Controls.Add(actions, 0, 2);

        root.Controls.Add(preview, 0, 3);
        root.Controls.Add(log, 0, 4);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(progress, 0, 0);
        bottom.Controls.Add(lblState, 1, 0);
        root.Controls.Add(bottom, 0, 5);
    }

    private void LoadSheetPreview()
    {
        if (!File.Exists(SheetPath))
        {
            Log($"[오류] {SheetFileName} 파일이 패처 EXE 옆에 없습니다.");
            return;
        }

        using var src = Image.FromFile(SheetPath);
        preview.Image?.Dispose();
        preview.Image = new Bitmap(src);
        Log($"[이미지] {SheetFileName} / {src.Width}x{src.Height}");
    }

    private void BrowseClient()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Sprite00.idx ~ Sprite15.idx가 있는 리니지 클라이언트 폴더를 선택하세요."
        };

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        clientRoot = dlg.SelectedPath;
        txtClient.Text = clientRoot;
        Log($"[클라] {clientRoot}");
    }

    private async Task ScanAsync()
    {
        try
        {
            string root = RequireClientRoot();
            SetBusy(true, "GFX 267 검색 중...", 5);

            var targets = await Task.Run(() => ScanTargets(root));
            var grouped = targets.GroupBy(x => Path.GetFileName(x.IdxPath)).OrderBy(g => g.Key).ToList();

            Log($"[검색] GFX {TargetGfx}: 총 {targets.Count}개 SPR 발견");
            foreach (var g in grouped)
                Log($"[검색] {g.Key}: {g.Count()}개");

            if (targets.Count == 0)
                throw new InvalidDataException("GFX 267의 SPR 파일을 찾지 못했습니다.");

            var directions = targets.Select(x => x.Part % 8).Distinct().OrderBy(x => x).ToArray();
            Log($"[방향] 발견 방향: {string.Join(", ", directions)}");
            Log("[확인] 이 패처는 기존 GFX 267의 애니메이션 프로필을 그대로 사용하고 이미지 프레임만 팡으로 교체합니다.");

            SetBusy(false, $"검사 완료 · {targets.Count}개 SPR", 100);
        }
        catch (Exception ex)
        {
            SetBusy(false, "검사 실패", 0);
            Log("[검사 오류] " + ex.Message);
            MessageBox.Show(this, ex.Message, "검사 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ApplyAsync()
    {
        string? backupDir = null;

        try
        {
            string root = RequireClientRoot();

            if (!File.Exists(SheetPath))
                throw new FileNotFoundException($"패처 EXE 옆에 {SheetFileName}가 없습니다.", SheetPath);

            if (Process.GetProcessesByName("mjlin").Length > 0)
                throw new InvalidOperationException("리니지 클라이언트(mjlin)가 실행 중입니다. 게임을 완전히 종료한 뒤 적용하세요.");

            var confirm = MessageBox.Show(this,
                "팡 몬스터 이미지를 GFX 267에 적용합니다.\n\n" +
                "기존 GFX 267 SPR만 수정하며, 적용 전에 원본 IDX와 해당 SPR 바이트를 백업합니다.\n" +
                "DB 등록은 패치 폴더의 팡_DB등록.sql을 Navicat에서 실행해야 합니다.\n\n진행할까요?",
                "팡 몬스터 패치",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);

            if (confirm != DialogResult.OK)
                return;

            SetBusy(true, "대상 검색 중...", 2);
            var targets = await Task.Run(() => ScanTargets(root));
            if (targets.Count == 0)
                throw new InvalidDataException("GFX 267의 SPR을 찾지 못했습니다.");

            Log($"[적용] 대상 SPR {targets.Count}개");

            SetBusy(true, "원본 백업 중...", 5);
            backupDir = await Task.Run(() => CreateCompactBackup(root, targets));
            Log($"[백업] {backupDir}");

            using var sheet = new Bitmap(SheetPath);
            string tempRoot = Path.Combine(Path.GetTempPath(), "PangMonsterPatch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                int done = 0;
                int total = targets.Count;
                var modeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var group in targets.GroupBy(x => x.IdxPath, StringComparer.OrdinalIgnoreCase)
                                             .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                {
                    using var pak = new SpritePak(group.Key);

                    if (pak.IsDesEncrypted)
                        throw new InvalidOperationException($"{Path.GetFileName(group.Key)}가 DES 암호화 형식이라 자동 적용을 중단합니다.");

                    foreach (var target in group.OrderBy(x => x.Part))
                    {
                        var entry = pak.Entries.FirstOrDefault(e =>
                            e.FileName.Equals(target.EntryName, StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidDataException($"원본 엔트리를 찾지 못했습니다: {target.EntryName}");

                        byte[] originalStoredSpr = pak.Extract(entry);
                        bool sprZlib = SpriteCodec.IsZlib(originalStoredSpr);
                        byte[] originalRawSpr = SpriteCodec.DecodeIfNeeded(originalStoredSpr);
                        var format = SprInfo.Analyze(originalRawSpr);

                        if (format.FrameCount <= 0 || format.FrameCount > 254)
                            throw new InvalidDataException($"SPR 프레임 수가 잘못되었습니다: {target.EntryName} / {format.FrameCount}");

                        using var originalFrame = SprDecoder.DecodeFrame(originalRawSpr, 0);
                        int width = Math.Clamp(originalFrame.Width, 24, 512);
                        int height = Math.Clamp(originalFrame.Height, 24, 512);

                        string partDir = Path.Combine(tempRoot, $"part_{target.Part:D3}");
                        Directory.CreateDirectory(partDir);

                        var pngFiles = BuildFrames(sheet, target.Part, format.FrameCount, width, height, partDir);
                        byte[] newRawSpr = SprEncoder.CreateFromPngs(pngFiles, format.IsPalette, format.FrameType);

                        var checkFormat = SprInfo.Analyze(newRawSpr);
                        if (checkFormat.FrameCount != format.FrameCount)
                            throw new InvalidDataException($"새 SPR 프레임 검증 실패: {target.EntryName}");

                        using (var test = SprDecoder.DecodeFrame(newRawSpr, 0)) { }
                        if (format.FrameCount > 1)
                            using (var testLast = SprDecoder.DecodeFrame(newRawSpr, format.FrameCount - 1)) { }

                        byte[] newStoredSpr = sprZlib ? SpriteCodec.EncodeZlib(newRawSpr) : newRawSpr;
                        string mode = pak.ReplaceEntryMinimal(target.EntryName, newStoredSpr);

                        modeCounts.TryGetValue(mode, out int n);
                        modeCounts[mode] = n + 1;

                        done++;
                        int pct = 10 + (int)(80.0 * done / Math.Max(1, total));
                        SetBusy(true, $"SPR 적용 중 {done}/{total} · {target.EntryName}", pct);
                        Log($"[적용] {target.EntryName} / frames={format.FrameCount} / dir={target.Part % 8} / {mode}");
                    }
                }

                SetBusy(true, "적용 결과 재검증 중...", 92);
                int verified = await Task.Run(() => VerifyTargets(root, targets));

                Log($"[검증] {verified}/{targets.Count} SPR 정상 디코딩");
                foreach (var kv in modeCounts.OrderBy(x => x.Key))
                    Log($"[저장방식] {kv.Key}: {kv.Value}개");

                SetBusy(false, $"완료 · {verified}개 SPR 검증", 100);

                MessageBox.Show(this,
                    $"팡 몬스터 클라이언트 패치 완료\n\n" +
                    $"GFX: {TargetGfx}\nSPR: {verified}/{targets.Count}\n" +
                    $"백업: {backupDir}\n\n" +
                    $"이제 Navicat에서 {SqlFileName}을 실행한 뒤 서버를 재시작하세요.",
                    "팡 패치 완료",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            finally
            {
                try { Directory.Delete(tempRoot, true); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log("[적용 오류] " + ex);

            if (!string.IsNullOrWhiteSpace(backupDir) && Directory.Exists(backupDir))
            {
                try
                {
                    Log("[복구] 적용 실패로 자동 원복을 시작합니다.");
                    await Task.Run(() => RestoreBackup(backupDir));
                    Log("[복구] 원본 IDX/PAK 상태로 자동 원복했습니다.");
                }
                catch (Exception restoreEx)
                {
                    Log("[복구 오류] " + restoreEx);
                }
            }

            SetBusy(false, "적용 실패", 0);
            MessageBox.Show(this,
                ex.Message + "\n\n백업이 생성된 뒤 실패했다면 자동 원복을 시도했습니다.",
                "팡 패치 실패",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task RestoreLatestAsync()
    {
        try
        {
            string root = RequireClientRoot();

            if (Process.GetProcessesByName("mjlin").Length > 0)
                throw new InvalidOperationException("리니지 클라이언트를 완전히 종료한 뒤 원복하세요.");

            string? backup = Directory.EnumerateDirectories(root, "_PANG_BACKUP_*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (backup == null)
                throw new DirectoryNotFoundException("팡 패치 백업 폴더를 찾지 못했습니다.");

            if (MessageBox.Show(this,
                    $"최근 백업으로 원복합니다.\n\n{backup}\n\n진행할까요?",
                    "팡 패치 원복",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            SetBusy(true, "원본 복원 중...", 20);
            await Task.Run(() => RestoreBackup(backup));
            SetBusy(false, "원복 완료", 100);
            Log($"[원복] 완료: {backup}");

            MessageBox.Show(this, "팡 패치 적용 전 상태로 원복했습니다.", "원복 완료",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetBusy(false, "원복 실패", 0);
            Log("[원복 오류] " + ex);
            MessageBox.Show(this, ex.Message, "원복 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenSql()
    {
        if (!File.Exists(SqlPath))
        {
            MessageBox.Show(this, $"{SqlFileName} 파일이 없습니다.", "DB SQL",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{SqlPath}\"",
            UseShellExecute = true
        });
    }

    private string RequireClientRoot()
    {
        if (string.IsNullOrWhiteSpace(clientRoot) || !Directory.Exists(clientRoot))
            throw new DirectoryNotFoundException("먼저 리니지 클라이언트 폴더를 선택하세요.");

        if (!Directory.EnumerateFiles(clientRoot, "Sprite*.idx", SearchOption.TopDirectoryOnly).Any())
            throw new DirectoryNotFoundException("선택한 폴더에서 Sprite*.idx를 찾지 못했습니다.");

        return clientRoot;
    }

    private static List<TargetSpr> ScanTargets(string root)
    {
        var list = new List<TargetSpr>();
        var rx = new Regex($"^{TargetGfx}-(\\d+)\\.spr$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (string idx in Directory.EnumerateFiles(root, "Sprite*.idx", SearchOption.TopDirectoryOnly)
                                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var pak = new SpritePak(idx);
                foreach (var e in pak.Entries)
                {
                    var m = rx.Match(Path.GetFileName(e.FileName));
                    if (!m.Success)
                        continue;

                    list.Add(new TargetSpr(idx, e.FileName, int.Parse(m.Groups[1].Value)));
                }
            }
            catch
            {
                // Sprite 형식이 아닌 다른 idx는 건너뛴다.
            }
        }

        return list
            .OrderBy(x => x.Part)
            .ThenBy(x => x.IdxPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildFrames(
        Bitmap sheet,
        int part,
        int frameCount,
        int targetWidth,
        int targetHeight,
        string outputDir)
    {
        if (sheet.Width < 4 || sheet.Height < 4)
            throw new InvalidDataException("팡 시트 이미지 크기가 잘못되었습니다.");

        int cellW = sheet.Width / 4;
        int cellH = sheet.Height / 4;
        int dir = ((part % 8) + 8) % 8;
        var map = MapDirection(dir);

        var files = new List<string>(frameCount);

        for (int i = 0; i < frameCount; i++)
        {
            int col = i % 4;
            var srcRect = new Rectangle(col * cellW, map.Row * cellH, cellW, cellH);

            using var cell = sheet.Clone(srcRect, PixelFormat.Format32bppArgb);
            RemoveWhiteBackground(cell);

            Rectangle bounds = FindOpaqueBounds(cell);
            using var subject = bounds.Width > 0 && bounds.Height > 0
                ? cell.Clone(bounds, PixelFormat.Format32bppArgb)
                : new Bitmap(cell);

            if (map.Mirror)
                subject.RotateFlip(RotateFlipType.RotateNoneFlipX);

            using var canvas = FitToCanvas(subject, targetWidth, targetHeight);
            string path = Path.Combine(outputDir, $"frame_{i:D3}.png");
            canvas.Save(path, ImageFormat.Png);
            files.Add(path);
        }

        return files;
    }

    private static (int Row, bool Mirror) MapDirection(int dir)
    {
        // 시트: 0=정면, 1=좌측, 2=우측, 3=후면.
        // 리니지 8방향에 맞춰 대각선은 가장 가까운 앞/뒤 방향을 재사용한다.
        return dir switch
        {
            0 => (3, false), // 북서 근사
            1 => (3, false), // 북
            2 => (3, true),  // 북동 근사
            3 => (2, false), // 동/우측
            4 => (0, true),  // 남동 근사
            5 => (0, false), // 남/정면
            6 => (0, false), // 남서 근사
            7 => (1, false), // 서/좌측
            _ => (0, false)
        };
    }

    private static void RemoveWhiteBackground(Bitmap bmp)
    {
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            Color c = bmp.GetPixel(x, y);
            int min = Math.Min(c.R, Math.Min(c.G, c.B));

            if (min >= 238)
            {
                bmp.SetPixel(x, y, Color.FromArgb(0, c.R, c.G, c.B));
                continue;
            }

            if (min >= 220 && Math.Max(c.R, Math.Max(c.G, c.B)) - min < 12)
            {
                int alpha = Math.Clamp((238 - min) * 14, 0, 255);
                bmp.SetPixel(x, y, Color.FromArgb(alpha, c.R, c.G, c.B));
            }
        }
    }

    private static Rectangle FindOpaqueBounds(Bitmap bmp)
    {
        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;

        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            if (bmp.GetPixel(x, y).A < 24)
                continue;

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        if (maxX < minX || maxY < minY)
            return Rectangle.Empty;

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static Bitmap FitToCanvas(Bitmap subject, int width, int height)
    {
        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        output.SetResolution(96, 96);

        double sx = width * 0.92 / Math.Max(1, subject.Width);
        double sy = height * 0.96 / Math.Max(1, subject.Height);
        double scale = Math.Min(sx, sy);

        int drawW = Math.Max(1, (int)Math.Round(subject.Width * scale));
        int drawH = Math.Max(1, (int)Math.Round(subject.Height * scale));
        int x = (width - drawW) / 2;
        int y = Math.Max(0, height - drawH);

        using var g = Graphics.FromImage(output);
        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.DrawImage(subject, new Rectangle(x, y, drawW, drawH));

        return output;
    }

    private static string CreateCompactBackup(string root, IReadOnlyList<TargetSpr> targets)
    {
        string backupDir = Path.Combine(root, "_PANG_BACKUP_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        string entriesDir = Path.Combine(backupDir, "entries");
        Directory.CreateDirectory(entriesDir);

        var manifest = new BackupManifest
        {
            ClientRoot = root,
            CreatedAt = DateTime.Now,
            GfxId = TargetGfx
        };

        foreach (var group in targets.GroupBy(x => x.IdxPath, StringComparer.OrdinalIgnoreCase))
        {
            using var pak = new SpritePak(group.Key);
            string idxName = Path.GetFileName(group.Key);
            string pakName = Path.GetFileName(pak.PakPath);

            File.Copy(group.Key, Path.Combine(backupDir, idxName), true);

            var bp = new BackupPak
            {
                IdxName = idxName,
                PakName = pakName,
                OriginalPakLength = new FileInfo(pak.PakPath).Length
            };

            foreach (var target in group)
            {
                var e = pak.Entries.First(x =>
                    x.FileName.Equals(target.EntryName, StringComparison.OrdinalIgnoreCase));

                byte[] stored = pak.ReadStoredBytes(e);
                string safe = Regex.Replace(target.EntryName, @"[^0-9A-Za-z._-]", "_");
                string backupName = idxName + "__" + safe + ".bin";
                File.WriteAllBytes(Path.Combine(entriesDir, backupName), stored);

                bp.Entries.Add(new BackupEntry
                {
                    FileName = e.FileName,
                    Offset = e.Offset,
                    FileSize = e.FileSize,
                    CompressedSize = e.CompressedSize,
                    Flags = e.Flags,
                    StoredFile = backupName
                });
            }

            manifest.Paks.Add(bp);
        }

        File.WriteAllText(
            Path.Combine(backupDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        File.WriteAllText(
            Path.Combine(backupDir, "README_원복.txt"),
            "팡 몬스터 패치 원본 백업입니다.\r\n" +
            "PangMonsterPatcher.exe의 '최근 팡 패치 원복' 버튼으로 복구하세요.\r\n" +
            "IDX 원본과 수정 대상 SPR의 원래 PAK 저장 바이트, 원래 PAK 길이를 보관합니다.\r\n");

        return backupDir;
    }

    private static void RestoreBackup(string backupDir)
    {
        string manifestPath = Path.Combine(backupDir, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("백업 manifest.json을 찾지 못했습니다.", manifestPath);

        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("백업 manifest를 읽지 못했습니다.");

        string entriesDir = Path.Combine(backupDir, "entries");

        foreach (var bp in manifest.Paks)
        {
            string idxPath = Path.Combine(manifest.ClientRoot, bp.IdxName);
            string pakPath = Path.Combine(manifest.ClientRoot, bp.PakName);

            if (!File.Exists(pakPath))
                throw new FileNotFoundException("복원할 PAK이 없습니다.", pakPath);

            using (var fs = new FileStream(pakPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                foreach (var be in bp.Entries)
                {
                    string storedPath = Path.Combine(entriesDir, be.StoredFile);
                    byte[] bytes = File.ReadAllBytes(storedPath);
                    fs.Position = be.Offset;
                    fs.Write(bytes, 0, bytes.Length);
                }

                fs.SetLength(bp.OriginalPakLength);
                fs.Flush(true);
            }

            string backupIdx = Path.Combine(backupDir, bp.IdxName);
            File.Copy(backupIdx, idxPath, true);
        }
    }

    private static int VerifyTargets(string root, IReadOnlyList<TargetSpr> originalTargets)
    {
        var targets = ScanTargets(root);
        int verified = 0;

        foreach (var group in targets.GroupBy(x => x.IdxPath, StringComparer.OrdinalIgnoreCase))
        {
            using var pak = new SpritePak(group.Key);
            foreach (var target in group)
            {
                var e = pak.Entries.First(x =>
                    x.FileName.Equals(target.EntryName, StringComparison.OrdinalIgnoreCase));

                byte[] stored = pak.Extract(e);
                byte[] raw = SpriteCodec.DecodeIfNeeded(stored);
                var info = SprInfo.Analyze(raw);
                if (info.FrameCount <= 0)
                    throw new InvalidDataException($"검증 실패 - 프레임 없음: {target.EntryName}");

                using var bmp = SprDecoder.DecodeFrame(raw, 0);
                if (bmp.Width <= 0 || bmp.Height <= 0)
                    throw new InvalidDataException($"검증 실패 - 이미지 크기: {target.EntryName}");

                verified++;
            }
        }

        if (verified != originalTargets.Count)
            throw new InvalidDataException($"SPR 개수가 달라졌습니다. 적용 전 {originalTargets.Count}, 적용 후 {verified}");

        return verified;
    }

    private void SetBusy(bool busy, string text, int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetBusy(busy, text, value));
            return;
        }

        btnBrowse.Enabled = !busy;
        btnScan.Enabled = !busy;
        btnApply.Enabled = !busy;
        btnRestore.Enabled = !busy;
        btnSql.Enabled = !busy;
        progress.Value = Math.Clamp(value, 0, 100);
        lblState.Text = text;
        UseWaitCursor = busy;
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }

        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    private sealed record TargetSpr(string IdxPath, string EntryName, int Part);

    private sealed class BackupManifest
    {
        public string ClientRoot { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public int GfxId { get; set; }
        public List<BackupPak> Paks { get; set; } = new();
    }

    private sealed class BackupPak
    {
        public string IdxName { get; set; } = "";
        public string PakName { get; set; } = "";
        public long OriginalPakLength { get; set; }
        public List<BackupEntry> Entries { get; set; } = new();
    }

    private sealed class BackupEntry
    {
        public string FileName { get; set; } = "";
        public long Offset { get; set; }
        public int FileSize { get; set; }
        public int CompressedSize { get; set; }
        public int Flags { get; set; }
        public string StoredFile { get; set; } = "";
    }
}
