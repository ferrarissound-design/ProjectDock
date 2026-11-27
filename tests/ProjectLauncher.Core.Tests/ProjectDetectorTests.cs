using ProjectLauncher.Core.Detection;
using ProjectLauncher.Core.Models;

namespace ProjectLauncher.Core.Tests;

public class ProjectDetectorTests
{
    [Fact]
    public void Unity_requires_Assets_and_ProjectSettings()
    {
        using var t = new TempDir();
        t.Dir("Assets");
        t.Dir("ProjectSettings");
        Assert.Equal(ProjectKind.Unity, ProjectDetector.DetectKind(t.Path));
        Assert.True(ProjectDetector.IsCandidate(t.Path));
    }

    [Fact]
    public void Only_Assets_is_not_Unity()
    {
        using var t = new TempDir();
        t.Dir("Assets");
        Assert.Equal(ProjectKind.Other, ProjectDetector.DetectKind(t.Path));
        Assert.False(ProjectDetector.IsCandidate(t.Path));
    }

    [Fact]
    public void Unity_wins_over_generated_sln_and_csproj()
    {
        using var t = new TempDir();
        t.Dir("Assets");
        t.Dir("ProjectSettings");
        t.File("MyGame.sln");
        t.File("Assembly-CSharp.csproj");
        Assert.Equal(ProjectKind.Unity, ProjectDetector.DetectKind(t.Path));
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("index.html")]
    public void Web_by_package_json_or_index_html(string file)
    {
        using var t = new TempDir();
        t.File(file, "{}");
        Assert.Equal(ProjectKind.Web, ProjectDetector.DetectKind(t.Path));
        Assert.True(ProjectDetector.IsCandidate(t.Path));
    }

    [Theory]
    [InlineData("Tool.sln")]
    [InlineData("Tool.csproj")]
    [InlineData("Native.vcxproj")]
    public void WindowsApp_by_solution_or_project(string file)
    {
        using var t = new TempDir();
        t.File(file);
        Assert.Equal(ProjectKind.WindowsApp, ProjectDetector.DetectKind(t.Path));
    }

    [Fact]
    public void WindowsApp_wins_over_index_html()
    {
        // WebView2 アプリなどで index.html を同梱しているケース
        using var t = new TempDir();
        t.File("App.csproj");
        t.File("index.html");
        Assert.Equal(ProjectKind.WindowsApp, ProjectDetector.DetectKind(t.Path));
    }

    [Fact]
    public void Git_only_folder_is_candidate_with_kind_Other()
    {
        using var t = new TempDir();
        t.Dir(".git");
        t.File("notes.md");
        Assert.True(ProjectDetector.HasGit(t.Path));
        Assert.True(ProjectDetector.IsCandidate(t.Path));
        Assert.Equal(ProjectKind.Other, ProjectDetector.DetectKind(t.Path));
    }

    [Fact]
    public void Git_file_for_worktree_counts_as_git()
    {
        using var t = new TempDir();
        t.File(".git", "gitdir: ../main/.git/worktrees/x");
        Assert.True(ProjectDetector.HasGit(t.Path));
    }

    [Fact]
    public void Empty_or_missing_folder()
    {
        using var t = new TempDir();
        Assert.False(ProjectDetector.IsCandidate(t.Path));
        Assert.Equal(ProjectKind.Other, ProjectDetector.DetectKind(t.Path));
        var missing = Path.Combine(t.Path, "nope");
        Assert.False(ProjectDetector.IsCandidate(missing));
        Assert.Equal(ProjectKind.Other, ProjectDetector.DetectKind(missing));
    }

    [Fact]
    public void Scanner_finds_depth_1_and_2_and_skips_excluded()
    {
        using var t = new TempDir();
        // depth 1
        t.Dir("UnityHorror", "Assets");
        t.Dir("UnityHorror", "ProjectSettings");
        t.File(Path.Combine("ThreeGame", "package.json"), "{}");
        // depth 2（グループ用フォルダの下）
        t.Dir("tools", "Clipper", ".git");
        // depth 3 は見つからない
        t.File(Path.Combine("deep", "a", "b", "index.html"));
        // 除外フォルダ・隠しフォルダの中は見ない
        t.File(Path.Combine("node_modules", "pkg", "package.json"), "{}");
        t.File(Path.Combine(".cache", "x", "index.html"));
        // 候補フォルダの内側には入らない
        t.File(Path.Combine("ThreeGame", "sub", "index.html"));

        var registered = new[] { Path.Combine(t.Path, "ThreeGame") };
        var found = FolderScanner.Scan(t.Path, 2, registered);

        var names = found.Select(f => f.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Clipper", "ThreeGame", "UnityHorror" }, names);
        Assert.True(found.Single(f => f.Name == "ThreeGame").AlreadyRegistered);
        Assert.Equal(ProjectKind.Unity, found.Single(f => f.Name == "UnityHorror").Kind);
        Assert.True(found.Single(f => f.Name == "Clipper").HasGit);

        var depth1 = FolderScanner.Scan(t.Path, 1, Array.Empty<string>());
        Assert.DoesNotContain(depth1, f => f.Name == "Clipper");
    }
}
