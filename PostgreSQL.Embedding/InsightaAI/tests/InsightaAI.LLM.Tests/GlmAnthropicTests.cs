using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using Xunit;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// GLM-5.3 经 Anthropic 兼容端点的集成测试。
/// 验证推理强度控制（档位→预算映射、Budget 直通）经 GLM Anthropic 兼容层的真实行为。
/// 环境变量：GLM_ANTHROPIC_API_KEY（必需）、GLM_ANTHROPIC_BASE_URL（默认 https://open.bigmodel.cn/api/anthropic）、GLM_ANTHROPIC_MODEL（默认 glm-5.3）
/// </summary>
public class GlmAnthropicTests : TestBase
{
    [Fact]
    public void Factory_Should_Create_Glm_Anthropic_Client()
    {
        if (!Config.HasGlmAnthropic)
        {
            // 未配置 key 时验证构造路径（不发起真实请求）
            var config = new InsightaAI.LLM.Abstractions.ProviderConfig
            {
                ApiKey = "test-key",
                BaseUrl = "https://open.bigmodel.cn/api/anthropic"
            };
            var client = Factory.Create("anthropic", config);
            Assert.NotNull(client);
            return;
        }

        var realClient = CreateGlmAnthropicClient();
        Assert.NotNull(realClient);
    }

    [Fact]
    public async Task Glm_Effort_Should_Stream_Thinking_Content()
    {
        if (!Config.HasGlmAnthropic || Config.SkipRealApiCalls) return;

        var client = CreateGlmAnthropicClient()!;
        // Effort=Low 经映射表落为 budget_tokens=4096；Anthropic 协议要求 budget < max_tokens
        var request = new LlmRequest
        {
            Model = Config.GlmAnthropicModel,
            Messages = [Message.FromUser("What is 27 * 43? Think step by step.")],
            Reasoning = ReasoningConfig.WithEffort(ReasoningEffortLevel.Low),
            MaxTokens = 8192
        };

        var stream = client.Streaming(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);

        // 档位驱动的思考应产出思考内容；GLM 兼容层若不兑现 budget_tokens 此处会红
        var thinking = response.GetThinkingContent();
        Assert.False(string.IsNullOrEmpty(thinking),
            $"GLM Anthropic 兼容层未返回思考内容 (Effort=Low → budget 4096)。响应 blocks: {response.Content.Length}");
    }

    [Fact]
    public async Task Glm_Budget_Should_Stream_Thinking_Content()
    {
        if (!Config.HasGlmAnthropic || Config.SkipRealApiCalls) return;

        var client = CreateGlmAnthropicClient()!;
        var request = new LlmRequest
        {
            Model = Config.GlmAnthropicModel,
            Messages = [Message.FromUser("What is 15 * 17? Think step by step.")],
            Reasoning = ReasoningConfig.WithBudget(2048),
            MaxTokens = 4096
        };

        var stream = client.Streaming(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);

        var thinking = response.GetThinkingContent();
        Assert.False(string.IsNullOrEmpty(thinking),
            $"GLM Anthropic 兼容层未返回思考内容 (Budget=2048 直通)。响应 blocks: {response.Content.Length}");
    }

    [Fact]
    public async Task Glm_ProviderDefault_Should_Stream()
    {
        if (!Config.HasGlmAnthropic || Config.SkipRealApiCalls) return;

        var client = CreateGlmAnthropicClient()!;
        // 不传 Reasoning：GLM-5.3 强制开启深度思考（Anthropic 协议下不发 thinking 参数）
        var request = new LlmRequest
        {
            Model = Config.GlmAnthropicModel,
            Messages = [Message.FromUser("用一句话介绍西安。")],
            MaxTokens = 4096
        };

        var stream = client.Streaming(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);

        // 行为观察：GLM 强制思考，默认档 max 下思考流是否经 Anthropic 协议透出
        var thinking = response.GetThinkingContent();
        Console.WriteLine($"\n[ProviderDefault Thinking Length]: {thinking?.Length ?? 0}");
    }
}
