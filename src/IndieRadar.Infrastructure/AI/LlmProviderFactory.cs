using Microsoft.Extensions.Logging;
using IndieRadar.Domain.Entities;
using IndieRadar.Domain.Interfaces;

namespace IndieRadar.Infrastructure.AI;

/// <summary>
/// 大模型工厂。负责根据配置创建 ILLMProvider 实例。
/// 这是"动态换模型"的装配点：配置说用谁，就创建谁——业务代码完全不动。
/// </summary>
public sealed class LlmProviderFactory
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILoggerFactory _loggerFactory;

    public LlmProviderFactory(IHttpClientFactory httpFactory, ILoggerFactory loggerFactory)
    {
        _httpFactory = httpFactory;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 根据模式创建 Provider。
    /// - mode = "mock"：本地模拟，零依赖演示用
    /// - mode = "real"：按传入的三件套配置构造真实 OpenAI 兼容 provider，
    ///   若同时传了 fallback 配置则包装成带降级的组合 provider
    /// </summary>
    public ILLMProvider Create(
        string? mode,
        LlmProviderConfig? primaryConfig = null,
        LlmProviderConfig? fallbackConfig = null)
    {
        if (string.Equals(mode, "real", StringComparison.OrdinalIgnoreCase) && primaryConfig is not null)
        {
            var primary = BuildOpenAi(primaryConfig);

            if (fallbackConfig is not null)
            {
                var fallback = BuildOpenAi(fallbackConfig);
                return new FallbackLlmProvider(primary, fallback,
                    _loggerFactory.CreateLogger<FallbackLlmProvider>());
            }
            return primary;
        }

        // 默认（含 mock）：本地模拟实现
        return new MockLlmProvider();
    }

    /// <summary>用一条三件套配置构造 OpenAI 兼容 provider。</summary>
    private OpenAiCompatibleProvider BuildOpenAi(LlmProviderConfig config)
    {
        // 用独立命名的 HttpClient，避免共享 client 上反复改 Authorization 头
        var http = _httpFactory.CreateClient("llm");
        return new OpenAiCompatibleProvider(http, config,
            _loggerFactory.CreateLogger<OpenAiCompatibleProvider>());
    }
}
