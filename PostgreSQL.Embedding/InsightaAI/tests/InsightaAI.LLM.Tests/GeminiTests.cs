using InsightaAI.LLM.Abstractions;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using Xunit;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// Google Gemini 适配器测试
/// </summary>
public class GeminiTests : TestBase
{
    [Fact]
    public void Factory_Should_Register_Gemini_Adapter()
    {
        Assert.True(Factory.HasProvider("gemini"));
    }

    [Fact]
    public void Factory_Should_Create_Gemini_Client()
    {
        if (!Config.HasGemini)
        {
            // 仅验证工厂逻辑
            var config = new ProviderConfig { ApiKey = "test-key" };
            var client = Factory.Create("gemini", config);
            Assert.NotNull(client);
            Assert.Equal("gemini", client.ProviderName);
            return;
        }

        var realClient = CreateGeminiClient();
        Assert.NotNull(realClient);
    }

    [Fact]
    public async Task Gemini_Should_Stream_Simple_Completion()
    {
        if (!Config.HasGemini || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateGeminiClient()!;
        var request = new LlmRequest
        {
            Model = Config.GeminiModel,
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
    public async Task Gemini_Should_Handle_Tool_Calls()
    {
        if (!Config.HasGemini || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateGeminiClient()!;
        var request = new LlmRequest
        {
            Model = Config.GeminiModel,
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
    public async Task Gemini_Should_Complete_Without_Streaming()
    {
        if (!Config.HasGemini || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateGeminiClient()!;
        var request = new LlmRequest
        {
            Model = Config.GeminiModel,
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

    [Fact]
    public async Task Gemini_Should_Handle_System_Prompt()
    {
        if (!Config.HasGemini || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateGeminiClient()!;
        var request = new LlmRequest
        {
            Model = Config.GeminiModel,
            Messages =
            [
                Message.FromSystem("You are a pirate. Always respond with 'Arrr!'"),
                Message.FromUser("Hello!")
            ],
            MaxTokens = 50
        };

        var stream = client.Streaming(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Content);
    }
}
