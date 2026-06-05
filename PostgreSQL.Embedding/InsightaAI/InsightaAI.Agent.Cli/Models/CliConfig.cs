using System.Text.Json.Serialization;

namespace InsightaAI.Agent.Cli.Models;

/// <summary>
/// CLI 配置
/// </summary>
public class CliConfig
{
    /// <summary>
    /// 配置文件路径
    /// </summary>
    public static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".insightaai",
        "config.json");

    /// <summary>
    /// 会话存储目录
    /// </summary>
    public static readonly string SessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".insightaai",
        "sessions");

    /// <summary>
    /// 全局 Skills 目录（兼容主流工具的 .agents/skills 路径）
    /// </summary>
    public static readonly string GlobalSkillsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".agents",
        "skills");

    /// <summary>
    /// 项目级 Skills 目录（当前工作目录下）
    /// </summary>
    public static readonly string ProjectSkillsDir = Path.Combine(
        Directory.GetCurrentDirectory(),
        ".insighta",
        ".skills");

    /// <summary>
    /// 当前使用的 LLM 提供商
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "openai";

    /// <summary>
    /// 模型名称
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// OpenAI API Key
    /// </summary>
    [JsonPropertyName("openai_api_key")]
    public string? OpenAiApiKey { get; set; }

    /// <summary>
    /// OpenAI API Base URL（可选，用于自定义端点）
    /// </summary>
    [JsonPropertyName("openai_base_url")]
    public string? OpenAiBaseUrl { get; set; }

    /// <summary>
    /// Anthropic API Key
    /// </summary>
    [JsonPropertyName("anthropic_api_key")]
    public string? AnthropicApiKey { get; set; }

    /// <summary>
    /// Anthropic API Base URL（可选，用于自定义端点）
    /// </summary>
    [JsonPropertyName("anthropic_base_url")]
    public string? AnthropicBaseUrl { get; set; }

    /// <summary>
    /// Google Gemini API Key
    /// </summary>
    [JsonPropertyName("gemini_api_key")]
    public string? GeminiApiKey { get; set; }

    /// <summary>
    /// Google Gemini API Base URL（可选，用于自定义端点）
    /// </summary>
    [JsonPropertyName("gemini_base_url")]
    public string? GeminiBaseUrl { get; set; }

    /// <summary>
    /// 系统提示词
    /// </summary>
    [JsonPropertyName("system_prompt")]
    public string SystemPrompt { get; set; } = """
        You are a helpful AI assistant with access to various tools.

        When you need to use a tool:
        1. First, briefly explain what you're about to do (1-2 sentences)
        2. Then call the tool
        3. After getting the result, summarize or explain the outcome

        Keep your responses concise and conversational. Use the user's language to respond.
        """;

    /// <summary>
    /// 最大工具调用轮次
    /// </summary>
    [JsonPropertyName("max_tool_rounds")]
    public int MaxToolRounds { get; set; } = 10;

    /// <summary>
    /// 是否启用内置工具
    /// </summary>
    [JsonPropertyName("enable_builtin_tools")]
    public bool EnableBuiltInTools { get; set; } = true;

    /// <summary>
    /// 环境变量配置
    /// </summary>
    [JsonPropertyName("envs")]
    public Dictionary<string, string> Envs { get; set; } = [];

    /// <summary>
    /// 加载配置
    /// </summary>
    public static CliConfig Load()
    {
        if (File.Exists(ConfigPath))
        {
            var json = File.ReadAllText(ConfigPath);
            return System.Text.Json.JsonSerializer.Deserialize<CliConfig>(json) ?? new CliConfig();
        }
        return new CliConfig();
    }

    /// <summary>
    /// 保存配置
    /// </summary>
    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>
    /// 确保会话目录存在
    /// </summary>
    public static void EnsureSessionsDir()
    {
        if (!Directory.Exists(SessionsDir))
        {
            Directory.CreateDirectory(SessionsDir);
        }
    }
}
