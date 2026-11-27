using System.Diagnostics;
using PushButtonState = System.Windows.Forms.VisualStyles.PushButtonState;
using ProjectLauncher.App.Infrastructure;
using ProjectLauncher.Core.Detection;
using ProjectLauncher.Core.FileSystem;
using ProjectLauncher.Core.Git;
using ProjectLauncher.Core.Logging;
using ProjectLauncher.Core.Models;
using ProjectLauncher.Core.Services;

namespace ProjectLauncher.App.Forms;

internal sealed class MainForm : Form
{
    /// <summary>ウィンドウを開くたびの自動更新を、この間隔以内なら省略する（ホットキー連打対策）。</summary>
    private static readonly TimeSpan AutoRefreshMinInterval = TimeSpan.FromSeconds(30);

    private enum RowAction { Explorer, VsCode, GitHub, PublicUrl, Terminal, Unity, Remove }
    private enum Availability { Enabled, Disabled, Hidden }

    private readonly ProjectCatalog _catalog;
    private readonly StatusRefresher _refresher;
    private readonly IGitClient _git;
    private readonly ExternalLauncher _launcher;
    private readonly IAppLogger _log;
    private readonly TrayAppContext _app;

    private readonly TextBox _search = new();
    private readonly ComboBox _sort = new();
    private readonly CheckBox _onlyDirty = new();
    private readonly DataGridView _grid = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripStatusLabel _gitLabel = new();
    private readonly ContextMenuStrip _rowMenu = new();
    private readonly Font _emojiFont;
    private readonly Font _boldFont;

    private readonly Dictionary<int, RowAction> _actionColumns = new();
    private int _colKind, _colName, _colModified, _colGit, _colBranch;

    private CancellationTokenSource? _refreshCts;
    private DateTime _lastAutoRefresh = DateTime.MinValue;
    private int _activeRefreshes;
    private bool _refreshing => _activeRefreshes > 0;

    public MainForm(ProjectCatalog catalog, StatusRefresher refresher, IGitClient git, ExternalLauncher launcher,
        IAppLogger log, TrayAppContext app)
    {
        _catalog = catalog;
        _refresher = refresher;
        _git = git;
        _launcher = launcher;
        _log = log;
        _app = app;

        _emojiFont = new Font("Segoe UI Emoji", 10f);
        _boldFont = new Font(Font, FontStyle.Bold);

        Text = "ProjectLauncher";
        Icon = AppIcon.Get();
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(760, 360);
        Size = new Size(1100, 640);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;

        BuildLayout();
        LoadSavedBounds();

        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        KeyDown += OnFormKeyDown;
        _catalog.Changed += (_, _) => ApplyFilter();

        ApplyFilter();
    }

    // ───────────────────────── レイアウト ─────────────────────────

