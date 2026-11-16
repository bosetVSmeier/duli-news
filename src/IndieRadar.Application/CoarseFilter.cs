using IndieRadar.Domain.Entities;

namespace IndieRadar.Application;

/// <summary>
/// 关键词粗筛器。这是"三层漏斗"的第一层：
/// 在调用 AI 之前，先用黑名单把明显的广告、赞助、招聘、促销内容砍掉，省钱省时间。
/// </summary>
public sealed class CoarseFilter
{
    // 黑名单关键词：命中任意一个就直接丢弃。
    // 用小写匹配，且都是"词"而非单字，避免误伤（例如 "ad" 会误伤 "idea"，所以不用）。
    private static readonly string[] BlockedKeywords =
    [
        "sponsor", "sponsored", "advertisement", "advertorial", "promo code",
        "coupon", "discount", "sale now", "buy now", "limited time offer",
        "hiring", "we're hiring", "job opening", "career opportunity",
        "giveaway", "affiliate", "referral bonus", "click here to win",
        "cryptocurrency giveaway", "nft drop", " presale",
        // 中文广告词兜底
        "广告", "赞助", "促销", "优惠券", "招聘", "内推", "限时特惠"
    ];

    // 独立开发相关的正向信号词。命中越多，越值得进入 AI 精加工。
    private static readonly string[] PositiveKeywords =
    [
        "indie", "indiehacker", "solo founder", "solopreneur", "bootstrapped",
        "bootstrap", "side project", "saas", "mrr", "revenue", "launch",
        "build in public", "micro saas", "product hunt", "startup", "ship",
        "独立开发", "副业", "出海", "变现"
    ];

    /// <summary>
    /// 执行粗筛，返回是否通过以及原因。
    /// </summary>
    public (bool Passed, string Reason) Evaluate(Article article, Source? source)
    {
        var text = $"{article.OriginalTitle} {article.Summary}".ToLowerInvariant();

        // 第一步：命中黑名单直接丢弃
        var hit = BlockedKeywords.FirstOrDefault(k => text.Contains(k.Trim()));
        if (hit is not null)
            return (false, $"命中广告/无关关键词: {hit.Trim()}");

        // 第二步：正文过短（少于 15 字符）视为无有效信息
        if (text.Trim().Length < 15)
            return (false, "正文过短");

        // 第三步：计算正向信号数量。
        // 注意这里不直接丢弃"零信号"的文章，因为很多好文章标题未必含这些词；
        // 只是把信号数记到 Reason 里，交给 AI 阶段做更精准的判断。
        var signalCount = PositiveKeywords.Count(k => text.Contains(k));

        // 第四步：低权重来源 + 零正向信号，直接丢弃（粗筛的保守剪枝）
        var weight = source?.Weight ?? 5;
        if (signalCount == 0 && weight <= 3)
            return (false, "低权重来源且无独立开发信号");

        return (true, $"通过粗筛（正向信号 {signalCount} 个，源权重 {weight}）");
    }
}
