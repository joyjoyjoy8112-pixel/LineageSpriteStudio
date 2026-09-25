using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AutoHuntSettingsUI;

public sealed class MainForm : Form
{
    private readonly Color Bg = Color.FromArgb(24, 18, 12);
    private readonly Color PanelBg = Color.FromArgb(34, 26, 18);
    private readonly Color PanelBg2 = Color.FromArgb(42, 31, 20);
    private readonly Color Gold = Color.FromArgb(198, 156, 73);
    private readonly Color Gold2 = Color.FromArgb(238, 205, 132);
    private readonly Color TextMain = Color.FromArgb(235, 226, 205);
    private readonly Color TextDim = Color.FromArgb(178, 161, 130);
    private readonly Color Green = Color.FromArgb(88, 142, 34);
    private readonly Color Red = Color.FromArgb(141, 48, 30);

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private const string ApiUrl = "http://127.0.0.1:18081/autohunt/api";

    private readonly ComboBox characterBox = new();
    private readonly ComboBox mapBox = new();
    private readonly ComboBox potionBox = new();
    private readonly NumericUpDown potionHp = new();
    private readonly NumericUpDown returnHp = new();
    private readonly NumericUpDown resumeHp = new();
    private readonly CheckBox autoReturn = new();
    private readonly CheckBox autoShop = new();
    private readonly CheckBox attackSkill = new();
    private readonly CheckBox buffSkill = new();
    private readonly CheckBox defenseSkill = new();
    private readonly ComboBox attackSkillBox = new();
    private readonly ComboBox buffSkillBox = new();
    private readonly ComboBox defenseSkillBox = new();
    private readonly Label statusValue = new();
    private readonly Label messageLabel = new();

    private readonly Panel content = new();
    private readonly List<Button> tabButtons = new();
    private bool loadingCharacter;

    private static readonly (int Id, string Name)[] Maps =
    {
        (53,"기란 감옥"), (9,"말하는 섬"),
        (30,"용의 계곡 던전 1층"), (31,"용의 계곡 던전 2층"), (32,"용의 계곡 던전 3층"),
        (25,"사막 던전 1층"), (26,"사막 던전 2층"), (27,"사막 던전 3층"),
        (19,"용던 1층"), (20,"용던 2층"),
        (59,"수중 던전 1층"), (60,"수중 던전 2층"), (61,"수중 던전 3층"),
        (101,"오만의 탑 1층"), (102,"오만의 탑 2층"), (103,"오만의 탑 3층"), (104,"오만의 탑 4층"),
        (105,"오만의 탑 5층"), (106,"오만의 탑 6층"), (107,"오만의 탑 7층"), (108,"오만의 탑 8층"),
        (109,"오만의 탑 9층"), (110,"오만의 탑 10층")
    };

    private static readonly (int Id, string Name)[] Potions =
    {
        (40019,"농축 체력 회복제"), (40020,"농축 고급 체력 회복제"), (40021,"농축 강력 체력 회복제"),
        (40010,"체력 회복제"), (40011,"고급 체력 회복제"), (40012,"강력 체력 회복제"),
        (40022,"신속 체력 회복제"), (40023,"신속 고급 체력 회복제"), (40024,"신속 강력 체력 회복제")
    };

