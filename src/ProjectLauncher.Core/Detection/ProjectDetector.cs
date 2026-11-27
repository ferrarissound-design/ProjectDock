using ProjectLauncher.Core.Models;

namespace ProjectLauncher.Core.Detection;

/// <summary>フォルダの中身からプロジェクト種別・候補かどうかを判定する。</summary>
public static class ProjectDetector
{
    private static readonly string[] WindowsAppPatterns = { "*.sln", "*.csproj", "*.vbproj", "*.fsproj", "*.vcxproj" };

    public static bool HasGit(string dir)
        // .git はワークツリーやサブモジュールではファイルになる
        => Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git"));

    public static bool IsUnity(string dir)
        => Directory.Exists(Path.Combine(dir, "Assets")) && Directory.Exists(Path.Combine(dir, "ProjectSettings"));

    public static bool IsWeb(string dir)
        => File.Exists(Path.Combine(dir, "package.json")) || File.Exists(Path.Combine(dir, "index.html"));

    public static bool IsWindowsApp(string dir)
    {
        try
        {
            foreach (var pattern in WindowsAppPatterns)
            {
                if (Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly).Any())
                    return true;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    /// <summary>
    /// 種別の自動判定。優先順位：Unity → Windowsアプリ(.sln/.csproj等) → Web(package.json/index.html) → その他。
    /// Unity を最優先にするのは、Unity プロジェクトには .sln/.csproj が自動生成されるため。
    /// </summary>
    public static ProjectKind DetectKind(string dir)
    {
        if (!Directory.Exists(dir))
            return ProjectKind.Other;
        if (IsUnity(dir))
            return ProjectKind.Unity;
        if (IsWindowsApp(dir))
            return ProjectKind.WindowsApp;
        if (IsWeb(dir))
            return ProjectKind.Web;
        return ProjectKind.Other;
    }

    /// <summary>
    /// 「親フォルダをスキャン」で候補にするか。
    /// 仕様の3条件（.git / Unity / package.json or index.html）に加え、.sln/.csproj 等も候補にする。
    /// </summary>
    public static bool IsCandidate(string dir)
    {
        if (!Directory.Exists(dir))
            return false;
        return HasGit(dir) || IsUnity(dir) || IsWeb(dir) || IsWindowsApp(dir);
    }

    public static string KindLabel(ProjectKind kind) => kind switch
    {
        ProjectKind.Unity => "Unity",
        ProjectKind.Web => "Web",
        ProjectKind.WindowsApp => "Windowsアプリ",
        _ => "その他",
    };
}
