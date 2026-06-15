using InsightaAI.LLM.Abstractions;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.LLM;
using InsightaAI.Agent.Abstractions;
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
    /// 根据配置创建 LLM 客户端
    /// </summary>
    public static ILlmClient Create(CliConfig config)
    {
        var factory = new InsightaAI.LLM.LlmClientFactory();

        // 注册适配器
        factory.RegisterAdapter(new OpenAIAdapter());
        factory.RegisterAdapter(new AnthropicAdapter());
        factory.RegisterAdapter(new GeminiAdapter());

        var providerConfig = BuildProviderConfig(config);
        return factory.Create(config.Provider, providerConfig);
    }

    private static ProviderConfig BuildProviderConfig(CliConfig config)
    {
        return config.Provider.ToLower() switch
        {
            "openai" => new ProviderConfig
            {
                ApiKey = config.OpenAiApiKey
                    ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? throw new InvalidOperationException(
                        "OpenAI API key is not configured. Set it via config or OPENAI_API_KEY environment variable."),
                BaseUrl = config.OpenAiBaseUrl
                    ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL")
            },
            "anthropic" => new ProviderConfig
            {
                ApiKey = config.AnthropicApiKey
                    ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                    ?? throw new InvalidOperationException(
                        "Anthropic API key is not configured. Set it via config or ANTHROPIC_API_KEY environment variable."),
               BaseUrl = config.AnthropicBaseUrl
                    ?? Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL")
            },
            "gemini" => new ProviderConfig
            {
                ApiKey = config.GeminiApiKey
                    ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                    ?? throw new InvalidOperationException(
                        "Gemini API key is not configured. Set it via config or GEMINI_API_KEY environment variable."),
                BaseUrl = config.GeminiBaseUrl
                    ?? Environment.GetEnvironmentVariable("GEMINI_BASE_URL")
            },
            _ => throw new NotSupportedException($"Provider '{config.Provider}' is not supported.")
        };
    }
}
