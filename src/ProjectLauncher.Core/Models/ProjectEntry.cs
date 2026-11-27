namespace ProjectLauncher.Core.Models;

public enum ProjectKind
{
    Other = 0,
    Unity = 1,
    Web = 2,
    WindowsApp = 3,
}

/// <summary>登録済みプロジェクト1件。projects.json に保存される。</summary>
public sealed class ProjectEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public ProjectKind Kind { get; set; } = ProjectKind.Other;

    /// <summary>true のとき Kind は自動判定で上書きしない。</summary>
    public bool KindIsManual { get; set; }

    public string? PublicUrl { get; set; }
    public string? Memo { get; set; }
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>前回取得した状態。起動直後に即表示するためのキャッシュ。</summary>
    public ProjectStatus? CachedStatus { get; set; }
}
