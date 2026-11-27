namespace ProjectLauncher.Core.Services;

/// <summary>外部ツール（VS Code / Windows Terminal / Unity エディタ）の場所を探す。File.Exists を差し替えてテストできる。</summary>
public sealed class ToolLocator
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string?> _env;

    public ToolLocator(Func<string, bool>? fileExists = null, Func<string, string?>? env = null)
    {
        _fileExists = fileExists ?? File.Exists;
        _env = env ?? Environment.GetEnvironmentVariable;
    }

    /// <summary>PATH 上で最初に見つかったファイルのフルパス。</summary>
    public string? FindOnPath(params string[] fileNames)
    {
        var path = _env("PATH") ?? "";
        foreach (var dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var d = dir.Trim('"');
            foreach (var name in fileNames)
            {
                string candidate;
                try { candidate = Path.Combine(d, name); }
                catch (ArgumentException) { continue; }
                if (_fileExists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// VS Code の実行ファイル。
    /// 1) PATH の code.cmd（同じインストールの Code.exe があればそちらを使い、コンソール窓のチラつきを避ける）
    /// 2) %LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe（ユーザーインストール）
    /// 3) %ProgramFiles%\Microsoft VS Code\Code.exe（システムインストール）
    /// </summary>
    public string? FindVsCode()
    {
        var onPath = FindOnPath("code.cmd", "code.exe", "code");
        if (onPath != null)
        {
            var binDir = Path.GetDirectoryName(onPath);
            if (binDir != null && string.Equals(Path.GetFileName(binDir), "bin", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(binDir);
                if (parent != null)
                {
                    var exe = Path.Combine(parent, "Code.exe");
                    if (_fileExists(exe))
                        return exe;
                }
            }
            return onPath;
        }

        var local = _env("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local))
        {
            var p = Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe");
            if (_fileExists(p))
                return p;
        }

        var pf = _env("ProgramFiles");
        if (!string.IsNullOrEmpty(pf))
        {
            var p = Path.Combine(pf, "Microsoft VS Code", "Code.exe");
            if (_fileExists(p))
                return p;
        }
        return null;
    }

    /// <summary>Windows Terminal（wt.exe はアプリ実行エイリアスとして WindowsApps に置かれる）。</summary>
    public string? FindWindowsTerminal()
    {
        var onPath = FindOnPath("wt.exe");
        if (onPath != null)
            return onPath;
        var local = _env("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local))
        {
            var p = Path.Combine(local, "Microsoft", "WindowsApps", "wt.exe");
            if (_fileExists(p))
                return p;
        }
        return null;
    }

    /// <summary>Unity Hub が入れたエディタ（既定 or Hub のセカンダリ設定先）から指定バージョンの Unity.exe を探す。</summary>
    public string? FindUnityEditor(string version, IEnumerable<string> extraEditorRoots)
    {
        var roots = new List<string>();
        var pf = _env("ProgramFiles");
        if (!string.IsNullOrEmpty(pf))
            roots.Add(Path.Combine(pf, "Unity", "Hub", "Editor"));
        roots.AddRange(extraEditorRoots.Where(r => !string.IsNullOrWhiteSpace(r)));

        foreach (var root in roots)
        {
            var p = Path.Combine(root, version, "Editor", "Unity.exe");
            if (_fileExists(p))
                return p;
        }
        return null;
    }

    public string? FindUnityHub()
    {
        var pf = _env("ProgramFiles");
        if (string.IsNullOrEmpty(pf))
            return null;
        var p = Path.Combine(pf, "Unity Hub", "Unity Hub.exe");
        return _fileExists(p) ? p : null;
    }
}
