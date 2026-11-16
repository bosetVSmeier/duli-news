namespace IndieRadar.Domain.Entities;

/// <summary>
/// 资讯来源。每一个 RSS / HN / Product Hunt 等抓取源对应一条记录。
/// </summary>
public sealed class Source
{
    /// <summary>来源唯一标识，例如 "hackernews"。</summary>
    public required string Id { get; init; }

    /// <summary>展示名称，例如 "Hacker News"。</summary>
    public required string Name { get; init; }

    /// <summary>来源类型：rss / hackernews / producthunt / reddit 等。</summary>
    public required string Kind { get; init; }

    /// <summary>抓取地址（RSS 链接或 API 地址）。</summary>
    public required string Url { get; init; }

    /// <summary>
    /// 源权重（1-10）。权重越高的来源，其文章在粗筛阶段越容易被保留。
    /// 这是"先粗后精"策略里粗筛环节的一个信号。
    /// </summary>
    public int Weight { get; init; } = 5;

    /// <summary>是否启用该来源。</summary>
    public bool Enabled { get; init; } = true;
}
