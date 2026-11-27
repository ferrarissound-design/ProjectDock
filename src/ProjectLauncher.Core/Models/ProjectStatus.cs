namespace ProjectLauncher.Core.Models;

public enum GitState
{
    /// <summary>まだ取得していない。</summary>
    Unknown = 0,
    /// <summary>.git がない。</summary>
    NotARepository = 1,
    /// <summary>git.exe が見つからない。</summary>
    GitNotFound = 2,
    /// <summary>状態を取得できた。</summary>
    Ok = 3,
    /// <summary>git コマンドが失敗した（dubious ownership など）。</summary>
    Error = 4,
}

public enum LastModifiedSource
{
    None = 0,
    GitCommit = 1,
    Files = 2,
}

/// <summary>プロジェクトの動的な状態。毎回計算し、キャッシュとして保存もする。</summary>
public sealed class ProjectStatus
{
    public bool Exists { get; set; }
    public GitState GitState { get; set; }
    public string? Branch { get; set; }
    public int ChangeCount { get; set; }
    public string? GitError { get; set; }
    public string? RemoteUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public LastModifiedSource LastModifiedSource { get; set; }
    public DateTimeOffset CheckedAt { get; set; }

    public bool HasUncommittedChanges => GitState == GitState.Ok && ChangeCount > 0;
}
