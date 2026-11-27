using Microsoft.Win32;
using ProjectLauncher.Core.Logging;

namespace ProjectLauncher.App.Infrastructure;

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run による自動起動。</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ProjectLauncher";

    private static string Command => $"\"{Environment.ProcessPath}\" --tray";

    /// <summary>有効/無効を反映する。有効時は毎回書き直す（exe を新しい版に置き換えて場所が変わっても追従するため）。</summary>
    public static bool Apply(bool enabled, IAppLogger log)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, Command, RegistryValueKind.String);
                log.Info($"自動起動を登録: {Command}");
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                log.Info("自動起動を解除");
            }
            return true;
        }
        catch (Exception ex)
        {
            log.Error("自動起動レジストリの更新に失敗", ex);
            return false;
        }
    }
}
