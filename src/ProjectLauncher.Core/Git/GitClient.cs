using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ProjectLauncher.Core.Logging;

namespace ProjectLauncher.Core.Git;

public sealed record GitCommandResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public interface IGitClient
{
    /// <summary>git.exe が使えるか（初回呼び出しで判定してキャッシュ）。</summary>
    bool IsAvailable { get; }

    Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, CancellationToken ct);
}

/// <summary>git.exe をプロセスとして呼ぶ。ネットワークを使うコマンドは呼ばない前提。</summary>
public sealed class GitClient : IGitClient
{
    private readonly string _gitExe;
    private readonly TimeSpan _timeout;
    private readonly IAppLogger _log;
    private readonly Lazy<bool> _available;

    public GitClient(IAppLogger log, string gitExe = "git", TimeSpan? timeout = null)
    {
        _log = log;
        _gitExe = gitExe;
        _timeout = timeout ?? TimeSpan.FromSeconds(15);
        _available = new Lazy<bool>(Probe, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => _available.Value;

    private bool Probe()
    {
        try
        {
            var r = RunAsync(Environment.CurrentDirectory, new[] { "--version" }, CancellationToken.None)
                .GetAwaiter().GetResult();
            _log.Info($"git probe: exit={r.ExitCode} {r.StdOut.Trim()}");
            return r.Success;
        }
        catch (Exception ex)
        {
            _log.Warn($"git が見つかりません: {ex.Message}");
            return false;
        }
    }

    public async Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_gitExe)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // status が index.lock を取りに行かないようにする（VS Code 等と競合しない）
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["LC_ALL"] = "C";
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        try
        {
            proc.Start();
        }
        catch (Win32Exception ex)
        {
            throw new GitNotFoundException(ex.Message, ex);
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            if (ct.IsCancellationRequested)
                throw;
            return new GitCommandResult(-1, "", $"git がタイムアウトしました（{_timeout.TotalSeconds:0}秒）");
        }

        string stdout, stderr;
        try
        {
            stdout = await stdoutTask.ConfigureAwait(false);
            stderr = await stderrTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        return new GitCommandResult(proc.ExitCode, stdout, stderr);
    }
}

public sealed class GitNotFoundException : Exception
{
    public GitNotFoundException(string message, Exception inner) : base(message, inner) { }
}
