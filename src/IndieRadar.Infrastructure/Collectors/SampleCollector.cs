using System.Security.Cryptography;
using System.Text;
using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;

namespace IndieRadar.Infrastructure.Collectors;

/// <summary>
/// 演示数据采集器。内置若干条真实风格的资讯，包含：
/// - 应该被保留的独立开发内容
/// - 应该被粗筛丢弃的广告/招聘内容
/// - 应该被 AI 判定为不相关的泛科技内容
/// 这样本地跑一次就能完整演示"三层漏斗"的过滤效果。
/// </summary>
public sealed class SampleCollector : IArticleCollector
{
    public string Kind => "sample";

    // 用 URL 的 SHA256 前 16 位作为 Id，保证同一 URL 幂等
    private static string MakeId(string url)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();

    public Task<IReadOnlyList<Article>> CollectAsync(Source source, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var data = new (string Title, string Url, string Summary)[]
        {
            // —— 应该保留：独立开发变现案例 ——
            (
                "How I Grew My Micro-SaaS to $12k MRR in 8 Months",
                "https://example.com/micro-saas-mrr",
                "A solo founder shares the exact growth tactics, pricing experiments, and SEO moves that took a side project from zero to twelve thousand dollars monthly recurring revenue."
            ),
            // —— 应该保留：build in public 产品发布 ——
            (
                "Shipping my indie hacker tool after 6 months of building in public",
                "https://example.com/indie-tool-launch",
                "Lessons from launching a bootstrapped SaaS: what worked on Product Hunt, how I found my first 100 users, and the mistakes I would avoid next time."
            ),
            // —— 应该保留：AI 工具 but 面向独立开发者 ——
            (
                "5 AI tools that save solo founders 10 hours a week",
                "https://example.com/ai-tools-solo-founders",
                "A curated list of AI tools for indie developers covering content creation, customer support automation, and code generation, with real cost breakdowns."
            ),
            // —— 粗筛应丢弃：招聘广告 ——
            (
                "We're hiring senior engineers - competitive salary and equity",
                "https://example.com/hiring-engineers",
                "Join our team! Great benefits, free lunch, and stock options. Apply now with your resume."
            ),
            // —— 粗筛应丢弃：促销 ——
            (
                "Limited time offer: 50% off all plans, coupon inside",
                "https://example.com/promo-sale",
                "Black Friday sale. Use promo code at checkout for a discount on all annual subscriptions."
            ),
            // —— AI 应丢弃：泛科技大公司新闻 ——
            (
                "Apple announces new data center in Arizona",
                "https://example.com/apple-datacenter",
                "The tech giant revealed plans for a new facility focused on cloud infrastructure and artificial intelligence research."
            ),
            // —— AI 应丢弃：加密货币 ——
            (
                "Bitcoin price surges as institutional investors pile in",
                "https://example.com/bitcoin-surge",
                "Cryptocurrency markets rallied this week as large funds increased their holdings, analysts said."
            )
        };

        var articles = data.Select(d => new Article
        {
            Id = MakeId(d.Url),
            SourceId = source.Id,
            OriginalTitle = d.Title,
            Url = d.Url,
            Summary = d.Summary,
            PublishedAt = now.AddHours(-Random.Shared.Next(1, 24)),
            CollectedAt = now
        }).ToList();

        return Task.FromResult<IReadOnlyList<Article>>(articles);
    }
}
