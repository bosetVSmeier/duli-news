using IndieRadar.Domain.Entities;

namespace IndieRadar.Domain.Interfaces;

/// <summary>
/// 文章仓储接口。上层只依赖接口，不关心底层是内存还是 Postgres。
/// 这样本地演示用内存实现，生产用 Postgres 实现，切换只需改一行注册代码。
/// </summary>
public interface IArticleRepository
{
    /// <summary>批量写入文章；已存在（同 Id）的跳过。</summary>
    Task<int> AddRangeAsync(IEnumerable<Article> articles, CancellationToken ct = default);

    /// <summary>获取某一天采集的文章。</summary>
    Task<IReadOnlyList<Article>> GetByDateAsync(string date, CancellationToken ct = default);

    /// <summary>获取全部文章。</summary>
    Task<IReadOnlyList<Article>> GetAllAsync(CancellationToken ct = default);

    /// <summary>判断某 Id 是否已存在（用于去重）。</summary>
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);
}

/// <summary>日报仓储接口。</summary>
public interface IDailyDigestRepository
{
    Task SaveAsync(DailyDigest digest, CancellationToken ct = default);

    Task<DailyDigest?> GetAsync(string date, CancellationToken ct = default);

    Task<IReadOnlyList<DailyDigest>> GetRecentAsync(int count, CancellationToken ct = default);
}

/// <summary>来源仓储接口。</summary>
public interface ISourceRepository
{
    Task<IReadOnlyList<Source>> GetAllEnabledAsync(CancellationToken ct = default);
}
