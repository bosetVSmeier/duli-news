using IndieRadar.Application;
using IndieRadar.Domain.Entities;

namespace IndieRadar.Tests;

/// <summary>
/// 测试粗筛逻辑：广告/招聘应被丢弃，正常资讯应通过。
/// </summary>
public class CoarseFilterTests
{
    private readonly CoarseFilter _filter = new();

    private static Article MakeArticle(string title, string summary) => new()
    {
        Id = "test",
        SourceId = "sample",
        OriginalTitle = title,
        Url = "https://example.com/test",
        Summary = summary
    };

    [Fact]
    public void 招聘广告_应被丢弃()
    {
        var article = MakeArticle("We're hiring senior engineers", "Great benefits and apply now with your resume.");
        var source = new Source { Id = "sample", Name = "S", Kind = "sample", Url = "x" };

        var (passed, reason) = _filter.Evaluate(article, source);

        Assert.False(passed);
        Assert.Contains("命中广告", reason);
    }

    [Fact]
    public void 促销广告_应被丢弃()
    {
        var article = MakeArticle("Limited time offer 50% off", "Use promo code at checkout for a discount.");
        var source = new Source { Id = "sample", Name = "S", Kind = "sample", Url = "x" };

        var (passed, _) = _filter.Evaluate(article, source);

        Assert.False(passed);
    }

    [Fact]
    public void 独立开发资讯_应通过()
    {
        var article = MakeArticle(
            "How I grew my micro SaaS to 12k MRR",
            "A solo founder shares growth tactics and revenue numbers from a bootstrapped side project.");
        var source = new Source { Id = "sample", Name = "S", Kind = "sample", Url = "x" };

        var (passed, _) = _filter.Evaluate(article, source);

        Assert.True(passed);
    }
}
