using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Anthropic;
using InsightaAI.LLM.Gemini;
using InsightaAI.LLM.Models;
using InsightaAI.LLM.OpenAI;
using Xunit;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// 推理强度控制 M1 测试：语义模型校验、档位→预算映射、clamp、Off/ProviderDefault 语义位。
/// 设计见 docs/architecture/llm-reasoning-control-design.md
/// </summary>
public class ReasoningControlTests
{
    private static readonly ProviderConfig TestConfig = new() { ApiKey = "test-key" };

    private static async Task<JsonElement> GetBodyAsync(IProviderAdapter adapter, LlmRequest request)
    {
        var httpRequest = adapter.CreateRequest(request, TestConfig, stream: false);
        var json = await httpRequest.Content!.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    // ── Validate 校验 ──

    [Fact]
    public void Validate_Effort_Without_Level_Throws()
    {
        var config = new ReasoningConfig { Control = ReasoningControl.Effort };
        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void Validate_Budget_Without_Tokens_Throws()
    {
        var config = new ReasoningConfig { Control = ReasoningControl.Budget };
        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void Validate_Budget_NonPositive_Throws()
    {
        var config = new ReasoningConfig { Control = ReasoningControl.Budget, BudgetTokens = 0 };
        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [Fact]
    public void Validate_Legal_Combinations_Do_Not_Throw()
    {
        ReasoningConfig.Off().Validate();
        new ReasoningConfig().Validate(); // ProviderDefault
        ReasoningConfig.WithEffort(ReasoningEffortLevel.High).Validate();
        ReasoningConfig.WithBudget(5000).Validate();
    }

    // ── Anthropic：档位 → 预算映射表 ──

    [Theory]
    [InlineData(ReasoningEffortLevel.Minimal, 1024)]
    [InlineData(ReasoningEffortLevel.Low, 4096)]
    [InlineData(ReasoningEffortLevel.Medium, 10000)]
    [InlineData(ReasoningEffortLevel.High, 16000)]
    [InlineData(ReasoningEffortLevel.XHigh, 32000)]
    public async Task Anthropic_Effort_Maps_To_Budget(ReasoningEffortLevel level, int expectedBudget)
    {
        var body = await GetBodyAsync(new AnthropicAdapter(), Request(level: level));
        Assert.True(body.TryGetProperty("thinking", out var thinking));
        Assert.Equal("enabled", thinking.GetProperty("type").GetString());
        Assert.Equal(expectedBudget, thinking.GetProperty("budget_tokens").GetInt32());
    }

    [Fact]
    public async Task Anthropic_Budget_Passes_Through()
    {
        var body = await GetBodyAsync(new AnthropicAdapter(), Request(reasoning: ReasoningConfig.WithBudget(5000)));
        Assert.Equal(5000, body.GetProperty("thinking").GetProperty("budget_tokens").GetInt32());
    }

    [Fact]
    public async Task Anthropic_Budget_Clamps_To_Api_Minimum()
    {
        // API 下限 1024，低于下限的预算 clamp 到下限
        var body = await GetBodyAsync(new AnthropicAdapter(), Request(reasoning: ReasoningConfig.WithBudget(100)));
        Assert.Equal(1024, body.GetProperty("thinking").GetProperty("budget_tokens").GetInt32());
    }

    [Fact]
    public async Task Anthropic_Off_And_ProviderDefault_Omit_Thinking()
    {
        var adapter = new AnthropicAdapter();
        var off = await GetBodyAsync(adapter, Request(reasoning: ReasoningConfig.Off() with { OffMode = ReasoningOffMode.ThinkingDisabled }));
        var @default = await GetBodyAsync(adapter, Request(reasoning: new ReasoningConfig()));
        Assert.Equal("disabled", off.GetProperty("thinking").GetProperty("type").GetString());
        Assert.False(@default.TryGetProperty("thinking", out _));
    }

    [Fact]
    public async Task Anthropic_EffortNone_Omits_Thinking()
    {
        // Anthropic 无 effort=none 概念，档位驱动的关闭视同 Off
        var body = await GetBodyAsync(new AnthropicAdapter(), Request(level: ReasoningEffortLevel.None));
        Assert.False(body.TryGetProperty("thinking", out _));
    }

    // ── OpenAI Chat Completions：档位直传 ──

    [Theory]
    [InlineData(ReasoningEffortLevel.None, "none")]
    [InlineData(ReasoningEffortLevel.Minimal, "minimal")]
    [InlineData(ReasoningEffortLevel.Low, "low")]
    [InlineData(ReasoningEffortLevel.Medium, "medium")]
    [InlineData(ReasoningEffortLevel.High, "high")]
    [InlineData(ReasoningEffortLevel.XHigh, "xhigh")]
    public async Task OpenAI_Effort_Passes_Through(ReasoningEffortLevel level, string expected)
    {
        var body = await GetBodyAsync(new OpenAIAdapter(), Request(model: "gpt-5.2", level: level));
        Assert.Equal(expected, body.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task OpenAI_Off_Sends_None_While_ProviderDefault_Omits_ReasoningEffort()
    {
        var adapter = new OpenAIAdapter();
        var off = await GetBodyAsync(adapter, Request(model: "gpt-5.2", reasoning: ReasoningConfig.Off() with { OffMode = ReasoningOffMode.EffortNone }));
        var @default = await GetBodyAsync(adapter, Request(model: "gpt-5.2", reasoning: new ReasoningConfig()));
        Assert.Equal("none", off.GetProperty("reasoning_effort").GetString());
        Assert.False(@default.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task OpenAI_DeepSeek_Effort_Sets_Temperature_Zero()
    {
        var body = await GetBodyAsync(new OpenAIAdapter(), Request(model: "deepseek-reasoner", level: ReasoningEffortLevel.Medium));
        Assert.Equal(0, body.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task OpenAI_DeepSeek_EffortNone_Does_Not_Set_Temperature()
    {
        var body = await GetBodyAsync(new OpenAIAdapter(), Request(model: "deepseek-reasoner", level: ReasoningEffortLevel.None));
        Assert.False(body.TryGetProperty("temperature", out _));
    }

    [Fact]
    public async Task OpenAI_Budget_Is_Ignored()
    {
        // OpenAI 无原生预算参数，Budget 模式不发参数（不会触发 API 400）
        var body = await GetBodyAsync(new OpenAIAdapter(), Request(model: "gpt-5.2", reasoning: ReasoningConfig.WithBudget(5000)));
        Assert.False(body.TryGetProperty("reasoning_effort", out _));
    }

    // ── OpenAI Responses API ──

    [Fact]
    public async Task Responses_Effort_Passes_Through()
    {
        var body = await GetBodyAsync(new OpenAIResponseAdapter(), Request(model: "gpt-5.2", level: ReasoningEffortLevel.High));
        Assert.Equal("high", body.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task Responses_EffortNone_Sends_Explicit_None()
    {
        // o 系默认推理，显式 none 才能关闭；与"不传参数"语义不同
        var body = await GetBodyAsync(new OpenAIResponseAdapter(), Request(model: "gpt-5.2", level: ReasoningEffortLevel.None));
        Assert.Equal("none", body.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task Responses_Off_Sends_None_While_ProviderDefault_Omits_Reasoning()
    {
        var adapter = new OpenAIResponseAdapter();
        var off = await GetBodyAsync(adapter, Request(model: "gpt-5.2", reasoning: ReasoningConfig.Off() with { OffMode = ReasoningOffMode.EffortNone }));
        var @default = await GetBodyAsync(adapter, Request(model: "gpt-5.2", reasoning: new ReasoningConfig()));
        Assert.Equal("none", off.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.False(@default.TryGetProperty("reasoning", out _));
    }

    // ── GLM-5.3（OpenAI 兼容端点；档位集与统一集合不同）──

    [Theory]
    [InlineData(ReasoningEffortLevel.None, "none")]
    [InlineData(ReasoningEffortLevel.Minimal, "minimal")]
    [InlineData(ReasoningEffortLevel.Low, "high")]
    [InlineData(ReasoningEffortLevel.Medium, "high")]   // GLM 无 medium 档，向上靠齐
    [InlineData(ReasoningEffortLevel.High, "high")]
    [InlineData(ReasoningEffortLevel.XHigh, "max")]
    public async Task OpenAI_Glm_Effort_Maps_To_Glm_Levels(ReasoningEffortLevel level, string expected)
    {
        var body = await GetBodyAsync(new OpenAIAdapter(), Request(model: "glm-5.3", level: level));
        Assert.Equal(expected, body.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task OpenAI_Glm_Off_Sends_ThinkingDisabled()
    {
        var body = await GetBodyAsync(new OpenAIAdapter(), Request(model: "glm-5.3", reasoning: ReasoningConfig.Off() with { OffMode = ReasoningOffMode.ThinkingDisabled }));
        Assert.Equal("disabled", body.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("qwen3.8-max", "enable_thinking")]
    [InlineData("doubao-seed-2.1-pro", "thinking")]
    [InlineData("deepseek-reasoner", "thinking")]
    [InlineData("minimax-m3", "thinking")]
    public async Task OpenAICompatible_Off_Uses_ProviderSpecific_Control(string model, string property)
    {
        var body = await GetBodyAsync(new OpenAIAdapter(), Request(model: model, reasoning: ReasoningConfig.Off() with { OffMode = property == "thinking" ? ReasoningOffMode.ThinkingDisabled : ReasoningOffMode.EnableThinkingFalse }));
        if (property == "enable_thinking")
        {
            Assert.False(body.GetProperty(property).GetBoolean());
        }
        else
        {
            Assert.Equal("disabled", body.GetProperty(property).GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task OpenAICompatible_Off_Leaves_Unknown_Model_Unchanged()
    {
        var body = await GetBodyAsync(new OpenAIAdapter(), Request(model: "custom-gateway-model", reasoning: ReasoningConfig.Off()));
        Assert.False(body.TryGetProperty("reasoning_effort", out _));
        Assert.False(body.TryGetProperty("thinking", out _));
        Assert.False(body.TryGetProperty("enable_thinking", out _));
    }

    [Fact]
    public async Task Gemini_Off_Sends_Zero_ThinkingBudget_While_ProviderDefault_Omits_It()
    {
        var adapter = new GeminiAdapter();
        var off = await GetBodyAsync(adapter, Request(model: "gemini-2.5-flash", reasoning: ReasoningConfig.Off() with { OffMode = ReasoningOffMode.ThinkingBudgetZero }));
        var @default = await GetBodyAsync(adapter, Request(model: "gemini-2.5-flash", reasoning: new ReasoningConfig()));
        Assert.Equal(0, off.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
        Assert.False(@default.TryGetProperty("generationConfig", out _));
    }

    // ── 辅助 ──

    private static LlmRequest Request(
        string model = "claude-sonnet-4-5",
        ReasoningConfig? reasoning = null,
        ReasoningEffortLevel? level = null) => new()
    {
        Model = model,
        Messages = [Message.FromUser("Think step by step.")],
        Reasoning = reasoning ?? (level is { } l ? ReasoningConfig.WithEffort(l) : null)
    };
}
