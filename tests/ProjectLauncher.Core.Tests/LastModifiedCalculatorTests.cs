using ProjectLauncher.Core.FileSystem;

namespace ProjectLauncher.Core.Tests;

public class LastModifiedCalculatorTests
{
    private static readonly DateTime Old = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mid = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime New = new(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Returns_max_of_files_in_nested_folders()
    {
        using var t = new TempDir();
        t.File("a.txt", "", Old);
        t.File(Path.Combine("Assets", "Scripts", "Player.cs"), "", Mid);
        Assert.Equal(Mid, LastModifiedCalculator.GetLatestWriteTime(t.Path)!.Value.UtcDateTime);
    }

    [Theory]
    [InlineData("Library")]
    [InlineData("Temp")]
    [InlineData("obj")]
    [InlineData("bin")]
    [InlineData("node_modules")]
    [InlineData(".git")]
    [InlineData("Build")]
    [InlineData("library")] // 大文字小文字は区別しない
    public void Ignores_excluded_folders(string excluded)
    {
        using var t = new TempDir();
        t.File("main.js", "", Old);
        t.File(Path.Combine(excluded, "huge", "cache.bin"), "", New);
        Assert.Equal(Old, LastModifiedCalculator.GetLatestWriteTime(t.Path)!.Value.UtcDateTime);
    }

    [Fact]
    public void Excludes_at_any_depth()
    {
        using var t = new TempDir();
        t.File(Path.Combine("src", "App", "Program.cs"), "", Mid);
        t.File(Path.Combine("src", "App", "obj", "Debug", "x.dll"), "", New);
        t.File(Path.Combine("packages", "web", "node_modules", "a", "index.js"), "", New);
        Assert.Equal(Mid, LastModifiedCalculator.GetLatestWriteTime(t.Path)!.Value.UtcDateTime);
    }

    [Fact]
    public void Does_not_exclude_folders_that_only_contain_the_name()
    {
        using var t = new TempDir();
        t.File(Path.Combine("BuildScripts", "make.ps1"), "", New);
        t.File(Path.Combine("binary-assets", "x.png"), "", Mid);
        Assert.Equal(New, LastModifiedCalculator.GetLatestWriteTime(t.Path)!.Value.UtcDateTime);
    }

    [Fact]
    public void Only_excluded_content_returns_null()
    {
        using var t = new TempDir();
        t.File(Path.Combine("Library", "x"), "", New);
        Assert.Null(LastModifiedCalculator.GetLatestWriteTime(t.Path));
    }

    [Fact]
    public void Missing_folder_returns_null()
    {
        Assert.Null(LastModifiedCalculator.GetLatestWriteTime(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid())));
    }

    [Fact]
    public void Stops_at_max_files_without_throwing()
    {
        using var t = new TempDir();
        for (var i = 0; i < 10; i++)
            t.File($"f{i}.txt", "", Old);
        Assert.NotNull(LastModifiedCalculator.GetLatestWriteTime(t.Path, maxFiles: 3));
    }

    [Fact]
    public void Honors_cancellation()
    {
        using var t = new TempDir();
        t.File("a.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => LastModifiedCalculator.GetLatestWriteTime(t.Path, cts.Token));
    }
}
