using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;

namespace IndieRadar.Infrastructure.Collectors;

/// <summary>
/// Hacker News 采集器。使用 HN 官方公开 API（无需密钥）。
/// 先取 top stories 的 Id 列表，再逐条取详情。
/// </summary>
public sealed class HackerNewsCollector : IArticleCollector
{
    private readonly HttpClient _http;
    private readonly ILogger<HackerNewsCollector>? _logger;

    public HackerNewsCollector(HttpClient http, ILogger<HackerNewsCollector>? logger = null)
    {
        _http = http;
        _logger = logger;
    }

    public string Kind => "hackernews";

    private static string MakeId(string url)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16].ToLowerInvariant();

    public async Task<IReadOnlyList<Article>> CollectAsync(Source source, CancellationToken ct = default)
    {
        try
        {
            // HN 官方 API：取前 20 条热门 story 的 Id
            var idsJson = await _http.GetStringAsync(
                "https://hacker-news.firebaseio.com/v0/topstories.json", ct);
            var ids = JsonSerializer.Deserialize<int[]>(idsJson)?.Take(20) ?? [];

            var result = new List<Article>();
            foreach (var id in ids)
            {
                var itemJson = await _http.GetStringAsync(
                    $"https://hacker-news.firebaseio.com/v0/item/{id}.json", ct);
                using var doc = JsonDocument.Parse(itemJson);
                var root = doc.RootElement;

                var title = root.GetProperty("title").GetString() ?? "";
                var url = root.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(url))
                    url = $"https://news.ycombinator.com/item?id={id}"; // 无外链时用 HN 讨论页

                var time = root.TryGetProperty("time", out var t) ? t.GetInt64() : 0;

                result.Add(new Article
                {
                    Id = MakeId(url),
                    SourceId = source.Id,
                    OriginalTitle = title,
                    Url = url,
                    Summary = $"Hacker News story with score {GetScore(root)}.",
                    PublishedAt = time > 0 ? DateTimeOffset.FromUnixTimeSeconds(time) : DateTimeOffset.UtcNow
                });
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("HN 采集失败: {Message}", ex.Message);
            return [];
        }
    }

    private static int GetScore(JsonElement root)
        => root.TryGetProperty("score", out var s) ? s.GetInt32() : 0;
}
