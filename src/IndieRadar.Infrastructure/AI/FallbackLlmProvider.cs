using Microsoft.Extensions.Logging;
using IndieRadar.Domain.Interfaces;

namespace IndieRadar.Infrastructure.AI;

/// <summary>
/// 带降级的组合 Provider：主模型调用失败（异常/超时/限流）时，
/// 自动切换到兜底模型（通常是免费的 GLM-4-Flash），保证日报不中断。
/// 对上层来说它就是一个普通的 ILLMProvider，完全感知不到降级发生。
/// </summary>
public sealed class FallbackLlmProvider : ILLMProvider
{
    private readonly ILLMProvider _primary;
    private readonly ILLMProvider _fallback;
    private readonly ILogger<FallbackLlmProvider>? _logger;

    public FallbackLlmProvider(
        ILLMProvider primary,
        ILLMProvider fallback,
        ILogger<FallbackLlmProvider>? logger = null)
    {
        _primary = primary;
        _fallback = fallback;
        _logger = logger;
    }

    public string Name => $"{_primary.Name}(主) → {_fallback.Name}(备)";

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        // 先试主模型
        try
        {
            return await _primary.CompleteAsync(prompt, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "主模型 {Name} 调用失败，降级到 {Fallback}",
                _primary.Name, _fallback.Name);
        }

        // 主模型失败，切兜底模型
        return await _fallback.CompleteAsync(prompt, ct);
    }
}
