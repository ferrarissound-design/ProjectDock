using ProjectLauncher.Core.Models;

namespace ProjectLauncher.Core.Services;

public static class ProjectFilter
{
    /// <summary>
    /// 検索（名前・タグ・メモ。空白区切りは AND、大文字小文字無視）→ 未コミットのみ → 並び替え。
    /// 存在しないフォルダは並び順に関係なく末尾に寄せない（見落とさないよう通常どおり並べる）。
    /// </summary>
    public static List<ProjectEntry> Apply(IEnumerable<ProjectEntry> projects, string? query, SortMode sort, bool onlyDirty)
    {
        var terms = (query ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var q = projects.Where(p => terms.All(t => Matches(p, t)));
        if (onlyDirty)
            q = q.Where(p => p.CachedStatus?.HasUncommittedChanges == true);

        var nameCmp = StringComparer.CurrentCultureIgnoreCase;
        return sort switch
        {
            SortMode.Name => q.OrderBy(p => p.Name, nameCmp).ToList(),
            SortMode.DirtyFirst => q
                .OrderByDescending(p => p.CachedStatus?.HasUncommittedChanges == true)
                .ThenByDescending(p => p.CachedStatus?.ChangeCount ?? 0)
                .ThenByDescending(p => p.CachedStatus?.LastModified ?? DateTimeOffset.MinValue)
                .ThenBy(p => p.Name, nameCmp)
                .ToList(),
            _ => q
                .OrderByDescending(p => p.CachedStatus?.LastModified ?? DateTimeOffset.MinValue)
                .ThenBy(p => p.Name, nameCmp)
                .ToList(),
        };
    }

    private static bool Matches(ProjectEntry p, string term)
    {
        const StringComparison c = StringComparison.CurrentCultureIgnoreCase;
        return p.Name.Contains(term, c)
               || (p.Memo?.Contains(term, c) ?? false)
               || p.Tags.Any(t => t.Contains(term, c));
    }

    /// <summary>"a, b ,c" → ["a","b","c"]（全角読点・全角カンマも区切りとして扱う）。</summary>
    public static List<string> ParseTags(string? text)
        => (text ?? "")
            .Split(new[] { ',', '、', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
