using ProjectLauncher.Core.Models;

namespace ProjectLauncher.Core.Services;

public static class DisplayFormat
{
    /// <summary>一覧の「最終更新」列。例: "3日前 (11/24 21:05)"</summary>
    public static string LastModified(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value == null)
            return "—";
        var v = value.Value.ToLocalTime();
        return $"{Relative(v, now)} ({(v.Year == now.Year ? v.ToString("MM/dd HH:mm") : v.ToString("yyyy/MM/dd"))})";
    }

    public static string Relative(DateTimeOffset value, DateTimeOffset now)
    {
        var d = now - value;
        if (d < TimeSpan.Zero) return "たった今";
        if (d.TotalMinutes < 1) return "たった今";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}分前";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours}時間前";
        if (d.TotalDays < 31) return $"{(int)d.TotalDays}日前";
        if (d.TotalDays < 365) return $"{(int)(d.TotalDays / 30)}か月前";
        return $"{(int)(d.TotalDays / 365)}年前";
    }

    /// <summary>一覧の「Git」列のテキスト。</summary>
    public static string GitColumn(ProjectStatus? s)
    {
        if (s == null) return "取得中…";
        if (!s.Exists) return "見つかりません";
        return s.GitState switch
        {
            GitState.NotARepository => "Git未管理",
            GitState.GitNotFound => "git未検出",
            GitState.Error => "gitエラー",
            GitState.Ok => s.ChangeCount > 0 ? $"未コミット {s.ChangeCount}件" : "クリーン",
            _ => "取得中…",
        };
    }

    public static bool IsHttpUrl(string? url)
        => Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
}
