using ProjectLauncher.Core.Git;
using ProjectLauncher.Core.Logging;
using ProjectLauncher.Core.Models;
using ProjectLauncher.Core.Services;

namespace ProjectLauncher.Core.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+Alt+P", true, true, false, false, "P")]
    [InlineData("ctrl + shift + space", true, false, true, false, "space")]
    [InlineData("Win+Alt+F12", false, true, false, true, "F12")]
    [InlineData("Control+p", true, false, false, false, "P")]
    public void Parses(string text, bool ctrl, bool alt, bool shift, bool win, string key)
    {
        Assert.True(HotkeyParser.TryParse(text, out var h));
        Assert.Equal(new HotkeyDefinition(ctrl, alt, shift, win, key), h);
    }

    [Theory]
    [InlineData("")]
    [InlineData("P")]           // 修飾キーなし
    [InlineData("Ctrl+Alt")]    // キーなし
    [InlineData("Ctrl+A+B")]    // キー2つ
    [InlineData("Ctrl++")]
    public void Rejects(string text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _));
    }

    [Fact]
    public void ToString_round_trips()
    {
        HotkeyParser.TryParse("alt+ctrl+p", out var h);
        Assert.Equal("Ctrl+Alt+P", h!.ToString());
    }
}

public class ProjectFilterTests
{
    private static ProjectEntry P(string name, int changes = 0, int daysAgo = 0, string? memo = null, params string[] tags) => new()
    {
        Name = name,
        Path = "/" + name,
        Memo = memo,
        Tags = tags.ToList(),
        CachedStatus = new ProjectStatus
        {
            Exists = true,
            GitState = GitState.Ok,
            ChangeCount = changes,
            LastModified = new DateTimeOffset(2026, 11, 27, 0, 0, 0, TimeSpan.Zero).AddDays(-daysAgo),
        },
    };

    private readonly List<ProjectEntry> _all = new()
    {
        P("Alpha", changes: 0, daysAgo: 10, memo: "three.js の実験", "web"),
        P("Bravo", changes: 5, daysAgo: 30, memo: null, "unity", "horror"),
        P("Charlie", changes: 1, daysAgo: 1, memo: "トレイ常駐ツール", "tool"),
        new() { Name = "Delta", Path = "/Delta" }, // 状態未取得
    };

    [Fact]
    public void Default_sort_is_last_modified_desc_with_unknown_last()
    {
        var r = ProjectFilter.Apply(_all, null, SortMode.LastModifiedDesc, false);
        Assert.Equal(new[] { "Charlie", "Alpha", "Bravo", "Delta" }, r.Select(p => p.Name));
    }

    [Fact]
    public void Sort_by_name()
    {
        var r = ProjectFilter.Apply(_all, "", SortMode.Name, false);
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Delta" }, r.Select(p => p.Name));
    }

    [Fact]
    public void Dirty_first()
    {
        var r = ProjectFilter.Apply(_all, "", SortMode.DirtyFirst, false);
        Assert.Equal(new[] { "Bravo", "Charlie", "Alpha", "Delta" }, r.Select(p => p.Name));
    }

    [Fact]
    public void Only_dirty()
    {
        var r = ProjectFilter.Apply(_all, "", SortMode.Name, true);
        Assert.Equal(new[] { "Bravo", "Charlie" }, r.Select(p => p.Name));
    }

    [Theory]
    [InlineData("alpha", "Alpha")]     // 名前・大文字小文字無視
    [InlineData("HORROR", "Bravo")]    // タグ
    [InlineData("常駐", "Charlie")]     // メモ
    [InlineData("THREE.JS", "Alpha")]
    [InlineData("unity horror", "Bravo")] // AND
    public void Search(string q, string expected)
    {
        var r = ProjectFilter.Apply(_all, q, SortMode.Name, false);
        Assert.Equal(expected, Assert.Single(r).Name);
    }

    [Fact]
    public void Search_with_no_match()
    {
        Assert.Empty(ProjectFilter.Apply(_all, "unity tool", SortMode.Name, false));
    }

    [Fact]
    public void Parse_tags()
    {
        Assert.Equal(new[] { "unity", "ホラー", "3d" }, ProjectFilter.ParseTags(" unity, ホラー、3d ,,Unity"));
        Assert.Empty(ProjectFilter.ParseTags(null));
    }
}

public class ToolLocatorTests
{
    private static string J(params string[] p) => Path.Combine(p);

    [Fact]
    public void VsCode_from_PATH_prefers_Code_exe_next_to_bin()
    {
        var root = J("C:", "Users", "me", "AppData", "Local", "Programs", "Microsoft VS Code");
        var files = new HashSet<string> { J(root, "bin", "code.cmd"), J(root, "Code.exe") };
        var env = new Dictionary<string, string?> { ["PATH"] = J("C:", "Windows") + ";" + J(root, "bin") };
        var loc = new ToolLocator(files.Contains, k => env.GetValueOrDefault(k));
        Assert.Equal(J(root, "Code.exe"), loc.FindVsCode());
    }

