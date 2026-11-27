using System.Diagnostics;
using ProjectLauncher.Core.Git;
using ProjectLauncher.Core.Logging;
using ProjectLauncher.Core.Models;
using ProjectLauncher.Core.Services;

namespace ProjectLauncher.Core.Tests;

/// <summary>
/// 本物の git を使う結合テスト。CI（windows-latest / ubuntu）には git があるので実行される。
/// git がない環境ではアサートせずに抜ける。
/// </summary>
public class RealGitTests
{
    private static bool GitInstalled()
        => new GitClient(NullLogger.Instance).IsAvailable;

    private static void Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-c", "user.name=test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false" }.Concat(args))
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(" ", args)} failed: {p.StandardError.ReadToEnd()}");
    }

    [Fact]
    public async Task Reads_branch_changes_commit_date_and_remote()
    {
        if (!GitInstalled())
            return;

        using var t = new TempDir();
        Git(t.Path, "init", "-q", "-b", "main");
        t.File("a.txt", "1");
        Git(t.Path, "add", ".");
        Git(t.Path, "commit", "-q", "-m", "first");
        Git(t.Path, "remote", "add", "origin", "git@github.com:me/real-repo.git");
        t.File("a.txt", "2");     // modified
        t.File("b.txt", "new");   // untracked

        var svc = new ProjectStatusService(new GitClient(NullLogger.Instance), NullLogger.Instance);
        var s = await svc.GetStatusAsync(new ProjectEntry { Path = t.Path }, default);

        Assert.Equal(GitState.Ok, s.GitState);
        Assert.Equal("main", s.Branch);
        Assert.Equal(2, s.ChangeCount);
        Assert.Equal(LastModifiedSource.GitCommit, s.LastModifiedSource);
        Assert.InRange(s.LastModified!.Value, DateTimeOffset.Now.AddMinutes(-5), DateTimeOffset.Now.AddMinutes(1));
        Assert.Equal("https://github.com/me/real-repo", s.GitHubUrl);
    }

    [Fact]
    public async Task Repository_without_commits_falls_back_to_file_times()
    {
        if (!GitInstalled())
            return;

        using var t = new TempDir();
        Git(t.Path, "init", "-q", "-b", "dev");
        var when = new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc);
        t.File("draft.txt", "x", when);

        var s = await new ProjectStatusService(new GitClient(NullLogger.Instance), NullLogger.Instance)
            .GetStatusAsync(new ProjectEntry { Path = t.Path }, default);

        Assert.Equal(GitState.Ok, s.GitState);
        Assert.Equal("dev", s.Branch);
        Assert.Equal(1, s.ChangeCount);
        Assert.Null(s.GitHubUrl);
        Assert.Equal(LastModifiedSource.Files, s.LastModifiedSource);
        Assert.Equal(when, s.LastModified!.Value.UtcDateTime);
    }

    [Fact]
    public void Missing_git_executable_is_reported_as_unavailable()
    {
        var client = new GitClient(NullLogger.Instance, gitExe: "git-does-not-exist-" + Guid.NewGuid().ToString("N"));
        Assert.False(client.IsAvailable);
    }
}
