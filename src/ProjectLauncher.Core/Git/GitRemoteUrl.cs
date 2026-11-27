using System.Text.RegularExpressions;

namespace ProjectLauncher.Core.Git;

/// <summary>git remote の URL を GitHub のブラウザ用 URL に変換する。</summary>
public static partial class GitRemoteUrl
{
    // scp 風: [user@]host:owner/repo(.git)
    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[^:/\s]+):(?<path>[^\s]+)$")]
    private static partial Regex ScpLike();

    /// <summary>
    /// 対応形式:
    ///   https://github.com/owner/repo(.git)  （user:token@ 付きでも資格情報は捨てる）
    ///   http://, git://, ssh://git@github.com[:port]/owner/repo(.git)
    ///   git@github.com:owner/repo(.git)
    ///   git@github.com-work:owner/repo.git のような ~/.ssh/config の別名（github.com- で始まるホスト）
    /// GitHub 以外のホストや解釈できない形式は null。
    /// </summary>
    public static string? ToGitHubUrl(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote))
            return null;

        remote = remote.Trim();
        string host;
        string path;

        if (remote.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri))
                return null;
            var scheme = uri.Scheme.ToLowerInvariant();
            if (scheme is not ("https" or "http" or "ssh" or "git" or "git+ssh" or "ssh+git"))
                return null;
            host = uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        else
        {
            // Windows のローカルパス (C:\repo) を scp 形式と誤認しない
            if (remote.Length >= 2 && remote[1] == ':' && char.IsLetter(remote[0]))
                return null;
            var m = ScpLike().Match(remote);
            if (!m.Success)
                return null;
            host = m.Groups["host"].Value;
            path = m.Groups["path"].Value;
        }

        if (!IsGitHubHost(host))
            return null;

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];
        path = path.TrimEnd('/');

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return null;

        return $"https://github.com/{parts[0]}/{parts[1]}";
    }

    private static bool IsGitHubHost(string host)
    {
        host = host.ToLowerInvariant();
        return host is "github.com" or "www.github.com" or "ssh.github.com"
            || host.StartsWith("github.com-", StringComparison.Ordinal);
    }
}
