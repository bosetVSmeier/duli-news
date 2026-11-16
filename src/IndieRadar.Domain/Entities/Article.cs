namespace IndieRadar.Domain.Entities;

/// <summary>
/// 一篇文章（一条资讯）。采集后经过粗筛、AI 精加工，最终可能进入某份日报。
/// </summary>
public sealed class Article
{
    /// <summary>文章唯一标识。通常用来源 URL 的哈希生成，避免重复抓取重复入库。</summary>
    public required string Id { get; init; }

    /// <summary>所属来源 Id。</summary>
    public required string SourceId { get; init; }

    /// <summary>原文标题。</summary>
    public required string OriginalTitle { get; init; }

    /// <summary>原文链接。</summary>
    public required string Url { get; init; }

    /// <summary>原文摘要或正文片段（用于给 AI 判断相关性）。</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>发布时间。</summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>采集时间。</summary>
    public DateTimeOffset CollectedAt { get; init; } = DateTimeOffset.UtcNow;

    // ===== 粗筛阶段产生的字段 =====

    /// <summary>是否通过关键词粗筛（未被黑名单命中）。</summary>
    public bool PassedCoarseFilter { get; set; }

    /// <summary>粗筛命中的原因（便于调试为什么被丢弃）。</summary>
    public string CoarseFilterReason { get; set; } = string.Empty;

    // ===== AI 精加工阶段产生的字段 =====

    /// <summary>AI 给出的独立开发相关度评分（0-100）。</summary>
    public int? RelevanceScore { get; set; }

    /// <summary>AI 翻译/改写后的中文标题。</summary>
    public string? ChineseTitle { get; set; }

    /// <summary>AI 生成的中文摘要。</summary>
    public string? ChineseSummary { get; set; }

    /// <summary>AI 打出的分类标签，例如 "AI工具"、"变现"、"案例复盘"。</summary>
    public string? Category { get; set; }

    /// <summary>是否通过 AI 精加工（评分达到阈值）。</summary>
    public bool PassedAiFilter { get; set; }
}
