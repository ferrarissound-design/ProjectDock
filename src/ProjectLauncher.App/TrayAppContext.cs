using ProjectLauncher.App.Forms;
using ProjectLauncher.App.Infrastructure;
using ProjectLauncher.Core.Git;
using ProjectLauncher.Core.Logging;
using ProjectLauncher.Core.Services;
using ProjectLauncher.Core.Storage;

namespace ProjectLauncher.App;

/// <summary>トレイ常駐の本体。メインウィンドウは閉じても破棄せず Hide するだけ。</summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private readonly IAppLogger _log;
    private readonly ProjectCatalog _catalog;
    private readonly MainForm _main;
    private readonly NotifyIcon _tray;
    private readonly GlobalHotkey _hotkey;
    private readonly Icon _trayIcon;

    public TrayAppContext(IAppLogger log, bool startInTray)
    {
        _log = log;
        _catalog = new ProjectCatalog(new ProjectStore(AppPaths.ProjectsFile, log), log);

        var git = new GitClient(log);
        var refresher = new StatusRefresher(new ProjectStatusService(git, log), log);
        var launcher = new ExternalLauncher(log);

        _main = new MainForm(_catalog, refresher, git, launcher, log, this);
        // 非表示のまま BeginInvoke できるようにハンドルを先に作る
        _ = _main.Handle;

        _trayIcon = AppIcon.GetSmall();
        _tray = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "ProjectLauncher",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowMain();
        };

        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => ShowMain(focusSearch: true);
        ApplyHotkey(_catalog.Settings.Hotkey, showError: true);

        // exe の場所が変わっていても自動起動のパスを最新にする
        if (_catalog.Settings.StartWithWindows)
            StartupRegistration.Apply(true, log);

        if (_catalog.CorruptBackupPath != null)
        {
            ShowBalloon("projects.json が壊れていたため空の状態で起動しました。\n退避先: " + _catalog.CorruptBackupPath, ToolTipIcon.Warning);
        }

        if (!startInTray)
            ShowMain();
    }

    public bool IsExiting { get; private set; }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("開く", null, (_, _) => ShowMain()) { Font = new Font(menu.Font, FontStyle.Bold) };
        menu.Items.Add(open);
        menu.Items.Add("全プロジェクトを再スキャン", null, (_, _) => { ShowMain(); _main.RefreshAll(force: true); });
        menu.Items.Add("設定", null, (_, _) => { ShowMain(); _main.OpenSettings(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Exit());
        return menu;
    }

    public void ShowMain(bool focusSearch = false) => _main.ShowAndActivate(focusSearch);

    /// <summary>別プロセス（2重起動）から呼ばれる。スレッドプール上なので UI スレッドに戻す。</summary>
    public void RequestShowFromOtherInstance()
    {
        try
        {
            _main.BeginInvoke(() => ShowMain(focusSearch: true));
        }
        catch (InvalidOperationException) { /* 終了処理中 */ }
    }

    public bool ApplyHotkey(string text, bool showError)
    {
        if (_hotkey.TryRegister(text, out var error))
        {
            _log.Info($"ホットキー登録: {text}");
            _tray.Text = $"ProjectLauncher ({text})";
            return true;
        }
        _log.Warn(error ?? "ホットキー登録失敗");
        if (showError)
            ShowBalloon(error ?? "ホットキーを登録できませんでした", ToolTipIcon.Warning);
        return false;
    }

    public void ShowBalloon(string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _tray.BalloonTipTitle = "ProjectLauncher";
        _tray.BalloonTipText = text;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(5000);
    }

    public void Exit()
    {
        if (IsExiting)
            return;
        IsExiting = true;
        _log.Info("終了要求");
        _main.PrepareExit();
        _catalog.Save();
        _hotkey.Dispose();
        _tray.Visible = false;
        _main.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _trayIcon.Dispose();
            _main.Dispose();
        }
        base.Dispose(disposing);
    }
}
