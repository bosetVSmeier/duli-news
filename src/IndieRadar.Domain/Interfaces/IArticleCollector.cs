using IndieRadar.Domain.Entities;

namespace IndieRadar.Domain.Interfaces;

/// <summary>
/// 资讯采集器接口。每种来源类型（RSS / HN / 示例数据）各实现一个。
/// </summary>
public interface IArticleCollector
{
    /// <summary>该采集器处理的来源类型，例如 "rss"、"hackernews"、"sample"。</summary>
    string Kind { get; }

    /// <summary>从指定来源采集文章。</summary>
    Task<IReadOnlyList<Article>> CollectAsync(Source source, CancellationToken ct = default);
}