    public MainForm()
    {
        Text = "자동사냥 설정";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(620, 420);
        MinimumSize = MaximumSize = Size;
        BackColor = Bg;
        ForeColor = TextMain;
        TopMost = true;
        DoubleBuffered = true;
        Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildUi();
        PopulateStaticData();
        Shown += async (_, _) =>
        {
            CenterOnGame();
            await LoadStateAsync();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var p1 = new Pen(Color.FromArgb(90, 70, 45), 2);
        using var p2 = new Pen(Gold, 1);
        e.Graphics.DrawRectangle(p1, 1, 1, Width - 3, Height - 3);
        e.Graphics.DrawRectangle(p2, 5, 5, Width - 11, Height - 11);
    }

    private void BuildUi()
    {
        var title = new Panel { Left = 8, Top = 8, Width = 604, Height = 38, BackColor = Color.FromArgb(28, 20, 14) };
        title.MouseDown += DragMouseDown;
        Controls.Add(title);

        var titleLabel = new Label
        {
            Text = "자동사냥 설정", AutoSize = false, Left = 145, Top = 5, Width = 310, Height = 28,
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Gold2,
            Font = new Font("Malgun Gothic", 16F, FontStyle.Bold)
        };
        titleLabel.MouseDown += DragMouseDown;
        title.Controls.Add(titleLabel);

        characterBox.Left = 12; characterBox.Top = 8; characterBox.Width = 128; characterBox.Height = 24;
        StyleCombo(characterBox);
        characterBox.SelectedIndexChanged += async (_, _) =>
        {
            if (!loadingCharacter) await LoadStateAsync(characterBox.Text);
        };
        title.Controls.Add(characterBox);

        var close = MakeButton("×", 565, 6, 30, 27, Red);
        close.Font = new Font("Arial", 13, FontStyle.Bold);
        close.Click += (_, _) => Close();
        title.Controls.Add(close);

        string[] tabs = { "기본 설정", "사냥터 설정", "물약 설정", "스킬 설정" };
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            var b = MakeButton(tabs[i], 18 + i * 146, 51, 140, 31, PanelBg2);
            b.ForeColor = i == 0 ? Gold2 : TextMain;
            b.Click += (_, _) => SelectTab(index);
            Controls.Add(b);
            tabButtons.Add(b);
        }

        content.Left = 18; content.Top = 88; content.Width = 584; content.Height = 270;
        content.BackColor = PanelBg;
        Controls.Add(content);

        BuildBasicPage();

        var defaults = MakeButton("기본값", 18, 368, 120, 37, Color.FromArgb(79, 45, 28));
        defaults.Click += (_, _) => ApplyDefaults();
        Controls.Add(defaults);

        var save = MakeButton("저장", 148, 368, 120, 37, Color.FromArgb(89, 52, 31));
        save.Click += async (_, _) => await SendAsync("save");
        Controls.Add(save);

        var start = MakeButton("자동사냥 시작", 278, 368, 190, 37, Color.FromArgb(48, 105, 24));
        start.Click += async (_, _) => await SendAsync("start");
        Controls.Add(start);

        var stop = MakeButton("중지", 478, 368, 124, 37, Color.FromArgb(120, 42, 27));
        stop.Click += async (_, _) => await SendAsync("stop");
        Controls.Add(stop);

        messageLabel.Left = 20; messageLabel.Top = 345; messageLabel.Width = 570; messageLabel.Height = 20;
        messageLabel.ForeColor = TextDim;
        messageLabel.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(messageLabel);
    }

