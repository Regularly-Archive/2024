using InsightaAI.LLM.Abstractions;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using Xunit;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// OpenAI 适配器测试
/// </summary>
public class OpenAITests : TestBase
{
    [Fact]
    public void Factory_Should_Register_OpenAI_Adapter()
    {
        Assert.True(Factory.HasProvider("openai"));
    }

    [Fact]
    public void Factory_Should_Create_OpenAI_Client()
    {
        if (!Config.HasOpenAI)
        {
            // 仅验证工厂逻辑
            var config = new ProviderConfig { ApiKey = "test-key" };
            var client = Factory.Create("openai", config);
            Assert.NotNull(client);
            Assert.Equal("openai", client.ProviderName);
            return;
        }

        var realClient = CreateOpenAIClient();
        Assert.NotNull(realClient);
    }

    [Fact]
    public async Task OpenAI_Should_Stream_Simple_Completion()
    {
        if (!Config.HasOpenAI || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateOpenAIClient()!;
        var request = new LlmRequest
        {
            Model = Config.OpenAIModel,
            Messages = [Message.FromUser("Say 'Hello, World!' and nothing else.")],
            MaxTokens = 50
        };

        var stream = client.Streaming(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
        Assert.Contains("Hello", response.GetTextContent());
    }

    [Fact]
    public async Task OpenAI_Should_Handle_Tool_Calls()
    {
        if (!Config.HasOpenAI || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateOpenAIClient()!;
        var request = new LlmRequest
        {
            Model = Config.OpenAIModel,
            Messages = [Message.FromUser("What's the weather in Beijing?")],
            Tools = [CreateWeatherTool()],
            MaxTokens = 200
        };

        var stream = client.Streaming(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);
        Assert.True(response.GetToolCalls().Length > 0, "Expected tool calls in response");

        var toolCall = response.GetToolCalls()[0];
        Assert.Equal("get_weather", toolCall.Name);
    }

    [Fact]
    public async Task DeepSeek_Should_Stream_With_Reasoning()
    {
        if (!Config.HasDeepSeek || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateDeepSeekClient()!;
        var request = new LlmRequest
        {
            Model = Config.DeepSeekModel,
            Messages = [Message.FromUser("What is 2+5? Think step by step.")],
            Reasoning = new ReasoningConfig { Enabled = true },
            MaxTokens = 500
        };

        var stream = client.Streaming(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
        Console.WriteLine(response.GetTextContent());

        // DeepSeek R1 应该有 thinking 内容
        var thinking = response.GetThinkingContent();
        Console.WriteLine($"\n[Thinking Content Length]: {thinking?.Length ?? 0}");
    }

    [Fact]
    public async Task OpenAI_Should_Complete_Without_Streaming()
    {
        if (!Config.HasOpenAI || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateOpenAIClient()!;
        var request = new LlmRequest
        {
            Model = Config.OpenAIModel,
            Messages = [Message.FromUser("Say 'OK' and nothing else.")],
            MaxTokens = 50
        };

        var response = await client.CompleteAsync(request);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);

        var text = response.GetTextContent();
        Console.WriteLine($"[Response Text]: '{text}'");

        Assert.False(string.IsNullOrEmpty(text), "Expected text content in response");
    }
}
