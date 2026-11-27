using ProjectLauncher.Core.Git;

namespace ProjectLauncher.Core.Tests;

public class GitRemoteUrlTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo", "https://github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo/", "https://github.com/owner/repo")]
    [InlineData("http://github.com/owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("https://www.github.com/owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("https://user:ghp_secret@github.com/owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("https://GitHub.com/Owner/My-Repo.GIT", "https://github.com/Owner/My-Repo")]
    [InlineData("  https://github.com/owner/repo.git\r\n", "https://github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo", "https://github.com/owner/repo")]
    [InlineData("github.com:owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("git@github.com-work:owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("ssh://git@github.com/owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("ssh://git@ssh.github.com:443/owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("git://github.com/owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo.name.git", "https://github.com/owner/repo.name")]
    public void Converts_GitHub_remotes(string remote, string expected)
    {
        Assert.Equal(expected, GitRemoteUrl.ToGitHubUrl(remote));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://gitlab.com/owner/repo.git")]
    [InlineData("git@bitbucket.org:owner/repo.git")]
    [InlineData("https://github.com/owner")]
    [InlineData("https://github.com/owner/repo/tree/main")]
    [InlineData("C:\\repos\\bare.git")]
    [InlineData("/srv/git/repo.git")]
    [InlineData("file:///C:/repos/repo.git")]
    [InlineData("https://notgithub.com/owner/repo.git")]
    public void Returns_null_for_non_GitHub_or_invalid(string? remote)
    {
        Assert.Null(GitRemoteUrl.ToGitHubUrl(remote));
    }
}