    private void BuildBasicPage()
    {
        content.Controls.Clear();

        var left = MakeGroup("기본 설정", 10, 10, 338, 250);
        var right = MakeGroup("자동사냥 상태 / 빠른 기능", 356, 10, 218, 250);
        content.Controls.Add(left);
        content.Controls.Add(right);

        AddFieldLabel(left, "사냥터", 14, 38);
        mapBox.SetBounds(122, 34, 198, 26); StyleCombo(mapBox); left.Controls.Add(mapBox);

        AddFieldLabel(left, "사용할 물약", 14, 72);
        potionBox.SetBounds(122, 68, 198, 26); StyleCombo(potionBox); left.Controls.Add(potionBox);

        AddFieldLabel(left, "물약 사용 HP", 14, 106);
        StyleNumber(potionHp, 122, 102, 72, 26, 5, 100);
        left.Controls.Add(potionHp); AddSuffix(left, "% 이하", 202, 106);

        AddFieldLabel(left, "귀환 HP", 14, 140);
        StyleNumber(returnHp, 122, 136, 72, 26, 1, 95);
        left.Controls.Add(returnHp); AddSuffix(left, "% 이하", 202, 140);

        AddFieldLabel(left, "사냥터 복귀 HP", 14, 174);
        StyleNumber(resumeHp, 122, 170, 72, 26, 5, 100);
        left.Controls.Add(resumeHp); AddSuffix(left, "% 이상", 202, 174);

        StyleCheck(autoReturn, "자동 귀환", 14, 210); left.Controls.Add(autoReturn);
        StyleCheck(autoShop, "자동 상점", 168, 210); left.Controls.Add(autoShop);

        statusValue.Text = "현재 상태 : 연결 대기";
        statusValue.SetBounds(14, 34, 190, 28);
        statusValue.ForeColor = Gold2;
        statusValue.Font = new Font(Font, FontStyle.Bold);
        right.Controls.Add(statusValue);

        StyleCheck(attackSkill, "공격 스킬 사용", 14, 70); right.Controls.Add(attackSkill);
        StyleCheck(buffSkill, "버프 스킬 사용", 14, 96); right.Controls.Add(buffSkill);
        StyleCheck(defenseSkill, "방어 스킬 사용", 14, 122); right.Controls.Add(defenseSkill);

        AddFieldLabel(right, "공격 스킬", 14, 155);
        attackSkillBox.SetBounds(89, 151, 115, 25); StyleCombo(attackSkillBox); right.Controls.Add(attackSkillBox);
        AddFieldLabel(right, "버프 스킬", 14, 184);
        buffSkillBox.SetBounds(89, 180, 115, 25); StyleCombo(buffSkillBox); right.Controls.Add(buffSkillBox);
        AddFieldLabel(right, "방어 스킬", 14, 213);
        defenseSkillBox.SetBounds(89, 209, 115, 25); StyleCombo(defenseSkillBox); right.Controls.Add(defenseSkillBox);
    }

