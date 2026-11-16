namespace IndieRadar.Domain.Interfaces;

/// <summary>
/// 大模型的统一抽象。这是"动态换模型"的关键：
/// AI 加工服务只认这个接口，具体是 Hy4 flash 还是 GLM-4-Flash 由配置决定。
/// </summary>
public interface ILLMProvider
{
    /// <summary>提供方名称（用于日志和调试）。</summary>
    string Name { get; }

    /// <summary>
    /// 让 AI 判断文章与"独立开发风向"的相关度并生成中文加工结果。
    /// 返回结构化 JSON 字符串，由调用方解析，避免依赖特定 SDK。
    /// </summary>
    /// <param name="prompt">完整的提示词（含待分析的文章内容）。</param>
    Task<string> CompleteAsync(string prompt, CancellationToken ct = default);
}
