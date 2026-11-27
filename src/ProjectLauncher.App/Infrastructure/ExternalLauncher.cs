using System.Diagnostics;
using ProjectLauncher.Core.Logging;
using ProjectLauncher.Core.Models;
using ProjectLauncher.Core.Services;

namespace ProjectLauncher.App.Infrastructure;

/// <summary>エクスプローラー / VS Code / ブラウザ / ターミナル / Unity を起動する。</summary>
internal sealed class ExternalLauncher
{
    private readonly IAppLogger _log;
    private readonly ToolLocator _locator = new();
    private readonly Lazy<string?> _vsCode;
    private readonly Lazy<string?> _wt;

    public ExternalLauncher(IAppLogger log)
    {
        _log = log;
        _vsCode = new Lazy<string?>(() => _locator.FindVsCode());
        _wt = new Lazy<string?>(() => _locator.FindWindowsTerminal());
    }

    public string? VsCodePath => _vsCode.Value;
    public string? WindowsTerminalPath => _wt.Value;

    public void OpenExplorer(string path)
    {
        var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        psi.ArgumentList.Add(path);
        Start(psi, "エクスプローラー");
    }

    public void OpenVsCode(string path)
    {
        var exe = VsCodePath;
        if (exe == null)
        {
            Fail("VS Code が見つかりません。", null);
            return;
        }
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true, // code.cmd の場合にコンソールを出さない
            WorkingDirectory = path,
        };
        psi.ArgumentList.Add(path);
        Start(psi, "VS Code");
    }

    public void OpenUrl(string? url)
    {
        if (!DisplayFormat.IsHttpUrl(url))
        {
            Fail($"http/https の URL ではないため開きません: {url}", null);
            return;
        }
        Start(new ProcessStartInfo(url!.Trim()) { UseShellExecute = true }, "ブラウザ");
    }

    public void OpenTerminal(string path)
    {
        var wt = WindowsTerminalPath;
        if (wt != null)
        {
            var psi = new ProcessStartInfo(wt) { UseShellExecute = false };
            psi.ArgumentList.Add("-d");
            // wt は ; をコマンド区切りとして扱うのでエスケープする
            psi.ArgumentList.Add(path.Replace(";", "\\;"));
            if (Start(psi, "Windows Terminal", showError: false))
                return;
        }

        var cmd = new ProcessStartInfo("cmd.exe") { UseShellExecute = true, WorkingDirectory = path };
        Start(cmd, "コマンドプロンプト");
    }

    /// <summary>
    /// ProjectVersion.txt のバージョンに一致する、Unity Hub がインストールしたエディタで直接開く。
    /// 見つからなければ Unity Hub 自体を起動する。
    /// </summary>
    public void OpenUnity(ProjectEntry project)
    {
        var version = UnityProjectInfo.ReadEditorVersion(project.Path);
        if (version != null)
        {
            var editor = _locator.FindUnityEditor(version, SecondaryEditorRoots());
            if (editor != null)
            {
                var psi = new ProcessStartInfo(editor) { UseShellExecute = false };
                psi.ArgumentList.Add("-projectPath");
                psi.ArgumentList.Add(project.Path);
                Start(psi, $"Unity {version}");
                return;
            }
        }

        var hub = _locator.FindUnityHub();
        if (hub != null)
        {
            Start(new ProcessStartInfo(hub) { UseShellExecute = false }, "Unity Hub");
            MessageBox.Show(
                version == null
                    ? "ProjectVersion.txt からエディタのバージョンを読めなかったので、Unity Hub を開きました。"
                    : $"Unity {version} が見つからなかったので、Unity Hub を開きました。",
                "ProjectLauncher", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Fail(version == null
            ? "Unity エディタのバージョンが分からず、Unity Hub も見つかりません。"
            : $"Unity {version} のエディタも Unity Hub も見つかりません。", null);
    }

    private static IEnumerable<string> SecondaryEditorRoots()
    {
        var f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub", "secondaryInstallPath.json");
        string? p = null;
        try
        {
            if (File.Exists(f))
                p = UnityProjectInfo.ParseSecondaryInstallPath(File.ReadAllText(f));
        }
        catch { /* ignore */ }
        return p == null ? Array.Empty<string>() : new[] { p };
    }

    private bool Start(ProcessStartInfo psi, string what, bool showError = true)
    {
        try
        {
            _log.Info($"起動: {what} {psi.FileName} {string.Join(" ", psi.ArgumentList)} (cwd={psi.WorkingDirectory})");
            using var _ = Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            if (showError)
                Fail($"{what} を起動できませんでした。", ex);
            else
                _log.Warn($"{what} の起動に失敗: {ex.Message}");
            return false;
        }
    }

    private void Fail(string message, Exception? ex)
    {
        _log.Error(message, ex);
        MessageBox.Show(ex == null ? message : $"{message}\n{ex.Message}", "ProjectLauncher",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
