using System.Text.RegularExpressions;
using IndieRadar.Domain.Interfaces;

namespace IndieRadar.Infrastructure.AI;

/// <summary>
/// 模拟大模型。用于本地演示——不调用任何外部 API，根据标题关键词生成合理的加工结果。
/// 这样在没有 AI 密钥时也能完整跑通流水线、看到过滤效果。
/// 真实环境切换到 OpenAICompatibleProvider 即可。
/// </summary>
public sealed class MockLlmProvider : ILLMProvider
{
    public string Name => "Mock(本地演示)";

    public Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        // 从提示词里抠出标题（利用我们 BuildPrompt 的固定格式）
        var title = "";
        var match = Regex.Match(prompt, @"文章标题：(.*)", RegexOptions.Multiline);
        if (match.Success)
            title = match.Groups[1].Value.Trim();

        // 根据标题关键词判断相关度和分类
        var (score, category) = Classify(title);

        // 不相关的内容给低分，会被 AI 阶段丢弃
        var chineseTitle = score >= 60 ? $"【译】{Truncate(title, 30)}" : Truncate(title, 30);
        var chineseSummary = score >= 60
            ? $"独立开发相关资讯：{Truncate(title, 40)}。值得关注其思路与方法。"
            : "内容与独立开发关系不大，仅作存档。";

        // 返回与真实模型一致结构的 JSON
        var json = $$"""
            {
              "score": {{score}},
              "chineseTitle": "{{Escape(chineseTitle)}}",
              "chineseSummary": "{{Escape(chineseSummary)}}",
              "category": "{{category}}"
            }
            """;
        return Task.FromResult(json);
    }

    /// <summary>根据标题关键词给出评分和分类（模拟 AI 的判断）。</summary>
    private static (int Score, string Category) Classify(string title)
    {
        var t = title.ToLowerInvariant();

        // 泛科技/大公司/加密货币 → 低分，应被丢弃
        if (t.Contains("apple") || t.Contains("bitcoin") || t.Contains("crypto") ||
            t.Contains("data center") || t.Contains("institutional"))
            return (15, "行业观察");

        // 独立开发强相关 → 高分
        if (t.Contains("mrr") || t.Contains("revenue") || t.Contains("bootstrapped"))
            return (92, "变现");
        if (t.Contains("indie") || t.Contains("solo founder") || t.Contains("side project"))
            return (88, "案例复盘");
        if (t.Contains("ai tool") || t.Contains("ai tools"))
            return (80, "AI工具");
        if (t.Contains("launch") || t.Contains("shipping") || t.Contains("product hunt"))
            return (78, "产品发布");
        if (t.Contains("growth") || t.Contains("seo") || t.Contains("users"))
            return (75, "增长营销");

        // 其余默认中等分（可能保留，交给阈值决定）
        return (55, "行业观察");
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private static string Escape(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
