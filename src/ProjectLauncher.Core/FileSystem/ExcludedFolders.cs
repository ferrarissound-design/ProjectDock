namespace ProjectLauncher.Core.FileSystem;

public static class ExcludedFolders
{
    /// <summary>
    /// 更新日時の計算・スキャンで必ず除外するフォルダ名（大文字小文字は区別しない、どの深さでも除外）。
    /// Unity の Library を走査すると数十万ファイルになるので絶対に入らない。
    /// </summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Library", "Temp", "obj", "bin", "node_modules", ".git", "Build",
    };

    public static bool IsExcluded(string folderName) => Names.Contains(folderName);
}
