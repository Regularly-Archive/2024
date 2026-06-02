using Microsoft.Extensions.Configuration;
using InsightaAI.LLM.Abstractions;

namespace InsightaAI.Tests.Shared;

/// <summary>
/// 测试配置 - 从环境变量或配置文件加载
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

    // ============================================================
    // OpenAI 配置
    // ============================================================

    /// <summary>OpenAI API Key</summary>
    public string? OpenAIApiKey => _configuration["OPENAI_API_KEY"];

    /// <summary>OpenAI Base URL (支持自定义端点)</summary>
    public string? OpenAIBaseUrl => _configuration["OPENAI_BASE_URL"];

    /// <summary>OpenAI 模型 ID</summary>
    public string OpenAIModel => _configuration["OPENAI_MODEL"] ?? "gpt-4o-mini";

    /// <summary>OpenAI 是否配置完整</summary>
    public bool HasOpenAI => !string.IsNullOrEmpty(OpenAIApiKey);

    // ============================================================
    // DeepSeek 配置
    // ============================================================

    /// <summary>DeepSeek API Key</summary>
    public string? DeepSeekApiKey => _configuration["DEEPSEEK_API_KEY"];

    /// <summary>DeepSeek Base URL</summary>
    public string? DeepSeekBaseUrl => _configuration["DEEPSEEK_BASE_URL"] ?? "https://api.deepseek.com";

    /// <summary>DeepSeek 模型 ID</summary>
    public string DeepSeekModel => _configuration["DEEPSEEK_MODEL"] ?? "deepseek-chat";

    /// <summary>DeepSeek 是否配置完整</summary>
    public bool HasDeepSeek => !string.IsNullOrEmpty(DeepSeekApiKey);

    // ============================================================
    // Anthropic 配置
    // ============================================================

    /// <summary>Anthropic API Key</summary>
    public string? AnthropicApiKey => _configuration["ANTHROPIC_API_KEY"];

    /// <summary>Anthropic Base URL (支持自定义端点)</summary>
    public string? AnthropicBaseUrl => _configuration["ANTHROPIC_BASE_URL"];

    /// <summary>Anthropic 模型 ID</summary>
    public string AnthropicModel => _configuration["ANTHROPIC_MODEL"] ?? "claude-sonnet-4-20250514";

    /// <summary>Anthropic 是否配置完整</summary>
    public bool HasAnthropic => !string.IsNullOrEmpty(AnthropicApiKey);

    // ============================================================
    // Gemini 配置
    // ============================================================

    /// <summary>Google Gemini API Key</summary>
    public string? GeminiApiKey => _configuration["GEMINI_API_KEY"];

    /// <summary>Google Gemini Base URL (支持自定义端点)</summary>
    public string? GeminiBaseUrl => _configuration["GEMINI_BASE_URL"];

    /// <summary>Google Gemini 模型 ID</summary>
    public string GeminiModel => _configuration["GEMINI_MODEL"] ?? "gemini-2.0-flash";

    /// <summary>Google Gemini 是否配置完整</summary>
    public bool HasGemini => !string.IsNullOrEmpty(GeminiApiKey);

    // ============================================================
    // 通用配置
    // ============================================================

    /// <summary>是否跳过真实的 API 调用 (用于 CI/CD)</summary>
    public bool SkipRealApiCalls => bool.TryParse(_configuration["SKIP_REAL_API"], out var skip) && skip;

    /// <summary>测试超时时间 (毫秒)</summary>
    public int TestTimeoutMs => int.TryParse(_configuration["TEST_TIMEOUT_MS"], out var timeout) ? timeout : 60000;

    // ============================================================
    // 辅助方法
    // ============================================================

    /// <summary>
    /// 获取 OpenAI ProviderConfig
    /// </summary>
    public ProviderConfig? GetOpenAIConfig()
    {
        if (!HasOpenAI) return null;
        return new ProviderConfig
        {
            ApiKey = OpenAIApiKey!,
            BaseUrl = OpenAIBaseUrl
        };
    }

    /// <summary>
    /// 获取 DeepSeek ProviderConfig (使用 OpenAI 适配器)
    /// </summary>
    public ProviderConfig? GetDeepSeekConfig()
    {
        if (!HasDeepSeek) return null;
        return new ProviderConfig
        {
            ApiKey = DeepSeekApiKey!,
            BaseUrl = DeepSeekBaseUrl
        };
    }

    /// <summary>
    /// 获取 Anthropic ProviderConfig
    /// </summary>
    public ProviderConfig? GetAnthropicConfig()
    {
        if (!HasAnthropic) return null;
        return new ProviderConfig
        {
            ApiKey = AnthropicApiKey!,
            BaseUrl = AnthropicBaseUrl
        };
    }

    /// <summary>
    /// 获取 Google Gemini ProviderConfig
    /// </summary>
    public ProviderConfig? GetGeminiConfig()
    {
        if (!HasGemini) return null;
        return new ProviderConfig
        {
            ApiKey = GeminiApiKey!,
            BaseUrl = GeminiBaseUrl
        };
    }
}
