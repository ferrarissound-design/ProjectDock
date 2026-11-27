using ProjectLauncher.Core.Logging;

namespace ProjectLauncher.App;

internal static class Program
{
    private const string MutexName = @"Local\ProjectLauncher.SingleInstance";
    private const string ShowEventName = @"Local\ProjectLauncher.ShowWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // 2つ目の起動：既存インスタンスにウィンドウ表示を頼んで終わる
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                ev.Set();
            }
            catch
            {
                // 既存側がまだイベントを作っていない瞬間。何もしない
            }
            return;
        }

        AppPaths.EnsureCreated();
        var log = new FileLogger(AppPaths.LogDirectory);
        log.Info($"=== ProjectLauncher {Application.ProductVersion} 起動 args=[{string.Join(" ", args)}] ===");

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            log.Error("UIスレッドの未処理例外", e.Exception);
            MessageBox.Show($"予期しないエラーが発生しました。\n{e.Exception.Message}\n\n詳細はログを確認してください:\n{AppPaths.LogDirectory}",
                "ProjectLauncher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error("未処理例外", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Error("未監視タスク例外", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var startInTray = args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
        using var context = new TrayAppContext(log, startInTray);

        var registration = ThreadPool.RegisterWaitForSingleObject(showEvent,
            (_, _) => context.RequestShowFromOtherInstance(), null, Timeout.Infinite, executeOnlyOnce: false);
        try
        {
            Application.Run(context);
        }
        finally
        {
            registration.Unregister(null);
            log.Info("=== 終了 ===");
        }
    }
}
