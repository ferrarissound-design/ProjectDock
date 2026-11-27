using System.Globalization;
using ProjectLauncher.Core.Detection;
using ProjectLauncher.Core.FileSystem;
using ProjectLauncher.Core.Git;
using ProjectLauncher.Core.Logging;
using ProjectLauncher.Core.Models;

namespace ProjectLauncher.Core.Services;

/// <summary>1プロジェクトの状態（存在・Git・最終更新）を取得する。</summary>
public sealed class ProjectStatusService
{
    private readonly IGitClient _git;
    private readonly IAppLogger _log;

    public ProjectStatusService(IGitClient git, IAppLogger log)
    {
        _git = git;
        _log = log;
    }

    public async Task<ProjectStatus> GetStatusAsync(ProjectEntry project, CancellationToken ct)
    {
        var status = new ProjectStatus { CheckedAt = DateTimeOffset.Now };
        var dir = project.Path;

        if (!Directory.Exists(dir))
        {
            status.Exists = false;
            return status;
        }
        status.Exists = true;

        if (!ProjectDetector.HasGit(dir))
        {
            status.GitState = GitState.NotARepository;
        }
        else if (!_git.IsAvailable)
        {
            status.GitState = GitState.GitNotFound;
        }
        else
        {
            await FillGitAsync(dir, status, ct).ConfigureAwait(false);
        }

        if (status.LastModified == null)
        {
            status.LastModified = await Task.Run(() => LastModifiedCalculator.GetLatestWriteTime(dir, ct), ct)
                .ConfigureAwait(false);
            status.LastModifiedSource = status.LastModified == null ? LastModifiedSource.None : LastModifiedSource.Files;
        }

        return status;
    }

    private async Task FillGitAsync(string dir, ProjectStatus status, CancellationToken ct)
    {
        try
        {
            var st = await _git.RunAsync(dir, new[] { "status", "--porcelain", "--branch", "--untracked-files=normal" }, ct)
                .ConfigureAwait(false);
            if (!st.Success)
            {
                status.GitState = GitState.Error;
                status.GitError = FirstLine(st.StdErr) ?? $"git status が終了コード {st.ExitCode} で失敗";
                _log.Warn($"git status failed in {dir}: {st.StdErr.Trim()}");
                return;
            }

            var parsed = GitStatusParser.Parse(st.StdOut);
            status.GitState = GitState.Ok;
            status.ChangeCount = parsed.ChangeCount;
            status.Branch = parsed.IsDetached ? "(detached)" : parsed.Branch;

            if (!parsed.HasNoCommits)
            {
                var log = await _git.RunAsync(dir, new[] { "log", "-1", "--format=%cI" }, ct).ConfigureAwait(false);
                if (log.Success && DateTimeOffset.TryParse(log.StdOut.Trim(), CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var committed))
                {
                    status.LastModified = committed.ToLocalTime();
                    status.LastModifiedSource = LastModifiedSource.GitCommit;
                }
            }

            var remote = await _git.RunAsync(dir, new[] { "remote", "get-url", "origin" }, ct).ConfigureAwait(false);
            if (remote.Success)
            {
                status.RemoteUrl = RedactCredentials(remote.StdOut.Trim());
                status.GitHubUrl = GitRemoteUrl.ToGitHubUrl(remote.StdOut.Trim());
            }
        }
        catch (GitNotFoundException)
        {
            status.GitState = GitState.GitNotFound;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            status.GitState = GitState.Error;
            status.GitError = ex.Message;
            _log.Error($"git 取得中の例外: {dir}", ex);
        }
    }

    /// <summary>https://user:token@host/... の資格情報をキャッシュやログに残さない。</summary>
    internal static string RedactCredentials(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo)
            && (uri.Scheme == "https" || uri.Scheme == "http"))
        {
            var b = new UriBuilder(uri) { UserName = "", Password = "" };
            return b.Uri.ToString();
        }
        return url;
    }

    private static string? FirstLine(string s)
    {
        var line = s.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return line;
    }
}
