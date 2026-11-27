using ProjectLauncher.Core.Detection;
using ProjectLauncher.Core.FileSystem;
using ProjectLauncher.Core.Models;
using ProjectLauncher.Core.Services;

namespace ProjectLauncher.App.Forms;

/// <summary>表示名・パス・種別・公開URL・メモ・タグの編集。</summary>
internal sealed class ProjectEditDialog : Form
{
    private static readonly ProjectKind[] Kinds = { ProjectKind.Unity, ProjectKind.Web, ProjectKind.WindowsApp, ProjectKind.Other };

    private readonly ProjectEntry _entry;
    private readonly ProjectCatalog _catalog;
    private readonly TextBox _name = new();
    private readonly TextBox _path = new();
    private readonly ComboBox _kind = new();
    private readonly CheckBox _autoKind = new();
    private readonly TextBox _url = new();
    private readonly TextBox _tags = new();
    private readonly TextBox _memo = new();

    public ProjectEditDialog(ProjectEntry entry, ProjectCatalog catalog)
    {
        _entry = entry;
        _catalog = catalog;

        Text = "プロジェクトの編集";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(380)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _name.Text = entry.Name;
        _path.Text = entry.Path;
        _kind.DropDownStyle = ComboBoxStyle.DropDownList;
        _kind.Items.AddRange(Kinds.Select(k => (object)ProjectDetector.KindLabel(k)).ToArray());
        _kind.SelectedIndex = Array.IndexOf(Kinds, entry.Kind) is var i and >= 0 ? i : Kinds.Length - 1;
        _autoKind.Text = "自動判定";
        _autoKind.AutoSize = true;
        _autoKind.Checked = !entry.KindIsManual;
        _autoKind.CheckedChanged += (_, _) => SyncKind();
        _url.Text = entry.PublicUrl ?? "";
        _url.PlaceholderText = "https://...（任意）";
        _tags.Text = string.Join(", ", entry.Tags);
        _tags.PlaceholderText = "カンマ区切り（例: unity, ホラー）";
        _memo.Text = entry.Memo ?? "";
        _memo.Multiline = true;
        _memo.ScrollBars = ScrollBars.Vertical;
        _memo.Height = LogicalToDeviceUnits(80);
        _memo.AcceptsReturn = true;

        var browse = new Button { Text = "参照…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _path.Text, UseDescriptionForTitle = true, Description = "プロジェクトのフォルダ" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _path.Text = dlg.SelectedPath;
                SyncKind();
            }
        };

        AddRow(grid, "表示名", _name);
        AddRow(grid, "フォルダ", _path, browse);
        AddRow(grid, "種別", _kind, _autoKind);
        AddRow(grid, "公開URL", _url);
        AddRow(grid, "タグ", _tags);
        AddRow(grid, "メモ", _memo);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.None, AutoSize = true, MinimumSize = new Size(LogicalToDeviceUnits(80), 0) };
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(LogicalToDeviceUnits(80), 0) };
        ok.Click += (_, _) => OnOk();
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        grid.Controls.Add(buttons, 0, grid.RowCount);
        grid.SetColumnSpan(buttons, 3);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
        SyncKind();
    }

    public bool PathChanged { get; private set; }

    private static void AddRow(TableLayoutPanel grid, string label, Control control, Control? extra = null)
    {
        var row = grid.RowCount++;
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, row);
        control.Dock = DockStyle.Fill;
        grid.Controls.Add(control, 1, row);
        if (extra != null)
        {
            extra.Anchor = AnchorStyles.Left;
            grid.Controls.Add(extra, 2, row);
        }
    }

    private void SyncKind()
    {
        _kind.Enabled = !_autoKind.Checked;
        if (_autoKind.Checked)
        {
            var detected = ProjectDetector.DetectKind(_path.Text);
            _kind.SelectedIndex = Array.IndexOf(Kinds, detected);
        }
    }

    private void OnOk()
    {
        var name = _name.Text.Trim();
        var path = PathUtil.Normalize(_path.Text);
        var url = _url.Text.Trim();

        if (name.Length == 0)
        {
            Warn("表示名を入力してください。", _name);
            return;
        }
        if (path.Length == 0)
        {
            Warn("フォルダを指定してください。", _path);
            return;
        }
        if (!PathUtil.SamePath(path, _entry.Path) && _catalog.Contains(path))
        {
            Warn("そのフォルダは既に登録されています。", _path);
            return;
        }
        if (url.Length > 0 && !DisplayFormat.IsHttpUrl(url))
        {
            Warn("公開URLは http:// または https:// で始まる URL にしてください。", _url);
            return;
        }
        if (!Directory.Exists(path) &&
            MessageBox.Show(this, "フォルダが見つかりません。このまま保存しますか？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        PathChanged = !PathUtil.SamePath(path, _entry.Path);
        _entry.Name = name;
        _entry.Path = path;
        _entry.KindIsManual = !_autoKind.Checked;
        _entry.Kind = Kinds[Math.Max(0, _kind.SelectedIndex)];
        _entry.PublicUrl = url.Length == 0 ? null : url;
        _entry.Tags = ProjectFilter.ParseTags(_tags.Text);
        _entry.Memo = string.IsNullOrWhiteSpace(_memo.Text) ? null : _memo.Text.Trim();
        if (PathChanged)
            _entry.CachedStatus = null;

        DialogResult = DialogResult.OK;
        Close();
    }

    private void Warn(string message, Control focus)
    {
        MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
    }
}
