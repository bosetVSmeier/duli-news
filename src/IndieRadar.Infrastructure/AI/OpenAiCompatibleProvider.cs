using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;

namespace IndieRadar.Infrastructure.AI;

/// <summary>
/// OpenAI 协议兼容的大模型实现。Hy4 flash（腾讯云 TokenHub）、GLM-4-Flash
/// 等主流模型都提供 OpenAI 兼容端点，因此这一个实现就能覆盖所有模型——
/// 换模型只需改配置表里的 BaseUrl / ApiKey / ModelId，不用改代码。
/// </summary>
public sealed class OpenAiCompatibleProvider : ILLMProvider
{
    private readonly HttpClient _http;
    private readonly LlmProviderConfig _config;
    private readonly ILogger<OpenAiCompatibleProvider>? _logger;

    public OpenAiCompatibleProvider(
        HttpClient http,
        LlmProviderConfig config,
        ILogger<OpenAiCompatibleProvider>? logger = null)
    {
        _http = http;
        _config = config;
        _logger = logger;
        // 设置 Bearer Token
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
    }

    public string Name => _config.Name;

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        // 构造 OpenAI chat/completions 请求体
        var body = new
        {
            model = _config.ModelId,
            messages = new[]
            {
                new { role = "system", content = "你是一个资讯编辑，只返回 JSON，不要多余文字。" },
                new { role = "user", content = prompt }
            },
            temperature = 0.3,
            response_format = new { type = "json_object" }
        };

        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"{_config.BaseUrl.TrimEnd('/')}/chat/completions";
        using var response = await _http.PostAsync(url, content, ct);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger?.LogError("模型调用失败 {Name}: {Status} {Body}",
                _config.Name, response.StatusCode, responseJson);
            throw new HttpRequestException($"模型调用失败: {response.StatusCode}");
        }

        // 从 OpenAI 响应结构里取出第一条 message 的 content
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
