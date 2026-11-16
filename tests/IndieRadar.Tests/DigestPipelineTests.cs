using IndieRadar.Application;
using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;
using IndieRadar.Infrastructure.AI;
using IndieRadar.Infrastructure.Collectors;
using IndieRadar.Infrastructure.Persistence;

namespace IndieRadar.Tests;

/// <summary>
/// 端到端测试整条流水线：采集 → 去重 → 粗筛 → AI 精加工 → 日报。
/// 只用示例采集器，保证无网络也能跑，验证过滤漏斗的正确性。
/// </summary>
public class DigestPipelineTests
{
    private static DigestPipeline BuildPipeline()
    {
        IReadOnlyList<IArticleCollector> collectors = [new SampleCollector()];
        ISourceRepository sources = new InMemorySourceRepository();
        IArticleRepository articles = new InMemoryArticleRepository();
        IDailyDigestRepository digests = new InMemoryDigestRepository();
        ILLMProvider llm = new MockLlmProvider();
        var coarse = new CoarseFilter();
        var ai = new AiEnrichmentService(llm, scoreThreshold: 60);

        return new DigestPipeline(collectors, sources, articles, digests, coarse, ai);
    }

    [Fact]
    public async Task 完整流水线_过滤漏斗正确()
    {
        var pipeline = BuildPipeline();
        var result = await pipeline.RunAsync(DateTimeOffset.UtcNow);

        // 示例数据共 7 条
        Assert.Equal(7, result.TotalCollected);

        // 粗筛后应减少：招聘 + 促销被丢弃（剩 5 条）
        Assert.Equal(5, result.PassedCoarseCount);

        // AI 精加工后：Apple、Bitcoin 被判低分丢弃（剩 3 条）
        Assert.Equal(3, result.PassedAiCount);
        Assert.Equal(3, result.Articles.Count);

        // 留下的文章都应有中文标题和分类，且分数 >= 60
        Assert.All(result.Articles, a =>
        {
            Assert.NotNull(a.ChineseTitle);
            Assert.NotNull(a.Category);
            Assert.True(a.RelevanceScore >= 60);
        });

        // 验证按分数降序
        var scores = result.Articles.Select(a => a.RelevanceScore).ToList();
        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }

    [Fact]
    public async Task 二次运行_去重后精选不丢失()
    {
        var sources = new InMemorySourceRepository();
        var articles = new InMemoryArticleRepository();
        var digests = new InMemoryDigestRepository();
        ILLMProvider llm = new MockLlmProvider();

        var pipeline = new DigestPipeline(
            [new SampleCollector()], sources, articles, digests,
            new CoarseFilter(), new AiEnrichmentService(llm));

        var first = await pipeline.RunAsync(DateTimeOffset.UtcNow);
        var second = await pipeline.RunAsync(DateTimeOffset.UtcNow);

        // 第二次运行时文章被去重，但采用合并策略：
        // 精选列表不清空，仍保留第一次筛出的结果（防止重复触发把日报洗掉）
        Assert.Equal(7, first.TotalCollected);
        Assert.Equal(3, first.PassedAiCount);
        Assert.Equal(3, second.PassedAiCount);
        Assert.Equal(3, second.Articles.Count);
    }
}
