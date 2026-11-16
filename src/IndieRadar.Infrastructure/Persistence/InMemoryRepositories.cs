using System.Collections.Concurrent;
using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;

namespace IndieRadar.Infrastructure.Persistence;

/// <summary>
/// 内存版文章仓储。用于本地演示和单元测试——零外部依赖，启动即有数据。
/// 生产环境换成 Postgres 版即可，上层代码完全不用改。
/// </summary>
public sealed class InMemoryArticleRepository : IArticleRepository
{
    // ConcurrentDictionary 保证多线程安全（API 可能并发请求）
    private readonly ConcurrentDictionary<string, Article> _articles = new();

    public Task<int> AddRangeAsync(IEnumerable<Article> articles, CancellationToken ct = default)
    {
        var added = 0;
        foreach (var article in articles)
        {
            // TryAdd：同 Id 已存在则跳过，实现幂等
            if (_articles.TryAdd(article.Id, article))
                added++;
        }
        return Task.FromResult(added);
    }

    public Task<IReadOnlyList<Article>> GetByDateAsync(string date, CancellationToken ct = default)
    {
        var result = _articles.Values
            .Where(a => a.CollectedAt.ToString("yyyy-MM-dd") == date)
            .ToList();
        return Task.FromResult<IReadOnlyList<Article>>(result);
    }

    public Task<IReadOnlyList<Article>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Article>>(_articles.Values.ToList());

    public Task<bool> ExistsAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_articles.ContainsKey(id));
}

/// <summary>内存版日报仓储。</summary>
public sealed class InMemoryDigestRepository : IDailyDigestRepository
{
    private readonly ConcurrentDictionary<string, DailyDigest> _digests = new();

    public Task SaveAsync(DailyDigest digest, CancellationToken ct = default)
    {
        _digests.AddOrUpdate(digest.Date, digest, (_, _) => digest);
        return Task.CompletedTask;
    }

    public Task<DailyDigest?> GetAsync(string date, CancellationToken ct = default)
    {
        _digests.TryGetValue(date, out var digest);
        return Task.FromResult(digest);
    }

    public Task<IReadOnlyList<DailyDigest>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        var result = _digests.Values
            .OrderByDescending(d => d.Date)
            .Take(count)
            .ToList();
        return Task.FromResult<IReadOnlyList<DailyDigest>>(result);
    }
}

/// <summary>
/// 内存版来源仓储。预置了几个真实的独立开发资讯源，
/// 外加一个 sample 源用于无网络环境下的演示。
/// </summary>
public sealed class InMemorySourceRepository : ISourceRepository
{
    private readonly List<Source> _sources =
    [
        new() { Id = "hackernews", Name = "Hacker News", Kind = "hackernews", Url = "https://news.ycombinator.com", Weight = 8 },
        new() { Id = "indiehackers", Name = "Indie Hackers", Kind = "rss", Url = "https://www.indiehackers.com/feed.xml", Weight = 10 },
        new() { Id = "producthunt", Name = "Product Hunt", Kind = "rss", Url = "https://www.producthunt.com/feed", Weight = 7 },
        new() { Id = "sample", Name = "演示数据（本地）", Kind = "sample", Url = "local://sample", Weight = 5, Enabled = true }
    ];

    public Task<IReadOnlyList<Source>> GetAllEnabledAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Source>>(_sources.Where(s => s.Enabled).ToList());
}