    [Fact]
    public void VsCode_falls_back_to_LocalAppData()
    {
        var local = J("C:", "Users", "me", "AppData", "Local");
        var exe = J(local, "Programs", "Microsoft VS Code", "Code.exe");
        var env = new Dictionary<string, string?> { ["PATH"] = J("C:", "Windows"), ["LOCALAPPDATA"] = local };
        var loc = new ToolLocator(new HashSet<string> { exe }.Contains, k => env.GetValueOrDefault(k));
        Assert.Equal(exe, loc.FindVsCode());
    }

    [Fact]
    public void VsCode_not_found()
    {
        var loc = new ToolLocator(_ => false, _ => null);
        Assert.Null(loc.FindVsCode());
        Assert.Null(loc.FindWindowsTerminal());
    }

    [Fact]
    public void Windows_terminal_in_WindowsApps()
    {
        var local = J("C:", "Users", "me", "AppData", "Local");
        var wt = J(local, "Microsoft", "WindowsApps", "wt.exe");
        var env = new Dictionary<string, string?> { ["LOCALAPPDATA"] = local };
        var loc = new ToolLocator(new HashSet<string> { wt }.Contains, k => env.GetValueOrDefault(k));
        Assert.Equal(wt, loc.FindWindowsTerminal());
    }

    [Fact]
    public void Unity_editor_in_secondary_root()
    {
        var secondary = J("D:", "Unity");
        var exe = J(secondary, "2022.3.10f1", "Editor", "Unity.exe");
        var loc = new ToolLocator(new HashSet<string> { exe }.Contains, k => k == "ProgramFiles" ? J("C:", "Program Files") : null);
        Assert.Equal(exe, loc.FindUnityEditor("2022.3.10f1", new[] { secondary }));
        Assert.Null(loc.FindUnityEditor("6000.0.1f1", new[] { secondary }));
    }
}

public class UnityProjectInfoTests
{
    [Fact]
    public void Parses_editor_version()
    {
        var text = "m_EditorVersion: 2022.3.10f1\nm_EditorVersionWithRevision: 2022.3.10f1 (ff3792e53c62)\n";
        Assert.Equal("2022.3.10f1", UnityProjectInfo.ParseEditorVersion(text));
        Assert.Null(UnityProjectInfo.ParseEditorVersion("garbage"));
    }

    [Fact]
    public void Parses_secondary_install_path()
    {
        Assert.Equal(@"D:\Unity", UnityProjectInfo.ParseSecondaryInstallPath("\"D:\\\\Unity\""));
        Assert.Null(UnityProjectInfo.ParseSecondaryInstallPath("\"\""));
        Assert.Null(UnityProjectInfo.ParseSecondaryInstallPath("{bad"));
    }
}

public class DisplayFormatTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.FromHours(9));

    [Theory]
    [InlineData(0, "たった今")]
    [InlineData(5, "5分前")]
    [InlineData(180, "3時間前")]
    [InlineData(60 * 24 * 3, "3日前")]
    [InlineData(60 * 24 * 90, "3か月前")]
    [InlineData(60 * 24 * 800, "2年前")]
    public void Relative(int minutesAgo, string expected)
    {
        Assert.Equal(expected, DisplayFormat.Relative(Now.AddMinutes(-minutesAgo), Now));
    }

    [Fact]
    public void Git_column()
    {
        Assert.Equal("見つかりません", DisplayFormat.GitColumn(new ProjectStatus { Exists = false }));
        Assert.Equal("Git未管理", DisplayFormat.GitColumn(new ProjectStatus { Exists = true, GitState = GitState.NotARepository }));
        Assert.Equal("git未検出", DisplayFormat.GitColumn(new ProjectStatus { Exists = true, GitState = GitState.GitNotFound }));
        Assert.Equal("未コミット 2件", DisplayFormat.GitColumn(new ProjectStatus { Exists = true, GitState = GitState.Ok, ChangeCount = 2 }));
        Assert.Equal("クリーン", DisplayFormat.GitColumn(new ProjectStatus { Exists = true, GitState = GitState.Ok }));
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("C:\\Windows", false)]
    [InlineData("", false)]
    public void Http_url_check(string url, bool expected)
    {
        Assert.Equal(expected, DisplayFormat.IsHttpUrl(url));
    }
}

/// <summary>実際の git を使わず、IGitClient のフェイクで状態取得ロジックを確かめる。</summary>
public class ProjectStatusServiceTests
{
    private sealed class FakeGit : IGitClient
    {
        public bool IsAvailable { get; set; } = true;
        public Dictionary<string, GitCommandResult> Responses { get; } = new();
        public int MaxConcurrent;
        private int _current;

