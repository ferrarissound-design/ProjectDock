using System.Text.Json;

namespace ProjectLauncher.Core.Services;

public static class UnityProjectInfo
{
    /// <summary>ProjectSettings/ProjectVersion.txt の "m_EditorVersion: 2022.3.10f1" を読む。</summary>
    public static string? ParseEditorVersion(string? projectVersionText)
    {
        if (string.IsNullOrEmpty(projectVersionText))
            return null;
        foreach (var raw in projectVersionText.Split('\n'))
        {
            var line = raw.Trim();
            const string key = "m_EditorVersion:";
            if (line.StartsWith(key, StringComparison.Ordinal))
            {
                var v = line[key.Length..].Trim();
                return v.Length == 0 ? null : v;
            }
        }
        return null;
    }

    public static string? ReadEditorVersion(string projectDir)
    {
        try
        {
            var f = Path.Combine(projectDir, "ProjectSettings", "ProjectVersion.txt");
            return File.Exists(f) ? ParseEditorVersion(File.ReadAllText(f)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>%APPDATA%\UnityHub\secondaryInstallPath.json（中身は JSON 文字列 1 個）からエディタの追加インストール先を読む。</summary>
    public static string? ParseSecondaryInstallPath(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var s = JsonSerializer.Deserialize<string>(json);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