    private Panel MakeGroup(string title, int x, int y, int w, int h)
    {
        var p = new Panel { Left = x, Top = y, Width = w, Height = h, BackColor = Color.FromArgb(29, 22, 16) };
        p.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(90, 70, 44));
            e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
        };
        var l = new Label
        {
            Text = title, Left = 10, Top = 7, Width = w - 20, Height = 22,
            ForeColor = Gold2, Font = new Font(Font, FontStyle.Bold)
        };
        p.Controls.Add(l);
        return p;
    }

    private void SelectTab(int index)
    {
        for (int i = 0; i < tabButtons.Count; i++)
        {
            tabButtons[i].ForeColor = i == index ? Gold2 : TextMain;
            tabButtons[i].BackColor = i == index ? Color.FromArgb(73, 54, 27) : PanelBg2;
        }
        BuildBasicPage();
        if (index == 1)
        {
            foreach (Control c in content.Controls) c.Visible = c == content.Controls[0];
            messageLabel.Text = "사냥터 설정: 사냥터/귀환/복귀 조건을 조정합니다.";
        }
        else if (index == 2)
        {
            foreach (Control c in content.Controls) c.Visible = c == content.Controls[0];
            messageLabel.Text = "물약 설정: 물약 이름과 HP 사용 기준을 조정합니다.";
        }
        else if (index == 3)
        {
            foreach (Control c in content.Controls) c.Visible = c == content.Controls[1];
            messageLabel.Text = "스킬 설정: 현재 서버 API는 사용 여부를 저장합니다.";
        }
        else messageLabel.Text = "";
    }

    private void PopulateStaticData()
    {
        mapBox.Items.Clear();
        foreach (var x in Maps) mapBox.Items.Add(new NamedId(x.Id, x.Name));

        potionBox.Items.Clear();
        foreach (var x in Potions) potionBox.Items.Add(new NamedId(x.Id, x.Name));

        foreach (var b in new[] { attackSkillBox, buffSkillBox, defenseSkillBox })
        {
            b.Items.Clear();
            b.Items.Add("자동 선택");
            b.SelectedIndex = 0;
        }
        ApplyDefaults();
    }

    private void ApplyDefaults()
    {
        SelectById(mapBox, 53);
        SelectById(potionBox, 40019);
        potionHp.Value = 50;
        returnHp.Value = 20;
        resumeHp.Value = 90;
        autoReturn.Checked = true;
        autoShop.Checked = true;
        attackSkill.Checked = true;
        buffSkill.Checked = true;
        defenseSkill.Checked = true;
        messageLabel.Text = "기본값을 불러왔습니다.";
    }

    private async Task LoadStateAsync(string? character = null)
    {
        try
        {
            string url = ApiUrl;
            if (!string.IsNullOrWhiteSpace(character))
                url += "?character=" + Uri.EscapeDataString(character);

            using var res = await http.GetAsync(url);
            res.EnsureSuccessStatusCode();
            string json = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            loadingCharacter = true;
            try
            {
                characterBox.Items.Clear();
                if (root.TryGetProperty("characters", out var chars) && chars.ValueKind == JsonValueKind.Array)
                    foreach (var c in chars.EnumerateArray())
                        if (c.ValueKind == JsonValueKind.String) characterBox.Items.Add(c.GetString()!);

                string current = GetString(root, "character", "");
                if (!string.IsNullOrEmpty(current))
                {
                    int idx = characterBox.Items.IndexOf(current);
                    if (idx >= 0) characterBox.SelectedIndex = idx;
                    else characterBox.Text = current;
                }
                else if (characterBox.Items.Count > 0) characterBox.SelectedIndex = 0;
            }
            finally { loadingCharacter = false; }

            SelectById(mapBox, GetInt(root, "mapId", 53));
            SelectById(potionBox, GetInt(root, "potionId", 40019));
            potionHp.Value = Clamp(GetInt(root, "potionPercent", 50), 5, 100);
            returnHp.Value = Clamp(GetInt(root, "returnPercent", 20), 1, 95);
            resumeHp.Value = Clamp(GetInt(root, "resumePercent", 90), 5, 100);
            autoReturn.Checked = GetBool(root, "autoReturn", true);
            autoShop.Checked = GetBool(root, "autoShop", true);
            attackSkill.Checked = GetBool(root, "attackSkill", true);
            buffSkill.Checked = GetBool(root, "buffSkill", true);
            defenseSkill.Checked = GetBool(root, "defenseSkill", true);

            bool running = GetBool(root, "running", false);
            statusValue.Text = running ? "현재 상태 : 자동사냥 중" : "현재 상태 : 대기 중";
            statusValue.ForeColor = running ? Color.FromArgb(166, 220, 91) : Gold2;
            messageLabel.Text = string.IsNullOrEmpty(GetString(root, "character", "")) ? "접속 중인 캐릭터를 기다리는 중입니다." : "설정을 불러왔습니다.";
        }
        catch (Exception ex)
        {
            statusValue.Text = "현재 상태 : 서버 연결 대기";
            statusValue.ForeColor = Color.FromArgb(220, 170, 90);
            messageLabel.Text = "서버 연결 실패: " + ex.Message;
        }
    }

    private async Task SendAsync(string action)
    {
        try
        {
            string character = characterBox.Text.Trim();
            var map = mapBox.SelectedItem as NamedId ?? new NamedId(53, "기란 감옥");
            var potion = potionBox.SelectedItem as NamedId ?? new NamedId(40019, "농축 체력 회복제");

            var data = new Dictionary<string, string>
            {
                ["character"] = character,
                ["action"] = action,
                ["mapId"] = map.Id.ToString(),
                ["potionId"] = potion.Id.ToString(),
                ["potionPercent"] = ((int)potionHp.Value).ToString(),
                ["returnPercent"] = ((int)returnHp.Value).ToString(),
                ["resumePercent"] = ((int)resumeHp.Value).ToString(),
                ["autoReturn"] = autoReturn.Checked ? "1" : "0",
                ["autoShop"] = autoShop.Checked ? "1" : "0",
                ["attackSkill"] = attackSkill.Checked ? "1" : "0",
                ["buffSkill"] = buffSkill.Checked ? "1" : "0",
                ["defenseSkill"] = defenseSkill.Checked ? "1" : "0"
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new FormUrlEncodedContent(data)
            };
            req.Headers.TryAddWithoutValidation("X-AutoHunt-Client", "1");
            using var res = await http.SendAsync(req);
            res.EnsureSuccessStatusCode();
            string json = await res.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            string error = GetString(doc.RootElement, "error", "");
            messageLabel.Text = !string.IsNullOrEmpty(error)
                ? error
                : action switch
                {
                    "save" => "설정을 저장했습니다.",
                    "start" => "자동사냥 시작 명령을 보냈습니다.",
                    "stop" => "자동사냥 중지 명령을 보냈습니다.",
                    _ => "완료했습니다."
                };

            await LoadStateAsync(character);
        }
        catch (Exception ex)
        {
            messageLabel.Text = "요청 실패: " + ex.Message;
        }
    }

    private Button MakeButton(string text, int x, int y, int w, int h, Color bg)
    {
        var b = new Button
        {
            Text = text, Left = x, Top = y, Width = w, Height = h,
            FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = TextMain,
            Cursor = Cursors.Hand, TabStop = false
        };
        b.FlatAppearance.BorderColor = Gold;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(
            Math.Min(255, bg.R + 14), Math.Min(255, bg.G + 14), Math.Min(255, bg.B + 14));
        return b;
    }

    private void AddFieldLabel(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text, Left = x, Top = y, Width = 105, Height = 22,
            ForeColor = TextMain, TextAlign = ContentAlignment.MiddleLeft
        });
    }

    private void AddSuffix(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label
        {
            Text = text, Left = x, Top = y, Width = 65, Height = 22,
            ForeColor = TextDim, TextAlign = ContentAlignment.MiddleLeft
        });
    }

    private void StyleCombo(ComboBox box)
    {
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.BackColor = Color.FromArgb(48, 37, 25);
        box.ForeColor = TextMain;
        box.FlatStyle = FlatStyle.Flat;
    }

    private void StyleNumber(NumericUpDown n, int x, int y, int w, int h, int min, int max)
    {
        n.SetBounds(x, y, w, h);
        n.Minimum = min; n.Maximum = max;
        n.BackColor = Color.FromArgb(48, 37, 25);
        n.ForeColor = TextMain;
        n.BorderStyle = BorderStyle.FixedSingle;
        n.TextAlign = HorizontalAlignment.Center;
    }

    private void StyleCheck(CheckBox c, string text, int x, int y)
    {
        c.Text = text; c.Left = x; c.Top = y; c.Width = 145; c.Height = 23;
        c.ForeColor = TextMain; c.FlatStyle = FlatStyle.Flat; c.Cursor = Cursors.Hand;
    }

    private static decimal Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

    private static void SelectById(ComboBox box, int id)
    {
        for (int i = 0; i < box.Items.Count; i++)
            if (box.Items[i] is NamedId n && n.Id == id)
            {
                box.SelectedIndex = i;
                return;
            }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private static string GetString(JsonElement root, string name, string fallback)
        => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? fallback : fallback;

    private static int GetInt(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out var p)) return fallback;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int v)) return v;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out v)) return v;
        return fallback;
    }

    private static bool GetBool(JsonElement root, string name, bool fallback)
    {
        if (!root.TryGetProperty(name, out var p)) return fallback;
        if (p.ValueKind == JsonValueKind.True) return true;
        if (p.ValueKind == JsonValueKind.False) return false;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int v)) return v != 0;
        if (p.ValueKind == JsonValueKind.String)
        {
            string? s = p.GetString();
            if (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return fallback;
    }

    private void CenterOnGame()
    {
        try
        {
            var p = Process.GetProcessesByName("mjlin").FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero);
            if (p != null && GetWindowRect(p.MainWindowHandle, out RECT r))
            {
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                Left = r.Left + Math.Max(0, (w - Width) / 2);
                Top = r.Top + Math.Max(0, (h - Height) / 2);
                return;
            }
        }
        catch { }
        var area = Screen.PrimaryScreen!.WorkingArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    private void DragMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
    }

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private sealed class NamedId
    {
        public int Id { get; }
        public string Name { get; }
        public NamedId(int id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }
}
