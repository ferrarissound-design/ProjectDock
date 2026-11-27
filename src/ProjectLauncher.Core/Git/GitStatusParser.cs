namespace ProjectLauncher.Core.Git;

public sealed record GitStatusResult(string? Branch, int ChangeCount, bool IsDetached, bool HasNoCommits);

/// <summary>
/// <c>git status --porcelain --branch</c> の出力をパースする。
/// 1行目の "## ..." がブランチ情報、それ以外の非空行が変更1件。
/// （--branch なしの出力を渡しても、行数カウントはそのまま機能する）
/// </summary>
public static class GitStatusParser
{
    public static GitStatusResult Parse(string? output)
    {
        string? branch = null;
        bool detached = false;
        bool noCommits = false;
        int count = 0;

        if (string.IsNullOrEmpty(output))
            return new GitStatusResult(null, 0, false, false);

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                continue;

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                (branch, detached, noCommits) = ParseBranchHeader(line[3..]);
                continue;
            }

            count++;
        }

        return new GitStatusResult(branch, count, detached, noCommits);
    }

    internal static (string? Branch, bool Detached, bool NoCommits) ParseBranchHeader(string header)
    {
        header = header.Trim();

        // "No commits yet on main"（新しめの git） / "Initial commit on master"（古い git）
        foreach (var prefix in new[] { "No commits yet on ", "Initial commit on " })
        {
            if (header.StartsWith(prefix, StringComparison.Ordinal))
                return (header[prefix.Length..].Trim(), false, true);
        }

        // "HEAD (no branch)"
        if (header.StartsWith("HEAD (no branch)", StringComparison.Ordinal))
            return (null, true, false);

        // "main...origin/main [ahead 1, behind 2]" / "main [gone]" / "main"
        var name = header;
        var dots = name.IndexOf("...", StringComparison.Ordinal);
        if (dots >= 0)
            name = name[..dots];
        var bracket = name.IndexOf(" [", StringComparison.Ordinal);
        if (bracket >= 0)
            name = name[..bracket];

        name = name.Trim();
        return (name.Length == 0 ? null : name, false, false);
    }
}
