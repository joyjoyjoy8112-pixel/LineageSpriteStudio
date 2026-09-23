namespace LineageSpriteStudio;

internal sealed record SprExtractionChoice(
    string IdxPath,
    string IdxName,
    string PakName,
    string EntryName)
{
    public string Key => CreateKey(IdxPath, EntryName);

    public static string CreateKey(string idxPath, string entryName) =>
        Path.GetFullPath(idxPath) + "\0" + entryName;
}

internal sealed class SprSelectionDialog : Form
{
    private const int MaxVisibleRows = 5000;

    private readonly IReadOnlyList<SprExtractionChoice> _items;
    private readonly List<SprExtractionChoice> _filtered = new();
    private readonly HashSet<string> _selectedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox _search = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "예: 61-9.spr, 61-18.spr 또는 61-"
    };
    private readonly Label _summary = new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Padding = new Padding(8, 5, 0, 0)
    };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        MultiSelect = true,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        EditMode = DataGridViewEditMode.EditOnEnter
    };
    private readonly Button _selectFiltered = new() { Text = "검색 결과 전체 선택", AutoSize = true };
    private readonly Button _clearSelection = new() { Text = "전체 선택 해제", AutoSize = true };
    private readonly Button _ok = new() { Text = "선택한 SPR 추출", AutoSize = true };
    private readonly Button _cancel = new() { Text = "취소", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly System.Windows.Forms.Timer _filterTimer = new() { Interval = 180 };
    private bool _rebuilding;

    public IReadOnlyList<SprExtractionChoice> SelectedItems =>
        _items.Where(x => _selectedKeys.Contains(x.Key)).ToList();

    public SprSelectionDialog(IReadOnlyList<SprExtractionChoice> items, string initialFilter)
    {
        _items = items;

        Text = "원하는 SPR 선택";
        Width = 900;
        Height = 680;
        MinimumSize = new Size(680, 460);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);
        ShowInTaskbar = false;

        BuildUi();

        _search.Text = initialFilter;
        _search.TextChanged += (_, _) =>
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            ApplyFilter();
        };

        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (_rebuilding || e.RowIndex < 0 || e.ColumnIndex != 0)
                return;

            UpdateSelectionFromRow(_grid.Rows[e.RowIndex]);
            UpdateSummary();
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex == 0)
                return;

            var cell = _grid.Rows[e.RowIndex].Cells[0];
            cell.Value = !(cell.Value is bool value && value);
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        _selectFiltered.Click += (_, _) =>
        {
            SaveVisibleSelections();
            foreach (var item in _filtered)
                _selectedKeys.Add(item.Key);
            RefreshVisibleChecks();
            UpdateSummary();
        };
        _clearSelection.Click += (_, _) =>
        {
            _selectedKeys.Clear();
            RefreshVisibleChecks();
            UpdateSummary();
        };
        _ok.Click += (_, _) =>
        {
            SaveVisibleSelections();
            if (_selectedKeys.Count == 0)
            {
                MessageBox.Show(this, "추출할 SPR을 하나 이상 체크하세요.", "SPR 선택",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        AcceptButton = _ok;
        CancelButton = _cancel;
        Shown += (_, _) =>
        {
            ApplyFilter();
            _search.Focus();
            _search.SelectAll();
        };
        FormClosed += (_, _) =>
        {
            _filterTimer.Stop();
            _filterTimer.Dispose();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "추출할 SPR을 검색한 뒤 체크하세요. 여러 파일명은 쉼표로 검색할 수 있습니다.",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 7)
        }, 0, 0);

        var searchBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8)
        };
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchBar.Controls.Add(new Label
        {
            Text = "검색",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 0)
        }, 0, 0);
        searchBar.Controls.Add(_search, 1, 0);
        searchBar.Controls.Add(_summary, 2, 0);
        root.Controls.Add(searchBar, 0, 1);

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Checked",
            HeaderText = "선택",
            Width = 55,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Entry",
            HeaderText = "SPR 파일",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 70
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Idx",
            HeaderText = "원본 IDX",
            ReadOnly = true,
            Width = 135,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Pak",
            HeaderText = "원본 PAK",
            ReadOnly = true,
            Width = 135,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None
        });
        root.Controls.Add(_grid, 0, 2);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(0, 9, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var selectionButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Dock = DockStyle.Fill
        };
        selectionButtons.Controls.Add(_selectFiltered);
        selectionButtons.Controls.Add(_clearSelection);
        footer.Controls.Add(selectionButtons, 0, 0);

        var actionButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right
        };
        actionButtons.Controls.Add(_ok);
        actionButtons.Controls.Add(_cancel);
        footer.Controls.Add(actionButtons, 1, 0);
        root.Controls.Add(footer, 0, 3);
    }

    private void ApplyFilter()
    {
        SaveVisibleSelections();

        string[] terms = _search.Text
            .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _filtered.Clear();
        foreach (var item in _items)
        {
            if (terms.Length == 0 || terms.Any(term =>
                    item.EntryName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.IdxName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    item.PakName.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                _filtered.Add(item);
            }
        }

        _rebuilding = true;
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var item in _filtered.Take(MaxVisibleRows))
            {
                int rowIndex = _grid.Rows.Add(
                    _selectedKeys.Contains(item.Key),
                    item.EntryName,
                    item.IdxName,
                    item.PakName);
                _grid.Rows[rowIndex].Tag = item;
            }

            _grid.ClearSelection();
        }
        finally
        {
            _grid.ResumeLayout();
            _rebuilding = false;
        }

        UpdateSummary();
    }

    private void SaveVisibleSelections()
    {
        if (_rebuilding)
            return;

        _grid.EndEdit();
        foreach (DataGridViewRow row in _grid.Rows)
            UpdateSelectionFromRow(row);
    }

    private void UpdateSelectionFromRow(DataGridViewRow row)
    {
        if (row.Tag is not SprExtractionChoice item)
            return;

        bool isChecked = row.Cells[0].Value is bool value && value;
        if (isChecked)
            _selectedKeys.Add(item.Key);
        else
            _selectedKeys.Remove(item.Key);
    }

    private void RefreshVisibleChecks()
    {
        _rebuilding = true;
        try
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is SprExtractionChoice item)
                    row.Cells[0].Value = _selectedKeys.Contains(item.Key);
            }
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void UpdateSummary()
    {
        string limited = _filtered.Count > MaxVisibleRows
            ? $" · 화면 {MaxVisibleRows:N0}개(검색어로 좁히세요)"
            : "";
        _summary.Text = $"검색 {_filtered.Count:N0} / 전체 {_items.Count:N0} · 선택 {_selectedKeys.Count:N0}{limited}";
        _ok.Text = _selectedKeys.Count == 0
            ? "선택한 SPR 추출"
            : $"선택한 SPR 추출 ({_selectedKeys.Count:N0})";
    }
}
