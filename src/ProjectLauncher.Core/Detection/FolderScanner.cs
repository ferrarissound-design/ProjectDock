using ProjectLauncher.Core.FileSystem;
using ProjectLauncher.Core.Models;

namespace ProjectLauncher.Core.Detection;

public sealed record ScanCandidate(string Path, string Name, ProjectKind Kind, bool HasGit, bool AlreadyRegistered);

/// <summary>親フォルダの直下（深さ1〜2）からプロジェクト候補を探す。</summary>
public static class FolderScanner
{
    /// <param name="depth">1 = 直下のみ、2 = 孫まで。候補が見つかったフォルダの中には入らない。</param>
    public static List<ScanCandidate> Scan(string parent, int depth, IEnumerable<string> registeredPaths, CancellationToken ct = default)
    {
        depth = Math.Clamp(depth, 1, 2);
        var registered = new HashSet<string>(registeredPaths.Select(PathUtil.Normalize), StringComparer.OrdinalIgnoreCase);
        var results = new List<ScanCandidate>();

        if (!Directory.Exists(parent))
            return results;

        // 親フォルダ自体がプロジェクトのケースも拾う
        if (ProjectDetector.IsCandidate(parent))
            results.Add(Make(parent, registered));

        Walk(parent, 1);
        return results.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();

        void Walk(string dir, int level)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var child in SafeSubdirs(dir))
            {
                var name = System.IO.Path.GetFileName(child);
                if (ExcludedFolders.IsExcluded(name) || name.StartsWith('.'))
                    continue;

                if (ProjectDetector.IsCandidate(child))
                {
                    results.Add(Make(child, registered));
                    continue;
                }

                if (level < depth)
                    Walk(child, level + 1);
            }
        }
    }

    private static ScanCandidate Make(string path, HashSet<string> registered)
    {
        var norm = PathUtil.Normalize(path);
        return new ScanCandidate(
            norm,
            PathUtil.FolderName(norm),
            ProjectDetector.DetectKind(norm),
            ProjectDetector.HasGit(norm),
            registered.Contains(norm));
    }

    private static IEnumerable<string> SafeSubdirs(string dir)
    {
        try
        {
            return Directory.GetDirectories(dir)
                .Where(d =>
                {
                    try { return (File.GetAttributes(d) & (FileAttributes.ReparsePoint | FileAttributes.System)) == 0; }
                    catch { return false; }
                })
                .ToArray();
        }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