    private void BuildLayout()
    {
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 8,
            Padding = new Padding(8, 8, 8, 4),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 1; i < 8; i++)
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _search.PlaceholderText = "検索（名前・タグ・メモ）  Ctrl+F";
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 3, 8, 3);
        _search.TextChanged += (_, _) => ApplyFilter();
        _search.KeyDown += OnSearchKeyDown;

        _sort.DropDownStyle = ComboBoxStyle.DropDownList;
        _sort.Items.AddRange(new object[] { "最終更新が新しい順", "名前順", "未コミットありを上に" });
        _sort.SelectedIndex = (int)_catalog.Settings.SortMode;
        _sort.Width = 170;
        _sort.Margin = new Padding(0, 3, 8, 3);
        _sort.SelectedIndexChanged += (_, _) =>
        {
            _catalog.Settings.SortMode = (SortMode)_sort.SelectedIndex;
            _catalog.Save();
            ApplyFilter();
        };

        _onlyDirty.Text = "未コミットありだけ";
        _onlyDirty.AutoSize = true;
        _onlyDirty.Checked = _catalog.Settings.OnlyDirty;
        _onlyDirty.Margin = new Padding(0, 6, 8, 3);
        _onlyDirty.CheckedChanged += (_, _) =>
        {
            _catalog.Settings.OnlyDirty = _onlyDirty.Checked;
            _catalog.Save();
            ApplyFilter();
        };

        top.Controls.Add(_search, 0, 0);
        top.Controls.Add(_sort, 1, 0);
        top.Controls.Add(_onlyDirty, 2, 0);
        top.Controls.Add(MakeButton("＋ フォルダ追加", (_, _) => AddFolderByDialog()), 3, 0);
        top.Controls.Add(MakeButton("親フォルダをスキャン", (_, _) => OpenScanDialog()), 4, 0);
        top.Controls.Add(MakeButton("⟳ 再スキャン", (_, _) => RefreshAll(force: true)), 5, 0);
        top.Controls.Add(MakeButton("⚙ 設定", (_, _) => OpenSettings()), 6, 0);

        BuildGrid();
        BuildRowMenu();

        var status = new StatusStrip { SizingGrip = true };
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        status.Items.Add(_statusLabel);
        status.Items.Add(_gitLabel);

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(status);
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 2, 6, 2), UseVisualStyleBackColor = true };
        b.Click += onClick;
        return b;
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToOrderColumns = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _grid.RowTemplate.Height = LogicalToDeviceUnits(30);
        _grid.ShowCellToolTips = true;
        _grid.AllowDrop = true;
        _grid.StandardTab = true;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(220, 235, 252);
        _grid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        _grid.EnableHeadersVisualStyles = true;

        _colKind = AddTextColumn("", 40, emoji: true);
        _colName = AddTextColumn("表示名", 0, fill: true);
        _colModified = AddTextColumn("最終更新", 150);
        _colGit = AddTextColumn("Git", 120);
        _colBranch = AddTextColumn("ブランチ", 110);
        AddActionColumn(RowAction.Explorer, "📁");
        AddActionColumn(RowAction.VsCode, "💻");
        AddActionColumn(RowAction.GitHub, "🐙");
        AddActionColumn(RowAction.PublicUrl, "🌐");
        AddActionColumn(RowAction.Terminal, "⌨");
        AddActionColumn(RowAction.Unity, "🎮");
        AddActionColumn(RowAction.Remove, "🗑");

        _grid.CellPainting += OnCellPainting;
        _grid.CellClick += OnCellClick;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && !_actionColumns.ContainsKey(e.ColumnIndex))
                EditProject(EntryAt(e.RowIndex));
        };
        _grid.CellToolTipTextNeeded += OnCellToolTipTextNeeded;
        _grid.CellMouseEnter += (_, e) =>
        {
            var hand = e.RowIndex >= 0 && _actionColumns.TryGetValue(e.ColumnIndex, out var a)
                       && GetAvailability(EntryAt(e.RowIndex), a, out var _) == Availability.Enabled;
            _grid.Cursor = hand ? Cursors.Hand : Cursors.Default;
        };
        _grid.CellMouseLeave += (_, _) => _grid.Cursor = Cursors.Default;
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[_colName];
            }
        };
        _grid.KeyDown += OnGridKeyDown;
        _grid.DragEnter += OnDragEnter;
        _grid.DragDrop += OnDragDrop;
    }

    private int AddTextColumn(string header, int width, bool fill = false, bool emoji = false)
    {
        var col = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
        };
        if (!fill)
            col.Width = LogicalToDeviceUnits(width);
        if (emoji)
        {
            col.DefaultCellStyle.Font = _emojiFont;
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        return _grid.Columns.Add(col);
    }

    private void AddActionColumn(RowAction action, string glyph)
    {
        var col = new DataGridViewTextBoxColumn
        {
            HeaderText = "",
            Width = LogicalToDeviceUnits(38),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Tag = glyph,
            ToolTipText = ActionLabel(action),
        };
        col.DefaultCellStyle.Font = _emojiFont;
        col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        var index = _grid.Columns.Add(col);
        _actionColumns[index] = action;
    }

    private void BuildRowMenu()
    {
        _rowMenu.Items.Add("編集…", null, (_, _) => WithSelected(EditProject));
        _rowMenu.Items.Add("種別を自動判定し直す", null, (_, _) => WithSelected(p =>
        {
            p.KindIsManual = false;
            p.Kind = ProjectDetector.DetectKind(p.Path);
            _catalog.NotifyChanged();
        }));
        _rowMenu.Items.Add("この行だけ再取得", null, (_, _) => WithSelected(p => _ = RefreshAsync(new[] { p })));
        _rowMenu.Items.Add("パスをコピー", null, (_, _) => WithSelected(p => Clipboard.SetText(p.Path)));
        _rowMenu.Items.Add(new ToolStripSeparator());
        _rowMenu.Items.Add("一覧から削除…", null, (_, _) => WithSelected(ConfirmRemove));
        _grid.ContextMenuStrip = _rowMenu;
        _rowMenu.Opening += (_, e) => e.Cancel = SelectedEntry() == null;
    }

    // ───────────────────────── 表示 ─────────────────────────

    public void ShowAndActivate(bool focusSearch)
    {
        if (!Visible)
            Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        // フォアグラウンドに出す（ホットキー受信直後はフォアグラウンド権限がある）
        TopMost = true;
        TopMost = false;
        Activate();
        BringToFront();

        if (focusSearch)
        {
            _search.Focus();
            _search.SelectAll();
        }

        if (DateTime.Now - _lastAutoRefresh > AutoRefreshMinInterval)
            RefreshAll(force: false);
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedEntry()?.Id;
        var list = ProjectFilter.Apply(_catalog.Projects, _search.Text, (SortMode)Math.Max(0, _sort.SelectedIndex), _onlyDirty.Checked);

        _grid.SuspendLayout();
        _grid.Rows.Clear();
        foreach (var p in list)
        {
            var i = _grid.Rows.Add();
            _grid.Rows[i].Tag = p;
            FillRow(_grid.Rows[i], p);
        }
        _grid.ResumeLayout();

        if (selectedId != null)
        {
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (((ProjectEntry)r.Tag!).Id == selectedId)
                {
                    r.Selected = true;
                    _grid.CurrentCell = r.Cells[_colName];
                    break;
                }
            }
        }
        else
        {
            _grid.ClearSelection();
        }

        UpdateStatusBar();
    }

    private void FillRow(DataGridViewRow row, ProjectEntry p)
    {
        var s = p.CachedStatus;
        var missing = s is { Exists: false };

        row.Cells[_colKind].Value = KindGlyph(p.Kind);
        row.Cells[_colName].Value = missing ? $"{p.Name}  — 見つかりません" : p.Name;
        row.Cells[_colModified].Value = missing ? "" : DisplayFormat.LastModified(s?.LastModified, DateTimeOffset.Now);
        row.Cells[_colGit].Value = DisplayFormat.GitColumn(s);
        row.Cells[_colBranch].Value = s?.GitState == GitState.Ok ? s.Branch ?? "" : "";
        foreach (var (col, _) in _actionColumns)
            row.Cells[col].Value = _grid.Columns[col].Tag;

        var nameStyle = row.Cells[_colName].Style;
        var gitStyle = row.Cells[_colGit].Style;
        nameStyle.ForeColor = missing ? Color.Firebrick : SystemColors.ControlText;
        nameStyle.Font = _boldFont;
        gitStyle.ForeColor = (s?.GitState, s?.Exists) switch
        {
            (_, false) => Color.Firebrick,
            (GitState.Ok, _) when s!.ChangeCount > 0 => Color.FromArgb(230, 120, 0),
            (GitState.Ok, _) => Color.SeaGreen,
            (GitState.Error, _) => Color.Firebrick,
            _ => Color.Gray,
        };
        gitStyle.Font = s?.HasUncommittedChanges == true ? _boldFont : null;
        row.Cells[_colModified].Style.ForeColor = SystemColors.GrayText;
        row.Cells[_colBranch].Style.ForeColor = SystemColors.GrayText;
        gitStyle.SelectionForeColor = gitStyle.ForeColor;
        nameStyle.SelectionForeColor = nameStyle.ForeColor;
    }

    private void UpdateRow(ProjectEntry p)
    {
        foreach (DataGridViewRow r in _grid.Rows)
        {
            if (ReferenceEquals(r.Tag, p))
            {
                FillRow(r, p);
                _grid.InvalidateRow(r.Index);
                return;
            }
        }
    }

    private void UpdateStatusBar(int? done = null, int? total = null)
    {
        var all = _catalog.Projects.Count;
        var dirty = _catalog.Projects.Count(p => p.CachedStatus?.HasUncommittedChanges == true);
        var missing = _catalog.Projects.Count(p => p.CachedStatus is { Exists: false });
        var text = $"{_grid.Rows.Count} / {all} 件表示　未コミットあり {dirty} 件";
        if (missing > 0)
            text += $"　見つからない {missing} 件";
        if (_refreshing && done != null)
            text += $"　｜ 更新中… {done}/{total}";
        else if (all == 0)
            text = "フォルダをこのウィンドウにドラッグ＆ドロップするか、「親フォルダをスキャン」で登録してください";
        _statusLabel.Text = text;
    }

    private static string KindGlyph(ProjectKind k) => k switch
    {
        ProjectKind.Unity => "🎮",
        ProjectKind.Web => "🌐",
        ProjectKind.WindowsApp => "🪟",
        _ => "📦",
    };

    private static string ActionLabel(RowAction a) => a switch
    {
        RowAction.Explorer => "エクスプローラーで開く",
        RowAction.VsCode => "VS Code で開く",
        RowAction.GitHub => "GitHub を開く",
        RowAction.PublicUrl => "公開URLを開く",
        RowAction.Terminal => "ターミナルで開く",
        RowAction.Unity => "Unity で開く",
        RowAction.Remove => "一覧から削除",
        _ => "",
    };

    private Availability GetAvailability(ProjectEntry p, RowAction a, out string? reason)
    {
        reason = null;
        var s = p.CachedStatus;
        var exists = s?.Exists ?? Directory.Exists(p.Path);

        if (a == RowAction.Remove)
            return exists ? Availability.Hidden : Availability.Enabled;

        if (!exists && a != RowAction.PublicUrl)
        {
            reason = "フォルダが見つかりません";
            return a == RowAction.GitHub && s?.GitHubUrl != null ? Availability.Enabled : Availability.Disabled;
        }

        switch (a)
        {
            case RowAction.VsCode when _launcher.VsCodePath == null:
                reason = "VS Code が見つかりません（PATH の code、%LOCALAPPDATA%\\Programs\\Microsoft VS Code\\Code.exe のどちらもありません）";
                return Availability.Disabled;
            case RowAction.GitHub:
                if (s == null) { reason = "状態を取得中です"; return Availability.Disabled; }
                if (s.GitState == GitState.NotARepository) return Availability.Hidden;
                if (s.GitHubUrl != null) return Availability.Enabled;
                reason = s.GitState switch
                {
                    GitState.GitNotFound => "git が見つからないため origin を読めません",
                    GitState.Error => "git の実行に失敗しました: " + s.GitError,
                    _ when s.RemoteUrl == null => "origin リモートが設定されていません",
                    _ => $"origin が GitHub ではありません: {s.RemoteUrl}",
                };
                return Availability.Disabled;
            case RowAction.PublicUrl:
                return string.IsNullOrWhiteSpace(p.PublicUrl) ? Availability.Hidden : Availability.Enabled;
            case RowAction.Unity:
                return p.Kind == ProjectKind.Unity ? Availability.Enabled : Availability.Hidden;
            default:
                return Availability.Enabled;
        }
    }

    private void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || !_actionColumns.TryGetValue(e.ColumnIndex, out var action) || e.Graphics == null)
            return;

        var p = EntryAt(e.RowIndex);
        var avail = GetAvailability(p, action, out var _);
        e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);
        if (avail != Availability.Hidden)
        {
            var r = Rectangle.Inflate(e.CellBounds, -LogicalToDeviceUnits(3), -LogicalToDeviceUnits(3));
            var state = avail == Availability.Enabled ? PushButtonState.Normal : PushButtonState.Disabled;
            ButtonRenderer.DrawButton(e.Graphics, r, false, state);
            var color = avail == Availability.Enabled
                ? (action == RowAction.Remove ? Color.Firebrick : SystemColors.ControlText)
                : SystemColors.GrayText;
            TextRenderer.DrawText(e.Graphics, (string?)_grid.Columns[e.ColumnIndex].Tag, _emojiFont, r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        e.Handled = true;
    }

    private void OnCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0)
            return;
        var p = EntryAt(e.RowIndex);
        var s = p.CachedStatus;

        if (_actionColumns.TryGetValue(e.ColumnIndex, out var action))
        {
            var avail = GetAvailability(p, action, out var reason);
            e.ToolTipText = avail switch
            {
                Availability.Hidden => "",
                Availability.Disabled => $"{ActionLabel(action)}（使用不可）\n{reason}",
                _ => action switch
                {
                    RowAction.GitHub => $"GitHub を開く\n{s?.GitHubUrl}",
                    RowAction.PublicUrl => $"公開URLを開く\n{p.PublicUrl}",
                    RowAction.VsCode => $"VS Code で開く\n{_launcher.VsCodePath}",
                    RowAction.Terminal => _launcher.WindowsTerminalPath != null ? "Windows Terminal で開く" : "コマンドプロンプトで開く（Windows Terminal なし）",
                    RowAction.Unity => "ProjectVersion.txt と同じバージョンの Unity エディタで開く（なければ Unity Hub を起動）",
                    _ => ActionLabel(action),
                },
            };
            return;
        }

        if (e.ColumnIndex == _colName || e.ColumnIndex == _colKind)
        {
            var lines = new List<string> { p.Path, $"種別: {ProjectDetector.KindLabel(p.Kind)}{(p.KindIsManual ? "（手動）" : "")}" };
            if (p.Tags.Count > 0) lines.Add("タグ: " + string.Join(", ", p.Tags));
            if (!string.IsNullOrWhiteSpace(p.Memo)) lines.Add("メモ: " + p.Memo);
            if (!string.IsNullOrWhiteSpace(p.PublicUrl)) lines.Add("URL: " + p.PublicUrl);
            e.ToolTipText = string.Join("\n", lines);
        }
        else if (e.ColumnIndex == _colModified && s?.LastModified != null)
        {
            var src = s.LastModifiedSource == LastModifiedSource.GitCommit
                ? "最終コミット日時"
                : "ファイルの更新日時の最大値（" + string.Join(" / ", ExcludedFolders.Names) + " は除外）";
            e.ToolTipText = $"{s.LastModified.Value.LocalDateTime:yyyy/MM/dd HH:mm:ss}\n{src}";
        }
        else if (e.ColumnIndex == _colGit && s != null)
        {
            e.ToolTipText = s.GitState switch
            {
                GitState.Error => "git エラー: " + s.GitError,
                GitState.GitNotFound => "git.exe が PATH に見つかりません。Git for Windows を入れると表示されます",
                GitState.Ok => $"origin: {s.RemoteUrl ?? "(なし)"}\n取得: {s.CheckedAt.LocalDateTime:HH:mm:ss}",
                _ => "",
            };
        }
    }

    // ───────────────────────── 操作 ─────────────────────────

    private void OnCellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || !_actionColumns.TryGetValue(e.ColumnIndex, out var action))
            return;
        var p = EntryAt(e.RowIndex);
        if (GetAvailability(p, action, out var _) != Availability.Enabled)
            return;
        RunAction(p, action);
    }

    private void RunAction(ProjectEntry p, RowAction action)
    {
        switch (action)
        {
            case RowAction.Explorer: _launcher.OpenExplorer(p.Path); break;
            case RowAction.VsCode: _launcher.OpenVsCode(p.Path); break;
            case RowAction.GitHub: _launcher.OpenUrl(p.CachedStatus?.GitHubUrl); break;
            case RowAction.PublicUrl: _launcher.OpenUrl(p.PublicUrl); break;
            case RowAction.Terminal: _launcher.OpenTerminal(p.Path); break;
            case RowAction.Unity: _launcher.OpenUnity(p); break;
            case RowAction.Remove: ConfirmRemove(p); break;
        }
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.F)
        {
            _search.Focus();
            _search.SelectAll();
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.F5)
        {
            RefreshAll(force: true);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            if (_search.Focused && _search.TextLength > 0)
                _search.Clear();
            else
                Hide();
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Down or Keys.Enter && _grid.Rows.Count > 0)
        {
            _grid.Focus();
            _grid.CurrentCell = _grid.Rows[0].Cells[_colName];
            _grid.Rows[0].Selected = true;
            if (e.KeyCode == Keys.Enter)
                OpenDefault(EntryAt(0));
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        var p = SelectedEntry();
        if (p == null)
            return;
        if (e.KeyCode == Keys.Enter)
        {
            OpenDefault(p);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            ConfirmRemove(p);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F2)
        {
            EditProject(p);
            e.Handled = true;
        }
    }

    /// <summary>Enter キー：VS Code があれば VS Code、なければエクスプローラー。</summary>
    private void OpenDefault(ProjectEntry p)
    {
        if (GetAvailability(p, RowAction.VsCode, out var _) == Availability.Enabled)
            RunAction(p, RowAction.VsCode);
        else if (GetAvailability(p, RowAction.Explorer, out var _) == Availability.Enabled)
            RunAction(p, RowAction.Explorer);
    }

    private void ConfirmRemove(ProjectEntry p)
    {
        var r = MessageBox.Show(this, $"「{p.Name}」を一覧から削除しますか？\n（フォルダ自体は削除しません）\n\n{p.Path}",
            "ProjectLauncher", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (r != DialogResult.OK)
            return;
        _catalog.Remove(p);
        _catalog.NotifyChanged();
    }

    private void EditProject(ProjectEntry p)
    {
        using var dlg = new ProjectEditDialog(p, _catalog);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        var pathChanged = dlg.PathChanged;
        _catalog.NotifyChanged();
        if (pathChanged)
            _ = RefreshAsync(new[] { p });
    }

    private void AddFolderByDialog()
    {
        using var dlg = new FolderBrowserDialog { Description = "登録するプロジェクトのフォルダ", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            AddFolders(new[] { dlg.SelectedPath });
    }

    private void OpenScanDialog()
    {
        using var dlg = new ScanDialog(_catalog);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;
        var added = dlg.Selected
            .Select(c => _catalog.Add(c.Path, c.Name, c.Kind))
            .Where(e => e != null)
            .Cast<ProjectEntry>()
            .ToList();
        _catalog.NotifyChanged();
        if (added.Count > 0)
            _ = RefreshAsync(added);
        _statusLabel.Text = $"{added.Count} 件を登録しました";
    }

    public void OpenSettings()
    {
        using var dlg = new SettingsDialog(_catalog.Settings, _app, _log);
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _catalog.Save();
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Any(Directory.Exists))
            e.Effect = DragDropEffects.Link;
        else
            e.Effect = DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            AddFolders(paths);
    }

    private void AddFolders(IEnumerable<string> paths)
    {
        var dirs = paths.Where(Directory.Exists).ToList();
        var added = new List<ProjectEntry>();
        var skipped = 0;
        foreach (var d in dirs)
        {
            var entry = _catalog.Add(d);
            if (entry != null) added.Add(entry);
            else skipped++;
        }
        _catalog.NotifyChanged();
        if (added.Count > 0)
            _ = RefreshAsync(added);

        _statusLabel.Text = skipped > 0
            ? $"{added.Count} 件を登録しました（{skipped} 件は登録済み）"
            : $"{added.Count} 件を登録しました";
    }

    // ───────────────────────── 状態の更新 ─────────────────────────

    /// <summary>全件の状態を取り直す。force=false はウィンドウ表示時の自動更新。</summary>
    public void RefreshAll(bool force)
    {
        if (!force && _refreshing)
            return;
        _lastAutoRefresh = DateTime.Now;
        _ = RefreshAsync(_catalog.Projects.ToList());
    }

    private async Task RefreshAsync(IReadOnlyList<ProjectEntry> targets)
    {
        if (targets.Count == 0)
            return;

        var isFull = targets.Count == _catalog.Projects.Count;
        CancellationTokenSource cts;
        if (isFull)
        {
            _refreshCts?.Cancel();
            _refreshCts = cts = new CancellationTokenSource();
        }
        else
        {
            cts = new CancellationTokenSource();
        }

        _activeRefreshes++;
        var done = 0;
        var sw = Stopwatch.StartNew();
        UpdateStatusBar(0, targets.Count);
        UpdateGitLabel();

        try
        {
            await _refresher.RefreshAsync(targets, _catalog.Settings.MaxParallelism, (p, status) =>
            {
                // スレッドプール上：ファイルを見る種別判定はここで済ませる
                var kind = !p.KindIsManual && status.Exists ? ProjectDetector.DetectKind(p.Path) : p.Kind;
                BeginInvokeSafe(() =>
                {
                    p.CachedStatus = status;
                    p.Kind = kind;
                    UpdateRow(p);
                    UpdateStatusBar(Interlocked.Increment(ref done), targets.Count);
                });
            }, cts.Token);

            _log.Info($"状態更新 {targets.Count} 件 {sw.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException)
        {
            _log.Info("状態更新をキャンセル");
            return;
        }
        finally
        {
            _activeRefreshes--;
            if (ReferenceEquals(_refreshCts, cts))
                _refreshCts = null;
            cts.Dispose();
        }

        // 並び順が変わりうるので最後にまとめて並べ直し、キャッシュを保存
        BeginInvokeSafe(() =>
        {
            _catalog.Save();
            ApplyFilter();
            UpdateGitLabel();
        });
    }

    private void UpdateGitLabel()
    {
        // IsAvailable は初回にプロセス起動するので UI スレッドでは確定後だけ見る
        _ = Task.Run(() => _git.IsAvailable).ContinueWith(t =>
            BeginInvokeSafe(() => _gitLabel.Text = t.Result ? "" : "⚠ git未検出（Git列は表示されません）"),
            TaskScheduler.Default);
    }

    private void BeginInvokeSafe(System.Action a)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try { BeginInvoke(a); }
        catch (InvalidOperationException) { /* 終了中 */ }
    }

    // ───────────────────────── ウィンドウ ─────────────────────────

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_app.IsExiting)
        {
            // ×ボタン：終了せずトレイへ
            e.Cancel = true;
            SaveBounds();
            Hide();
            if (!_catalog.Settings.TrayHintShown)
            {
                _catalog.Settings.TrayHintShown = true;
                _catalog.Save();
                _app.ShowBalloon("タスクトレイに格納しました。終了するにはトレイアイコンの右クリック →「終了」。");
            }
            return;
        }

        SaveBounds();
        _catalog.Save();
        base.OnFormClosing(e);
    }

    public void PrepareExit()
    {
        _refreshCts?.Cancel();
        SaveBounds();
    }

    private void SaveBounds()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (b.Width > 0 && b.Height > 0)
            _catalog.Settings.WindowBounds = new[] { b.X, b.Y, b.Width, b.Height };
    }

    private void LoadSavedBounds()
    {
        var b = _catalog.Settings.WindowBounds;
        if (b is not { Length: 4 })
            return;
        var rect = new Rectangle(b[0], b[1], b[2], b[3]);
        // モニター構成が変わって画面外になっていたら復元しない
        if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = rect;
        }
    }

    // ───────────────────────── ヘルパー ─────────────────────────

    private ProjectEntry EntryAt(int rowIndex) => (ProjectEntry)_grid.Rows[rowIndex].Tag!;

    private ProjectEntry? SelectedEntry()
        => _grid.CurrentRow?.Tag as ProjectEntry ?? (_grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as ProjectEntry : null);

    private void WithSelected(Action<ProjectEntry> a)
    {
        var p = SelectedEntry();
        if (p != null) a(p);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _emojiFont.Dispose();
            _boldFont.Dispose();
            _refreshCts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
