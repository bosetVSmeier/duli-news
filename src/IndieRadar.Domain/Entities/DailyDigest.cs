namespace IndieRadar.Domain.Entities;

/// <summary>
/// 每日日报。由当天通过 AI 精加工的文章聚合而成。
/// </summary>
public sealed class DailyDigest
{
    /// <summary>日报日期，格式 yyyy-MM-dd，同时作为唯一标识。</summary>
    public required string Date { get; init; }

    /// <summary>该日报包含的文章 Id 列表（已按相关度排序）。</summary>
    public List<string> ArticleIds { get; init; } = [];

    /// <summary>生成时间。</summary>
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>当天采集总数（用于统计漏斗转化率）。</summary>
    public int TotalCollected { get; init; }

    /// <summary>通过粗筛的数量。</summary>
    public int PassedCoarseCount { get; init; }

    /// <summary>通过 AI 精加工的数量。</summary>
    public int PassedAiCount { get; init; }
}