        public async Task<GitCommandResult> RunAsync(string workingDirectory, IReadOnlyList<string> args, CancellationToken ct)
        {
            var now = Interlocked.Increment(ref _current);
            InterlockedMax(ref MaxConcurrent, now);
            await Task.Delay(20, ct);
            Interlocked.Decrement(ref _current);
            return Responses.TryGetValue(args[0], out var r) ? r : new GitCommandResult(1, "", "unexpected");
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int cur;
            while ((cur = target) < value && Interlocked.CompareExchange(ref target, value, cur) != cur) { }
        }
    }

    [Fact]
    public async Task Missing_folder()
    {
        var svc = new ProjectStatusService(new FakeGit(), NullLogger.Instance);
        var s = await svc.GetStatusAsync(new ProjectEntry { Path = "/definitely/missing/" + Guid.NewGuid() }, default);
        Assert.False(s.Exists);
        Assert.Equal("見つかりません", DisplayFormat.GitColumn(s));
    }

    [Fact]
    public async Task Non_git_folder_uses_file_times()
    {
        using var t = new TempDir();
        var when = new DateTime(2025, 5, 5, 0, 0, 0, DateTimeKind.Utc);
        t.File("index.html", "", when);
        var svc = new ProjectStatusService(new FakeGit(), NullLogger.Instance);
        var s = await svc.GetStatusAsync(new ProjectEntry { Path = t.Path }, default);
        Assert.Equal(GitState.NotARepository, s.GitState);
        Assert.Equal(LastModifiedSource.Files, s.LastModifiedSource);
        Assert.Equal(when, s.LastModified!.Value.UtcDateTime);
    }

    [Fact]
    public async Task Git_not_installed_does_not_throw()
    {
        using var t = new TempDir();
        t.Dir(".git");
        t.File("a.txt");
        var svc = new ProjectStatusService(new FakeGit { IsAvailable = false }, NullLogger.Instance);
        var s = await svc.GetStatusAsync(new ProjectEntry { Path = t.Path }, default);
        Assert.Equal(GitState.GitNotFound, s.GitState);
        Assert.Equal("git未検出", DisplayFormat.GitColumn(s));
        Assert.Equal(LastModifiedSource.Files, s.LastModifiedSource);
    }

    [Fact]
    public async Task Git_repo_status()
    {
        using var t = new TempDir();
        t.Dir(".git");
        var git = new FakeGit();
        git.Responses["status"] = new(0, "## main...origin/main\n M a.cs\n?? b.cs\n", "");
        git.Responses["log"] = new(0, "2026-11-20T21:05:00+09:00\n", "");
        git.Responses["remote"] = new(0, "https://user:tok@github.com/me/horror.git\n", "");
        var svc = new ProjectStatusService(git, NullLogger.Instance);

        var s = await svc.GetStatusAsync(new ProjectEntry { Path = t.Path }, default);

        Assert.Equal(GitState.Ok, s.GitState);
        Assert.Equal("main", s.Branch);
        Assert.Equal(2, s.ChangeCount);
        Assert.True(s.HasUncommittedChanges);
        Assert.Equal(LastModifiedSource.GitCommit, s.LastModifiedSource);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 12, 5, 0, TimeSpan.Zero), s.LastModified);
        Assert.Equal("https://github.com/me/horror", s.GitHubUrl);
        Assert.DoesNotContain("tok", s.RemoteUrl); // トークンをキャッシュに残さない
    }

    [Fact]
    public async Task Git_error_is_reported()
    {
        using var t = new TempDir();
        t.Dir(".git");
        var git = new FakeGit();
        git.Responses["status"] = new(128, "", "fatal: detected dubious ownership in repository\n");
        var s = await new ProjectStatusService(git, NullLogger.Instance).GetStatusAsync(new ProjectEntry { Path = t.Path }, default);
        Assert.Equal(GitState.Error, s.GitState);
        Assert.Contains("dubious ownership", s.GitError);
    }

    [Fact]
    public async Task Refresher_limits_parallelism()
    {
        using var t = new TempDir();
        var projects = Enumerable.Range(0, 12).Select(i =>
        {
            var d = t.Dir("p" + i, ".git");
            return new ProjectEntry { Path = Path.GetDirectoryName(d)! };
        }).ToList();
        var git = new FakeGit();
        git.Responses["status"] = new(0, "## main\n", "");
        git.Responses["log"] = new(0, "2026-11-20T21:05:00+09:00", "");
        git.Responses["remote"] = new(1, "", "no remote");
        var refresher = new StatusRefresher(new ProjectStatusService(git, NullLogger.Instance), NullLogger.Instance);

        var done = 0;
        await refresher.RefreshAsync(projects, 4, (_, _) => Interlocked.Increment(ref done), default);

        Assert.Equal(12, done);
        Assert.InRange(git.MaxConcurrent, 1, 4);
    }
}
