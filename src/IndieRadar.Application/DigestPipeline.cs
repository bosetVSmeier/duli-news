using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;

namespace IndieRadar.Application;

/// <summary>
/// 日报流水线（Phase 1 的中枢）。
/// 把五步流程串起来：采集 → 去重 → 粗筛 → AI 精加工 → 生成日报。
/// </summary>
public sealed class DigestPipeline
{
    private readonly IReadOnlyDictionary<string, IArticleCollector> _collectors;
    private readonly ISourceRepository _sourceRepo;
    private readonly IArticleRepository _articleRepo;
    private readonly IDailyDigestRepository _digestRepo;
    private readonly CoarseFilter _coarseFilter;
    private readonly AiEnrichmentService _aiService;

    public DigestPipeline(
        IEnumerable<IArticleCollector> collectors,
        ISourceRepository sourceRepo,
        IArticleRepository articleRepo,
        IDailyDigestRepository digestRepo,
        CoarseFilter coarseFilter,
        AiEnrichmentService aiService)
    {
        // 按 Kind 建索引，运行时根据来源类型找到对应采集器
        _collectors = collectors.ToDictionary(c => c.Kind, StringComparer.OrdinalIgnoreCase);
        _sourceRepo = sourceRepo;
        _articleRepo = articleRepo;
        _digestRepo = digestRepo;
        _coarseFilter = coarseFilter;
        _aiService = aiService;
    }

    /// <summary>
    /// 执行一天的日报生成，返回生成结果（含漏斗各阶段计数）。
    /// </summary>
    public async Task<DigestRunResult> RunAsync(
        DateTimeOffset date,
        CancellationToken ct = default)
    {
        var dateKey = date.ToString("yyyy-MM-dd");
        var sources = await _sourceRepo.GetAllEnabledAsync(ct);

        // ===== 第 1 步：采集 =====
        var collected = new List<Article>();
        foreach (var source in sources)
        {
            if (!_collectors.TryGetValue(source.Kind, out var collector))
                continue; // 没有对应采集器的来源跳过

            var items = await collector.CollectAsync(source, ct);
            collected.AddRange(items);
        }

        // ===== 第 2 步：去重 =====
        // 同一 Id（由 URL 哈希生成）只保留一条；同时也查库里是否已存在
        var deduped = new List<Article>();
        foreach (var article in collected)
        {
            if (deduped.Any(a => a.Id == article.Id))
                continue;
            if (await _articleRepo.ExistsAsync(article.Id, ct))
                continue;
            deduped.Add(article);
        }

        // ===== 第 3 步：粗筛 =====
        var sourceMap = sources.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var article in deduped)
        {
            var source = sourceMap.GetValueOrDefault(article.SourceId);
            var (passed, reason) = _coarseFilter.Evaluate(article, source);
            article.PassedCoarseFilter = passed;
            article.CoarseFilterReason = reason;
        }
        var afterCoarse = deduped.Where(a => a.PassedCoarseFilter).ToList();

        // ===== 第 4 步：AI 精加工 =====
        var aiPassed = await _aiService.EnrichBatchAsync(deduped, ct);
        var afterAi = deduped
            .Where(a => a.PassedAiFilter)
            .OrderByDescending(a => a.RelevanceScore)
            .ToList();

        // ===== 第 5 步：落库 + 生成日报 =====
        await _articleRepo.AddRangeAsync(deduped, ct);

        // 当天若已有日报，采用"合并"而非"覆盖"：
        // 重复触发（如手动+定时任务撞车）时，去重后新文章为 0，
        // 直接覆盖会把之前辛苦筛出的精选清空。这里把新旧文章 Id 合并去重。
        var existing = await _digestRepo.GetAsync(dateKey, ct);
        var mergedIds = existing is null
            ? afterAi.Select(a => a.Id).ToList()
            : existing.ArticleIds
                .Concat(afterAi.Select(a => a.Id))
                .Distinct()
                .ToList();

        var digest = new DailyDigest
        {
            Date = dateKey,
            ArticleIds = mergedIds,
            TotalCollected = (existing?.TotalCollected ?? 0) + collected.Count,
            PassedCoarseCount = (existing?.PassedCoarseCount ?? 0) + afterCoarse.Count,
            PassedAiCount = mergedIds.Count
        };
        await _digestRepo.SaveAsync(digest, ct);

        // 返回结果时，用合并后的完整列表（从库里取回，保证与日报一致）
        var allArticles = await _articleRepo.GetAllAsync(ct);
        var idSet = mergedIds.ToHashSet();
        var resultArticles = allArticles
            .Where(a => idSet.Contains(a.Id))
            .OrderByDescending(a => a.RelevanceScore)
            .ToList();

        return new DigestRunResult(
            dateKey,
            digest.TotalCollected,
            digest.PassedCoarseCount,
            digest.PassedAiCount,
            resultArticles);
    }
}

/// <summary>
/// 一次日报生成的执行结果，供 API 层返回给前端展示。
/// </summary>
public sealed record DigestRunResult(
    string Date,
    int TotalCollected,
    int PassedCoarseCount,
    int PassedAiCount,
    IReadOnlyList<Article> Articles);
