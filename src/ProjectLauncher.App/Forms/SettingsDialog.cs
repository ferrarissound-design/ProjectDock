using System.Diagnostics;
using ProjectLauncher.App.Infrastructure;
using ProjectLauncher.Core.Logging;
using ProjectLauncher.Core.Models;

namespace ProjectLauncher.App.Forms;

internal sealed class SettingsDialog : Form
{
    private readonly AppSettings _settings;
    private readonly TrayAppContext _app;
    private readonly IAppLogger _log;
    private readonly TextBox _hotkey = new();
    private readonly CheckBox _startup = new();
    private readonly NumericUpDown _parallel = new();
    private string _hotkeyText;

    public SettingsDialog(AppSettings settings, TrayAppContext app, IAppLogger log)
    {
        _settings = settings;
        _app = app;
        _log = log;
        _hotkeyText = settings.Hotkey;

        Text = "設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(300)));

        _hotkey.ReadOnly = true;
        _hotkey.BackColor = SystemColors.Window;
        _hotkey.Text = _hotkeyText;
        _hotkey.ShortcutsEnabled = false;
        _hotkey.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            var text = GlobalHotkey.Describe(e.KeyData);
            if (text != null && (e.Control || e.Alt || e.Shift))
            {
                _hotkeyText = text;
                _hotkey.Text = text;
            }
        };
        _hotkey.PreviewKeyDown += (_, e) => e.IsInputKey = true; // Tab 等も拾う

        _startup.Text = "Windows の起動時に自動で起動する（トレイに格納した状態）";
        _startup.AutoSize = true;
        _startup.Checked = settings.StartWithWindows;

        _parallel.Minimum = 1;
        _parallel.Maximum = 8;
        _parallel.Value = Math.Clamp(settings.MaxParallelism, 1, 8);
        _parallel.Width = 60;

        AddRow(grid, "ホットキー", _hotkey);
        AddRow(grid, "", new Label { Text = "欄をクリックして、登録したい組み合わせを押してください", AutoSize = true, ForeColor = SystemColors.GrayText });
        AddRow(grid, "自動起動", _startup);
        AddRow(grid, "Git取得の並列数", _parallel);

        var links = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        links.Controls.Add(LinkButton("データフォルダを開く", AppPaths.DataDirectory));
        links.Controls.Add(LinkButton("ログフォルダを開く", AppPaths.LogDirectory));
        AddRow(grid, "", links);
        AddRow(grid, "", new Label { Text = $"バージョン {Application.ProductVersion}", AutoSize = true, ForeColor = SystemColors.GrayText });

        var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(LogicalToDeviceUnits(80), 0) };
        ok.Click += (_, _) => OnOk();
        var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(LogicalToDeviceUnits(80), 0) };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        grid.Controls.Add(buttons, 0, grid.RowCount++);
        grid.SetColumnSpan(buttons, 2);

        Controls.Add(grid);
        CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        var row = grid.RowCount++;
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) }, 0, row);
        if (control is TextBox)
            control.Dock = DockStyle.Fill;
        grid.Controls.Add(control, 1, row);
    }

    private Button LinkButton(string text, string folder)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(folder);
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(folder);
                using var _ = Process.Start(psi);
            }
            catch (Exception ex)
            {
                _log.Error("フォルダを開けませんでした", ex);
            }
        };
        return b;
    }

    private void OnOk()
    {
        if (!string.Equals(_hotkeyText, _settings.Hotkey, StringComparison.OrdinalIgnoreCase))
        {
            if (!_app.ApplyHotkey(_hotkeyText, showError: false))
            {
                MessageBox.Show(this, $"{_hotkeyText} は登録できませんでした（他のアプリが使用中の可能性があります）。\n元の {_settings.Hotkey} に戻します。",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _app.ApplyHotkey(_settings.Hotkey, showError: true);
                _hotkeyText = _settings.Hotkey;
                _hotkey.Text = _hotkeyText;
                return;
            }
            _settings.Hotkey = _hotkeyText;
        }

        if (_startup.Checked != _settings.StartWithWindows)
        {
            if (StartupRegistration.Apply(_startup.Checked, _log))
                _settings.StartWithWindows = _startup.Checked;
            else
                MessageBox.Show(this, "自動起動の設定を変更できませんでした。ログを確認してください。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _settings.MaxParallelism = (int)_parallel.Value;
        DialogResult = DialogResult.OK;
        Close();
    }
}
