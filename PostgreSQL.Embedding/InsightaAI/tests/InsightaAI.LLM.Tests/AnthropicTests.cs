using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using Xunit;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// Anthropic 适配器测试
/// </summary>
public class AnthropicTests : TestBase
{
    [Fact]
    public void Factory_Should_Register_Anthropic_Adapter()
    {
        Assert.True(Factory.HasProvider("anthropic"));
    }

    [Fact]
    public void Factory_Should_Create_Anthropic_Client()
    {
        if (!Config.HasAnthropic)
        {
            var config = new ProviderConfig { ApiKey = "test-key" };
            var client = Factory.Create("anthropic", config);
            Assert.NotNull(client);
            Assert.Equal("anthropic", client.ProviderName);
            Assert.True(client.SupportsReasoning);
            return;
        }

        var realClient = CreateAnthropicClient();
        Assert.NotNull(realClient);
    }

    [Fact]
    public async Task Anthropic_Should_Stream_Simple_Completion()
    {
        if (!Config.HasAnthropic || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateAnthropicClient()!;
        var request = new LlmRequest
        {
            Model = Config.AnthropicModel,
            Messages = [Message.FromUser("Say 'Hello, World!' and nothing else.")],
            MaxTokens = 50
        };

        var stream = client.Stream(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
        Assert.Contains("Hello", response.GetTextContent());
    }

    [Fact]
    public async Task Anthropic_Should_Handle_Tool_Calls()
    {
        if (!Config.HasAnthropic || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateAnthropicClient()!;
        var request = new LlmRequest
        {
            Model = Config.AnthropicModel,
            Messages = [Message.FromUser("What's the weather in Tokyo?")],
            Tools = [CreateWeatherTool()],
            MaxTokens = 200
        };

        var stream = client.Stream(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);
        Assert.True(response.GetToolCalls().Length > 0, "Expected tool calls in response");

        var toolCall = response.GetToolCalls()[0];
        Assert.Equal("get_weather", toolCall.Name);
    }

    [Fact]
    public async Task Anthropic_Should_Support_Extended_Thinking()
    {
        if (!Config.HasAnthropic || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateAnthropicClient()!;
        var request = new LlmRequest
        {
            Model = Config.AnthropicModel,
            Messages = [Message.FromUser("What is 15 * 17? Think step by step.")],
            Reasoning = new ReasoningConfig
            {
                Enabled = true,
                BudgetTokens = 5000
            },
            MaxTokens = 500
        };

        var stream = client.Stream(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);

        // Claude with extended thinking should have thinking content
        var thinking = response.GetThinkingContent();
        Console.WriteLine($"\n[Thinking Content Length]: {thinking?.Length ?? 0}");

        // 如果启用了 thinking，应该有思考内容
        if (thinking != null)
        {
            Assert.NotEmpty(thinking);
        }
    }

    [Fact]
    public async Task Anthropic_Should_Complete_Without_Streaming()
    {
        if (!Config.HasAnthropic || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateAnthropicClient()!;
        var request = new LlmRequest
        {
            Model = Config.AnthropicModel,
            Messages = [Message.FromUser("Say 'OK' and nothing else.")],
            MaxTokens = 50  // Anthropic needs more tokens for thinking
        };

        var response = await client.CompleteAsync(request);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);

        // 检查文本内容或思考内容
        var text = response.GetTextContent();
        var thinking = response.GetThinkingContent();
        Console.WriteLine($"[Response Text]: '{text}'");
        Console.WriteLine($"[Thinking Length]: {thinking?.Length ?? 0}");

        Assert.True(!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(thinking),
            "Expected either text or thinking content in response");
    }
}
