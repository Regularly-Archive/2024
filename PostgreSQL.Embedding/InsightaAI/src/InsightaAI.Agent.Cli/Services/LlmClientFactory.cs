using InsightaAI.LLM.Abstractions;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.LLM;
using InsightaAI.LLM.Anthropic;
using InsightaAI.LLM.Gemini;
using InsightaAI.LLM.OpenAI;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// LLM 客户端工厂
/// </summary>
public static class LlmClientFactory
{
    /// <summary>
    /// 根据配置创建 LLM 客户端（使用 primary_model）
    /// </summary>
    public static ILlmClient Create(AuthConfig auth, CliConfig config)
    {
        var (providerName, _) = config.ParsePrimaryModel();
        return Create(auth, config, config.PrimaryModel);
    }

    /// <summary>
    /// 根据指定 model 引用创建 LLM 客户端（支持会话内切换模型）
    /// </summary>
    public static ILlmClient Create(AuthConfig auth, CliConfig config, string modelRef)
    {
        var (providerName, _) = CliConfig.ParseModelReference(modelRef);
        var provider = config.GetProvider(auth, providerName);

        var factory = new InsightaAI.LLM.LlmClientFactory();
        factory.RegisterAdapter(new OpenAIAdapter());
        factory.RegisterAdapter(new OpenAIResponseAdapter());
        factory.RegisterAdapter(new AnthropicAdapter());
        factory.RegisterAdapter(new GeminiAdapter());

        var providerConfig = new ProviderConfig
        {
            ApiKey = provider.ApiKey
                ?? throw new InvalidOperationException(
                    $"API key not configured for provider '{providerName}'. Run 'config' to add it."),
            BaseUrl = provider.BaseUrl,
            Headers = provider.Headers
        };

        return factory.Create(provider.Adapter, providerConfig);
    }
}
