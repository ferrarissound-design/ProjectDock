namespace ProjectLauncher.Core.FileSystem;

/// <summary>除外フォルダを避けながら、フォルダ内ファイルの最終更新日時の最大値を求める。</summary>
public static class LastModifiedCalculator
{
    /// <summary>無限に走査しないための上限（ファイル数）。超えたらそこまでの最大値を返す。</summary>
    public const int DefaultMaxFiles = 200_000;

    public static DateTimeOffset? GetLatestWriteTime(string root, CancellationToken ct = default, int maxFiles = DefaultMaxFiles)
    {
        if (!Directory.Exists(root))
            return null;

        DateTime? latestUtc = null;
        int seen = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(root));

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = dir.EnumerateFileSystemInfos("*", options);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            try
            {
                foreach (var e in entries)
                {
                    if (e is DirectoryInfo sub)
                    {
                        if (ExcludedFolders.IsExcluded(sub.Name))
                            continue;
                        // シンボリックリンク・ジャンクションはループの危険があるので辿らない
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0)
                            continue;
                        stack.Push(sub);
                    }
                    else
                    {
                        var t = e.LastWriteTimeUtc;
                        if (latestUtc == null || t > latestUtc)
                            latestUtc = t;
                        if (++seen >= maxFiles)
                            return ToOffset(latestUtc);
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ToOffset(latestUtc);
    }

    private static DateTimeOffset? ToOffset(DateTime? utc)
        => utc == null ? null : new DateTimeOffset(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc)).ToLocalTime();
}
