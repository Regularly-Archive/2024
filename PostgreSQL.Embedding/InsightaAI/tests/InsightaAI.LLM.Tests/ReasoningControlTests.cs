using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Anthropic;
using InsightaAI.LLM.Gemini;
using InsightaAI.LLM.Models;
using InsightaAI.LLM.OpenAI;
using Xunit;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// 推理强度控制测试：语义模型校验、单一策略序列化、Budget 边界与 Off/ProviderDefault 语义位。
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

    // ── Anthropic：仅 Budget 策略 ──

    [Theory]
    [InlineData(ReasoningEffortLevel.Minimal)]
    [InlineData(ReasoningEffortLevel.Low)]
    [InlineData(ReasoningEffortLevel.Medium)]
    [InlineData(ReasoningEffortLevel.High)]
    [InlineData(ReasoningEffortLevel.XHigh)]
    public void Anthropic_Effort_Is_Rejected_Instead_Of_Being_Mapped_To_Budget(ReasoningEffortLevel level)
    {
        Assert.Throws<NotSupportedException>(() =>
            new AnthropicAdapter().CreateRequest(Request(level: level), TestConfig, stream: true));
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
    public async Task OpenAI_Effort_Does_Not_Inspect_The_Model_Name()
    {
        var body = await GetBodyAsync(new OpenAIAdapter(), Request(model: "custom-deployment", level: ReasoningEffortLevel.Medium));
        Assert.Equal("medium", body.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public void OpenAI_Budget_Is_Explicitly_Rejected()
    {
        Assert.Throws<NotSupportedException>(() =>
            new OpenAIAdapter().CreateRequest(
                Request(model: "custom-deployment", reasoning: ReasoningConfig.WithBudget(5000)),
                TestConfig,
                stream: true));
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

    // ── OpenAI-compatible endpoints：Off 由精确配置映射 ──

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

    [Fact]
    public async Task Gemini_Effort_Sends_ThinkingLevel()
    {
        var body = await GetBodyAsync(new GeminiAdapter(), Request(model: "gemini-custom", level: ReasoningEffortLevel.Low));

        Assert.Equal("low", body.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString());
    }

    [Theory]
    [InlineData(ReasoningEffortLevel.None)]
    [InlineData(ReasoningEffortLevel.XHigh)]
    public void Gemini_Unsupported_Effort_Is_Rejected(ReasoningEffortLevel level)
    {
        Assert.Throws<NotSupportedException>(() =>
            new GeminiAdapter().CreateRequest(Request(model: "gemini-custom", level: level), TestConfig, stream: true));
    }

    [Fact]
    public async Task Gemini_Budget_Sends_ThinkingBudget()
    {
        var body = await GetBodyAsync(new GeminiAdapter(), Request(model: "gemini-legacy", reasoning: ReasoningConfig.WithBudget(1024)));

        Assert.Equal(1024, body.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
    }

    // ── 辅助 ──

    private static LlmRequest Request(
        string model = "claude-sonnet-4-5",
        ReasoningConfig? reasoning = null,
        ReasoningEffortLevel? level = null) => LlmRequest.WithResolvedReasoning(
            new LlmRequest
            {
                Model = model,
                Messages = [Message.FromUser("Think step by step.")]
            },
            reasoning ?? (level is { } l ? ReasoningConfig.WithEffort(l) : null));
}
