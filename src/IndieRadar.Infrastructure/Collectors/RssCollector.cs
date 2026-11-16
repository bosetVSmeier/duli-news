using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;

namespace IndieRadar.Infrastructure.Collectors;

/// <summary>
/// 通用 RSS 采集器。用 HttpClient 拉取 XML，再用内置的 XDocument 解析，
/// 不依赖第三方 Feed 库，轻量且冷启动快。
/// </summary>
public sealed class RssCollector : IArticleCollector
{
    private readonly HttpClient _http;
    private readonly ILogger<RssCollector>? _logger;

    public RssCollector(HttpClient http, ILogger<RssCollector>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    public string Kind => "rss";

    private static string MakeId(string url)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();

    public async Task<IReadOnlyList<Article>> CollectAsync(Source source, CancellationToken ct = default)
    {
        try
        {
            var xml = await _http.GetStringAsync(source.Url, ct);
            var doc = XDocument.Parse(xml);

            // RSS 2.0 用 <item>，Atom 用 <entry>，这里两种都兼容
            var elements = doc.Descendants("item").Any()
                ? doc.Descendants("item")
                : doc.Descendants(XName.Get("entry", "http://www.w3.org/2005/Atom"));

            var result = new List<Article>();
            foreach (var el in elements.Take(30)) // 每次最多取 30 条，控制量
            {
                var title = el.Element("title")?.Value
                            ?? el.Element(XName.Get("title", "http://www.w3.org/2005/Atom"))?.Value
                            ?? "";
                var link = el.Element("link")?.Value
                           ?? el.Element(XName.Get("link", "http://www.w3.org/2005/Atom"))?.Attribute("href")?.Value
                           ?? "";
                var summary = el.Element("description")?.Value
                              ?? el.Element(XName.Get("summary", "http://www.w3.org/2005/Atom"))?.Value
                              ?? "";

                // 去 HTML 标签，避免脏数据干扰 AI 判断
                summary = StripHtml(summary);

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link))
                    continue;

                result.Add(new Article
                {
                    Id = MakeId(link),
                    SourceId = source.Id,
                    OriginalTitle = title.Trim(),
                    Url = link.Trim(),
                    Summary = summary.Trim(),
                    PublishedAt = DateTimeOffset.UtcNow
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            // 某个源抓取失败不应中断整个流水线，记录日志后返回空列表
            _logger?.LogWarning("RSS 采集失败 {Source}: {Message}", source.Name, ex.Message);
            return [];
        }
    }

    /// <summary>简单的 HTML 标签剥离。用正则足够应付 RSS 摘要。</summary>
    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", string.Empty).Trim();
    }
}
