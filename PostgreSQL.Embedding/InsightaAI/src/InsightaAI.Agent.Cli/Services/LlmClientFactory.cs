using InsightaAI.LLM.Abstractions;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.LLM;
using InsightaAI.LLM.Anthropic;
using InsightaAI.LLM.Gemini;
using InsightaAI.LLM.OpenAI;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// LLM 客户端工厂（编排层）
/// </summary>
/// <remarks>
/// <see cref="InsightaAI.LLM.LlmClientFactory"/> 实例由 Host 容器提供（AddLlmClientFactory 注册，
/// 共享带 resilience 管道的命名 HttpClient），本静态类只负责 model 引用解析与 provider 配置组装。
/// </remarks>
public static class LlmClientFactory
{
    /// <summary>
    /// 根据配置创建 LLM 客户端（使用 primary_model）
    /// </summary>
    public static ILlmClient Create(InsightaAI.LLM.LlmClientFactory factory, AuthConfig auth, CliConfig config)
    {
        return Create(factory, auth, config, config.PrimaryModel);
    }

    /// <summary>
    /// 根据指定 model 引用创建 LLM 客户端（支持会话内切换模型）
    /// </summary>
    public static ILlmClient Create(InsightaAI.LLM.LlmClientFactory factory, AuthConfig auth, CliConfig config, string modelRef)
    {
        var (providerName, _) = CliConfig.ParseModelReference(modelRef);
        var provider = config.GetProvider(auth, providerName);

        var providerConfig = new ProviderConfig
        {
            ApiKey = provider.ApiKey
                ?? throw new InvalidOperationException(
                    $"API key not configured for provider '{providerName}'. Run 'config' to add it."),
            BaseUrl = provider.BaseUrl,
            Headers = provider.Headers
        };

        var model = config.GetModel(modelRef);
        var catalog = ModelReasoningCapabilityCatalog.LoadDefault();
        var resolver = new ModelReasoningResolver(modelRef, provider.Adapter, model, catalog);
        return factory.Create(provider.Adapter, providerConfig, [new ModelReasoningMiddleware(resolver)]);
    }
}
