namespace ProjectLauncher.Core.FileSystem;

public static class PathUtil
{
    /// <summary>絶対パス化し、末尾の区切り文字を落とす（ドライブ直下 C:\ は残す）。</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        var full = System.IO.Path.GetFullPath(path.Trim().Trim('"'));
        var root = System.IO.Path.GetPathRoot(full);
        if (full.Length > (root?.Length ?? 0))
            full = full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return full;
    }

    /// <summary>Windows 前提で大文字小文字を区別せずに比較する。</summary>
    public static bool SamePath(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string FolderName(string path)
    {
        var n = Normalize(path);
        var name = System.IO.Path.GetFileName(n);
        return string.IsNullOrEmpty(name) ? n : name;
    }
}
