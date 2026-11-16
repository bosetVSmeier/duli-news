using System.Text.Json;
using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;

namespace IndieRadar.Application;

/// <summary>
/// AI 精加工服务。三层漏斗的最后一层：
/// 对通过粗筛的文章，让大模型判断"独立开发相关度"，达标的生成中文标题、摘要、分类。
/// </summary>
public sealed class AiEnrichmentService
{
    private readonly ILLMProvider _provider;
    private readonly int _scoreThreshold;

    /// <summary>
    /// 构造函数注入大模型提供者和评分阈值。
    /// 阈值从配置读，默认 60 分——低于 60 的视为与独立开发关系不大，丢弃。
    /// </summary>
    public AiEnrichmentService(ILLMProvider provider, int scoreThreshold = 60)
    {
        _provider = provider;
        _scoreThreshold = scoreThreshold;
    }

    /// <summary>
    /// 构造提示词。要求模型返回严格的 JSON，便于程序解析。
    /// 提示词里明确了"独立开发风向"的判断标准，让模型知道该留什么、丢什么。
    /// </summary>
    private static string BuildPrompt(Article article) => $$"""
        你是一个"海外独立开发资讯"编辑。请判断下面这篇文章是否与独立开发者（indie hacker / 独立 SaaS / 个人开发者 / 出海变现）相关。

        判断标准：
        - 相关：独立开发经验、SaaS 变现/MRR、产品发布、build in public、个人开发者工具、出海案例、增长与营销实战。
        - 不相关：大公司八卦、纯融资新闻、加密货币、泛科技资讯、招聘广告、产品促销、与独立开发无关的 AI 新闻。

        文章标题：{{article.OriginalTitle}}
        文章来源：{{article.SourceId}}
        文章摘要：{{article.Summary}}

        请只返回 JSON，不要任何多余文字，格式如下：
        {
          "score": 0-100 的整数，表示与独立开发的相关度,
          "chineseTitle": "翻译或改写的中文标题，不超过 30 字",
          "chineseSummary": "中文摘要，2 句话以内，说清这篇文章对独立开发者有什么价值",
          "category": "从这些里选一个：AI工具 / 变现 / 案例复盘 / 产品发布 / 增长营销 / 开发技巧 / 行业观察"
        }
        """;

    /// <summary>
    /// 对单篇文章做精加工，原地更新 article 的 AI 相关字段。
    /// </summary>
    public async Task EnrichAsync(Article article, CancellationToken ct = default)
    {
        var prompt = BuildPrompt(article);
        var raw = await _provider.CompleteAsync(prompt, ct);

        // 模型有时会在 JSON 外包一层 ```json ... ```，这里做容错清理
        var json = raw.Trim();
        if (json.StartsWith("```"))
        {
            var first = json.IndexOf('\n');
            var last = json.LastIndexOf("```");
            if (first >= 0 && last > first)
                json = json[(first + 1)..last].Trim();
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        article.RelevanceScore = root.GetProperty("score").GetInt32();
        article.ChineseTitle = root.GetProperty("chineseTitle").GetString();
        article.ChineseSummary = root.GetProperty("chineseSummary").GetString();
        article.Category = root.GetProperty("category").GetString();
        article.PassedAiFilter = article.RelevanceScore >= _scoreThreshold;
    }

    /// <summary>
    /// 批量加工：只处理通过粗筛的文章，返回通过 AI 筛选的数量。
    /// 逐条串行调用，便于控制速率（免费模型通常有 RPM 限制）。
    /// </summary>
    public async Task<int> EnrichBatchAsync(
        IEnumerable<Article> articles,
        CancellationToken ct = default)
    {
        var passed = 0;
        foreach (var article in articles)
        {
            if (!article.PassedCoarseFilter)
                continue;

            await EnrichAsync(article, ct);
            if (article.PassedAiFilter)
                passed++;
        }
        return passed;
    }
}
