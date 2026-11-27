using ProjectLauncher.Core.Git;

namespace ProjectLauncher.Core.Tests;

public class GitStatusParserTests
{
    [Fact]
    public void Clean_repository()
    {
        var r = GitStatusParser.Parse("## main...origin/main\n");
        Assert.Equal("main", r.Branch);
        Assert.Equal(0, r.ChangeCount);
        Assert.False(r.IsDetached);
        Assert.False(r.HasNoCommits);
    }

    [Fact]
    public void Counts_each_changed_line_and_ignores_header()
    {
        var output = "## feature/x...origin/feature/x [ahead 2, behind 1]\n" +
                     " M src/a.cs\n" +
                     "M  src/b.cs\n" +
                     "?? new file.txt\n" +
                     "R  old.cs -> new.cs\n" +
                     "UU conflict.cs\n";
        var r = GitStatusParser.Parse(output);
        Assert.Equal("feature/x", r.Branch);
        Assert.Equal(5, r.ChangeCount);
    }

    [Fact]
    public void Handles_crlf_and_trailing_blank_lines()
    {
        var r = GitStatusParser.Parse("## main\r\n M a.txt\r\n?? b.txt\r\n\r\n");
        Assert.Equal("main", r.Branch);
        Assert.Equal(2, r.ChangeCount);
    }

    [Fact]
    public void Branch_without_upstream()
    {
        Assert.Equal("dev", GitStatusParser.Parse("## dev\n").Branch);
    }

    [Fact]
    public void Branch_with_gone_upstream()
    {
        Assert.Equal("old", GitStatusParser.Parse("## old...origin/old [gone]\n").Branch);
    }

    [Fact]
    public void Branch_without_upstream_but_with_bracket()
    {
        Assert.Equal("topic", GitStatusParser.Parse("## topic [ahead 1]\n").Branch);
    }

    [Theory]
    [InlineData("## No commits yet on main\n?? README.md\n", "main", 1)]
    [InlineData("## Initial commit on master\n", "master", 0)]
    public void No_commits_yet(string output, string branch, int count)
    {
        var r = GitStatusParser.Parse(output);
        Assert.True(r.HasNoCommits);
        Assert.Equal(branch, r.Branch);
        Assert.Equal(count, r.ChangeCount);
    }

    [Fact]
    public void Detached_head()
    {
        var r = GitStatusParser.Parse("## HEAD (no branch)\n M x\n");
        Assert.True(r.IsDetached);
        Assert.Null(r.Branch);
        Assert.Equal(1, r.ChangeCount);
    }

    [Fact]
    public void Output_without_branch_header_still_counts_lines()
    {
        var r = GitStatusParser.Parse(" M a\n M b\n?? c\n");
        Assert.Null(r.Branch);
        Assert.Equal(3, r.ChangeCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_output(string? output)
    {
        var r = GitStatusParser.Parse(output);
        Assert.Equal(0, r.ChangeCount);
        Assert.Null(r.Branch);
    }

    [Fact]
    public void Japanese_branch_name()
    {
        Assert.Equal("機能/検索", GitStatusParser.Parse("## 機能/検索...origin/機能/検索\n").Branch);
    }
}
