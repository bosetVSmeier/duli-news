namespace IndieRadar.Domain.Entities;

/// <summary>
/// 大模型提供方配置。这是"动态换模型"的核心——换模型只需改这条配置，业务代码不动。
/// 因为主流模型（Hy4 flash / GLM-4-Flash）都兼容 OpenAI 协议，所以统一用三件套描述：
/// BaseUrl + ApiKey + ModelId。
/// </summary>
public sealed class LlmProviderConfig
{
    /// <summary>配置名称，例如 "hy4flash"、"glm4free"。</summary>
    public required string Name { get; init; }

    /// <summary>是否启用。同一时刻只应有一个启用。</summary>
    public bool Enabled { get; init; }

    /// <summary>API 根地址。例：https://api.xxx.com/v1</summary>
    public required string BaseUrl { get; init; }

    /// <summary>API 密钥。生产环境应从环境变量注入，不写死在配置里。</summary>
    public required string ApiKey { get; init; }

    /// <summary>模型标识。例：hy4-flash、glm-4-flash。</summary>
    public required string ModelId { get; init; }

    /// <summary>是否为兜底模型（主模型不可用时自动降级到它）。</summary>
    public bool IsFallback { get; init; }
}
