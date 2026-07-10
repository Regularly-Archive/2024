using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace InsightaAI.Agent.Cli.Models;

/// <summary>
/// Provider 配置条目（存储在 auth.json 中）
/// </summary>
public class ProviderEntry
{
    /// <summary>
    /// 适配器类型：openai, anthropic, gemini
    /// </summary>
    [JsonPropertyName("adapter")]
    public string Adapter { get; set; } = "openai";

    /// <summary>
    /// API Key
    /// </summary>
    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// API Base URL（可选）
    /// </summary>
    [JsonPropertyName("base_url")]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 自定义 HTTP 请求头（可选）
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}

/// <summary>
/// Model 配置条目（存储在 config.json 中）
/// </summary>
public class ModelEntry
{
    /// <summary>
    /// 实际模型 ID（发送给 API 的 model 参数）
    /// </summary>
    [JsonPropertyName("model_id")]
    public string ModelId { get; set; } = "";

    /// <summary>
    /// 最大输出 token 数（可选）
    /// </summary>
    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 上下文窗口大小（可选，覆盖默认值）
    /// </summary>
    [JsonPropertyName("context_window")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ContextWindow { get; set; }
}

/// <summary>
/// 认证配置（~/.insighta/auth.json）
/// </summary>
public class AuthConfig
{
    public static readonly string AuthConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".insighta",
        "auth.json");

    /// <summary>
    /// Provider 配置字典，key 为 provider 名称
    /// </summary>
    [JsonPropertyName("providers")]
    public Dictionary<string, ProviderEntry> Providers { get; set; } = [];

    /// <summary>
    /// 加载认证配置
    /// </summary>
    public static AuthConfig Load()
    {
        if (File.Exists(AuthConfigPath))
        {
            var json = File.ReadAllText(AuthConfigPath);
            return System.Text.Json.JsonSerializer.Deserialize<AuthConfig>(json) ?? new AuthConfig();
        }
        return new AuthConfig();
    }

    /// <summary>
    /// 保存认证配置
    /// </summary>
    public void Save()
    {
        var dir = Path.GetDirectoryName(AuthConfigPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            SetDirectoryPermissions(dir);
        }

        var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(AuthConfigPath, json);
        SetFilePermissions(AuthConfigPath);
    }

    private static void SetDirectoryPermissions(string dirPath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var userName = Environment.UserName;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "icacls",
                    Arguments = $"\"{dirPath}\" /inheritance:r /grant:r \"{userName}:(OI)(CI)F\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = System.Diagnostics.Process.Start(psi);
                process?.WaitForExit(5000);
            }
            else
            {
                System.Diagnostics.Process.Start("chmod", "700 " + dirPath)?.WaitForExit(5000);
            }
        }
        catch { }
    }

    private static void SetFilePermissions(string filePath)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var userName = Environment.UserName;
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "icacls",
                    Arguments = $"\"{filePath}\" /inheritance:r /grant:r \"{userName}:F\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = System.Diagnostics.Process.Start(psi);
                process?.WaitForExit(5000);
            }
            else
            {
                System.Diagnostics.Process.Start("chmod", "600 " + filePath)?.WaitForExit(5000);
            }
        }
        catch { }
    }
}

/// <summary>
/// CLI 配置（~/.insighta/config.json）
/// </summary>
public class CliConfig
{
    /// <summary>
    /// 配置文件路径
    /// </summary>
    public static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".insighta",
        "config.json");

    /// <summary>
    /// 会话存储目录
    /// </summary>
    public static readonly string SessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".insighta",
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
    /// 模型配置字典，key 为 "provider/model_key" 格式
    /// </summary>
    [JsonPropertyName("models")]
    public Dictionary<string, ModelEntry> Models { get; set; } = [];

    /// <summary>
    /// 当前激活的模型，格式为 "provider/model_key"
    /// </summary>
    [JsonPropertyName("primary_model")]
    public string PrimaryModel { get; set; } = "openai/gpt-4o-mini";

    /// <summary>
    /// 摘要模型（可选，格式为 "provider/model_key"，用于上下文压缩）
    /// </summary>
    [JsonPropertyName("secondary_model")]
    public string? SecondaryModel { get; set; }

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
    /// 解析 primary_model，返回 (ProviderName, ModelKey)
    /// </summary>
    public (string ProviderName, string ModelKey) ParsePrimaryModel()
    {
        return ParseModelReference(PrimaryModel);
    }

    /// <summary>
    /// 解析 model 引用，返回 (ProviderName, ModelKey)
    /// </summary>
    public static (string ProviderName, string ModelKey) ParseModelReference(string modelRef)
    {
        var parts = modelRef.Split('/', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new InvalidOperationException(
                $"Invalid model reference: '{modelRef}'. Expected format: 'provider/model_key'.");
        return (parts[0], parts[1]);
    }

    /// <summary>
    /// 获取指定 provider 的 ProviderEntry
    /// </summary>
    public ProviderEntry GetProvider(AuthConfig auth, string providerName)
    {
        if (!auth.Providers.TryGetValue(providerName, out var provider))
            throw new InvalidOperationException(
                $"Provider '{providerName}' not found in auth config. Run 'config' to add it.");
        return provider;
    }

    /// <summary>
    /// 获取指定 model 引用的 ModelEntry
    /// </summary>
    public ModelEntry GetModel(string modelRef)
    {
        if (!Models.TryGetValue(modelRef, out var model))
            throw new InvalidOperationException(
                $"Model '{modelRef}' not found in config. Run 'config' to add it.");
        return model;
    }

    /// <summary>
    /// 解析 primary_model，返回 (ProviderEntry, ModelEntry)
    /// </summary>
    public (ProviderEntry Provider, ModelEntry Model) ResolvePrimaryModel(AuthConfig auth)
    {
        var (providerName, _) = ParsePrimaryModel();
        var provider = GetProvider(auth, providerName);
        var model = GetModel(PrimaryModel);
        return (provider, model);
    }

    /// <summary>
    /// 解析 secondary_model 的 ModelId（如果配置了的话）
    /// </summary>
    public string? ResolveSecondaryModelId()
    {
        if (string.IsNullOrWhiteSpace(SecondaryModel))
            return null;

        var model = GetModel(SecondaryModel);
        return model.ModelId;
    }

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
