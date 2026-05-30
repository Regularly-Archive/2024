using Microsoft.Extensions.Configuration;
using InsightaAI.LLM.Abstractions;

namespace InsightaAI.Agent.Tests;

/// <summary>
/// 测试配置 - 从环境变量或 appsettings.json 加载
/// </summary>
public class TestConfig
{
    private readonly IConfiguration _configuration;

    public TestConfig()
    {
        _configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    // OpenAI 配置
    public string? OpenAIApiKey => _configuration["OPENAI_API_KEY"];
    public string? OpenAIBaseUrl => _configuration["OPENAI_BASE_URL"];
    public string OpenAIModel => _configuration["OPENAI_MODEL"] ?? "gpt-4o";
    public bool HasOpenAI => !string.IsNullOrEmpty(OpenAIApiKey);

    // DeepSeek 配置
    public string? DeepSeekApiKey => _configuration["DEEPSEEK_API_KEY"];
    public string? DeepSeekBaseUrl => _configuration["DEEPSEEK_BASE_URL"];
    public string DeepSeekModel => _configuration["DEEPSEEK_MODEL"] ?? "deepseek-chat";
    public bool HasDeepSeek => !string.IsNullOrEmpty(DeepSeekApiKey);

    // Anthropic 配置
    public string? AnthropicApiKey => _configuration["ANTHROPIC_API_KEY"];
    public string? AnthropicBaseUrl => _configuration["ANTHROPIC_BASE_URL"];
    public string AnthropicModel => _configuration["ANTHROPIC_MODEL"] ?? "claude-3-5-sonnet";
    public bool HasAnthropic => !string.IsNullOrEmpty(AnthropicApiKey);

    // 通用配置
    public bool SkipRealApiCalls => bool.TryParse(_configuration["SKIP_REAL_API"], out var skip) && skip;
    public int TestTimeoutMs => int.TryParse(_configuration["TEST_TIMEOUT_MS"], out var timeout) ? timeout : 60000;

    // 获取配置对象
    public ProviderConfig? GetOpenAIConfig() => HasOpenAI
        ? new ProviderConfig
        {
            ApiKey = OpenAIApiKey!,
            BaseUrl = OpenAIBaseUrl
        }
        : null;

    public ProviderConfig? GetDeepSeekConfig() => HasDeepSeek
        ? new ProviderConfig
        {
            ApiKey = DeepSeekApiKey!,
            BaseUrl = DeepSeekBaseUrl
        }
        : null;

    public ProviderConfig? GetAnthropicConfig() => HasAnthropic
        ? new ProviderConfig
        {
            ApiKey = AnthropicApiKey!,
            BaseUrl = AnthropicBaseUrl
        }
        : null;
}
