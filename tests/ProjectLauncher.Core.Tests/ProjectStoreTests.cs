using ProjectLauncher.Core.Models;
using ProjectLauncher.Core.Storage;

namespace ProjectLauncher.Core.Tests;

public class ProjectStoreTests
{
    [Fact]
    public void Missing_file_loads_empty()
    {
        using var t = new TempDir();
        var store = new ProjectStore(Path.Combine(t.Path, "projects.json"));
        var r = store.Load();
        Assert.Empty(r.Data.Projects);
        Assert.Null(r.CorruptBackupPath);
        Assert.Equal("Ctrl+Alt+P", r.Data.Settings.Hotkey);
    }

    [Fact]
    public void Round_trip_preserves_everything()
    {
        using var t = new TempDir();
        var file = Path.Combine(t.Path, "sub", "projects.json"); // フォルダがなくても作る
        var store = new ProjectStore(file);
        var data = new LauncherData();
        data.Settings.Hotkey = "Ctrl+Shift+Space";
        data.Settings.StartWithWindows = true;
        data.Settings.SortMode = SortMode.DirtyFirst;
        data.Projects.Add(new ProjectEntry
        {
            Name = "ホラーゲーム",
            Path = @"C:\dev\Horror",
            Kind = ProjectKind.Unity,
            KindIsManual = true,
            PublicUrl = "https://example.com/horror",
            Memo = "ボス戦まで",
            Tags = { "unity", "ホラー" },
            CachedStatus = new ProjectStatus
            {
                Exists = true,
                GitState = GitState.Ok,
                Branch = "main",
                ChangeCount = 3,
                LastModified = new DateTimeOffset(2026, 11, 20, 21, 5, 0, TimeSpan.FromHours(9)),
                LastModifiedSource = LastModifiedSource.GitCommit,
            },
        });

        store.Save(data);
        var loaded = store.Load();

        Assert.Null(loaded.CorruptBackupPath);
        Assert.Equal("Ctrl+Shift+Space", loaded.Data.Settings.Hotkey);
        Assert.True(loaded.Data.Settings.StartWithWindows);
        Assert.Equal(SortMode.DirtyFirst, loaded.Data.Settings.SortMode);
        var p = Assert.Single(loaded.Data.Projects);
        Assert.Equal("ホラーゲーム", p.Name);
        Assert.Equal(@"C:\dev\Horror", p.Path);
        Assert.Equal(ProjectKind.Unity, p.Kind);
        Assert.True(p.KindIsManual);
        Assert.Equal(new[] { "unity", "ホラー" }, p.Tags);
        Assert.Equal(3, p.CachedStatus!.ChangeCount);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 12, 5, 0, TimeSpan.Zero), p.CachedStatus.LastModified);
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public void Enums_are_saved_as_strings()
    {
        using var t = new TempDir();
        var file = Path.Combine(t.Path, "projects.json");
        var data = new LauncherData();
        data.Projects.Add(new ProjectEntry { Name = "a", Path = "/x", Kind = ProjectKind.WindowsApp });
        new ProjectStore(file).Save(data);
        Assert.Contains("\"WindowsApp\"", File.ReadAllText(file));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"projects\": \"oops\"}")]
    [InlineData("{\"settings\": {\"sortMode\": \"NoSuchMode\"}}")]
    public void Corrupt_json_is_backed_up_and_starts_empty(string content)
    {
        using var t = new TempDir();
        var file = Path.Combine(t.Path, "projects.json");
        File.WriteAllText(file, content);

        var r = new ProjectStore(file).Load();

        Assert.Empty(r.Data.Projects);
        Assert.NotNull(r.CorruptBackupPath);
        Assert.True(File.Exists(r.CorruptBackupPath));
        Assert.Equal(content, File.ReadAllText(r.CorruptBackupPath!));
        Assert.False(File.Exists(file)); // 元ファイルは退避済み。次の Save で作り直される
    }

    [Fact]
    public void Second_corruption_does_not_overwrite_first_backup()
    {
        using var t = new TempDir();
        var file = Path.Combine(t.Path, "projects.json");
        var store = new ProjectStore(file);

        File.WriteAllText(file, "broken-1");
        var b1 = store.Load().CorruptBackupPath;
        File.WriteAllText(file, "broken-2");
        var b2 = store.Load().CorruptBackupPath;

        Assert.NotEqual(b1, b2);
        Assert.Equal("broken-1", File.ReadAllText(b1!));
        Assert.Equal("broken-2", File.ReadAllText(b2!));
    }

    [Fact]
    public void Sanitizes_partial_data()
    {
        using var t = new TempDir();
        var file = Path.Combine(t.Path, "projects.json");
        File.WriteAllText(file, """
        {
          // コメントと末尾カンマを許容
          "settings": { "maxParallelism": 0, "scanDepth": 9, "hotkey": "" },
          "projects": [
            { "path": "C:\\dev\\NoName", "tags": null },
            { "name": "empty path", "path": "" },
          ]
        }
        """);

        var r = new ProjectStore(file).Load();

        Assert.Null(r.CorruptBackupPath);
        var p = Assert.Single(r.Data.Projects);
        Assert.NotEmpty(p.Id);
        Assert.NotNull(p.Tags);
        Assert.False(string.IsNullOrEmpty(p.Name));
        Assert.Equal(4, r.Data.Settings.MaxParallelism);
        Assert.Equal(2, r.Data.Settings.ScanDepth);
        Assert.Equal("Ctrl+Alt+P", r.Data.Settings.Hotkey);
    }
}
