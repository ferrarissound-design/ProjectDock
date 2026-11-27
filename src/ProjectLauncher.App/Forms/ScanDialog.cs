using ProjectLauncher.Core.Detection;

namespace ProjectLauncher.App.Forms;

/// <summary>親フォルダの直下（深さ1〜2）から候補を探し、チェックしたものだけ登録する。</summary>
internal sealed class ScanDialog : Form
{
    private readonly ProjectCatalog _catalog;
    private readonly TextBox _folder = new();
    private readonly NumericUpDown _depth = new();
    private readonly ListView _list = new();
    private readonly Label _summary = new();
    private readonly Button _scan = new();
    private readonly Button _ok = new();
    private CancellationTokenSource? _cts;

    public ScanDialog(ProjectCatalog catalog)
    {
        _catalog = catalog;

        Text = "親フォルダをスキャン";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(820, 520);
        MinimumSize = new Size(560, 360);
        Padding = new Padding(10);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _folder.Dock = DockStyle.Fill;
        _folder.Text = catalog.Settings.LastScanFolder ?? "";
        _folder.PlaceholderText = @"例: C:\dev";
        var browse = new Button { Text = "参照…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _folder.Text, UseDescriptionForTitle = true, Description = "スキャンする親フォルダ" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _folder.Text = dlg.SelectedPath;
                _ = ScanAsync();
            }
        };
        _depth.Minimum = 1;
        _depth.Maximum = 2;
        _depth.Value = Math.Clamp(catalog.Settings.ScanDepth, 1, 2);
        _depth.Width = 48;
        _scan.Text = "スキャン";
        _scan.AutoSize = true;
        _scan.Click += (_, _) => _ = ScanAsync();

        top.Controls.Add(new Label { Text = "親フォルダ", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        top.Controls.Add(_folder, 1, 0);
        top.Controls.Add(browse, 2, 0);
        top.Controls.Add(new Label { Text = "深さ", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 0, 0, 0) }, 3, 0);
        top.Controls.Add(_depth, 4, 0);
        top.Controls.Add(_scan, 5, 0);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.CheckBoxes = true;
        _list.FullRowSelect = true;
        _list.Columns.Add("名前", 200);
        _list.Columns.Add("種別", 100);
        _list.Columns.Add("Git", 50);
        _list.Columns.Add("パス", 420);
        _list.ItemCheck += (_, e) =>
        {
            // 登録済みはチェックさせない
            if (_list.Items[e.Index].Tag is ScanCandidate { AlreadyRegistered: true })
                e.NewValue = CheckState.Unchecked;
        };
        _list.ItemChecked += (_, _) => UpdateSummary();

        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 5, Padding = new Padding(0, 8, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _summary.AutoSize = true;
        _summary.Anchor = AnchorStyles.Left;
        var all = new Button { Text = "すべて選択", AutoSize = true };
        all.Click += (_, _) => SetAll(true);
        var none = new Button { Text = "選択解除", AutoSize = true };
        none.Click += (_, _) => SetAll(false);
        _ok.Text = "チェックしたものを登録";
        _ok.AutoSize = true;
        _ok.Enabled = false;
        _ok.Click += (_, _) => OnOk();
        var cancel = new Button { Text = "キャンセル", AutoSize = true, DialogResult = DialogResult.Cancel };
        bottom.Controls.Add(_summary, 0, 0);
        bottom.Controls.Add(all, 1, 0);
        bottom.Controls.Add(none, 2, 0);
        bottom.Controls.Add(_ok, 3, 0);
        bottom.Controls.Add(cancel, 4, 0);

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(bottom);
        CancelButton = cancel;
        AcceptButton = _scan;

        _summary.Text = "判定: .git / Unity (Assets + ProjectSettings) / package.json・index.html / .sln・.csproj";
        Shown += (_, _) =>
        {
            if (Directory.Exists(_folder.Text))
                _ = ScanAsync();
        };
    }

    public List<ScanCandidate> Selected { get; } = new();

    private async Task ScanAsync()
    {
        var folder = _folder.Text.Trim().Trim('"');
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, "フォルダが見つかりません。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var depth = (int)_depth.Value;
        var registered = _catalog.Projects.Select(p => p.Path).ToList();

        _scan.Enabled = false;
        _summary.Text = "スキャン中…";
        _list.Items.Clear();
        try
        {
            var found = await Task.Run(() => FolderScanner.Scan(folder, depth, registered, cts.Token), cts.Token);
            _catalog.Settings.LastScanFolder = folder;
            _catalog.Settings.ScanDepth = depth;

            _list.BeginUpdate();
            foreach (var c in found)
            {
                var item = new ListViewItem(c.Name) { Tag = c };
                item.SubItems.Add(ProjectDetector.KindLabel(c.Kind));
                item.SubItems.Add(c.HasGit ? "✓" : "");
                item.SubItems.Add(c.AlreadyRegistered ? c.Path + "（登録済み）" : c.Path);
                if (c.AlreadyRegistered)
                    item.ForeColor = SystemColors.GrayText;
                _list.Items.Add(item);
                item.Checked = !c.AlreadyRegistered;
            }
            _list.EndUpdate();
            UpdateSummary();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _summary.Text = "スキャンに失敗しました: " + ex.Message;
        }
        finally
        {
            _scan.Enabled = true;
        }
    }

    private void SetAll(bool value)
    {
        _list.BeginUpdate();
        foreach (ListViewItem item in _list.Items)
            item.Checked = value;
        _list.EndUpdate();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var total = _list.Items.Count;
        var registered = _list.Items.Cast<ListViewItem>().Count(i => i.Tag is ScanCandidate { AlreadyRegistered: true });
        var checkedCount = _list.CheckedItems.Count;
        _summary.Text = $"候補 {total} 件（登録済み {registered} 件）／ 選択 {checkedCount} 件";
        _ok.Enabled = checkedCount > 0;
    }

    private void OnOk()
    {
        Selected.Clear();
        Selected.AddRange(_list.CheckedItems.Cast<ListViewItem>()
            .Select(i => (ScanCandidate)i.Tag!)
            .Where(c => !c.AlreadyRegistered));
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosed(e);
    }
}
