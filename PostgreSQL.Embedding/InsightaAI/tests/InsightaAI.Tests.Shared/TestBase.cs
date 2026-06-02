using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.LLM.OpenAI;
using InsightaAI.LLM.Anthropic;
using InsightaAI.LLM.Gemini;
using System.Text.Json;
using InsightaAI.LLM;

namespace InsightaAI.Tests.Shared;

/// <summary>
/// 测试基类 - 提供通用的测试工具和辅助方法
/// </summary>
public abstract class TestBase
{
    protected readonly TestConfig Config;
    protected readonly LlmClientFactory Factory;

    protected TestBase()
    {
        Config = new TestConfig();
        Factory = new LlmClientFactory();
        Factory.RegisterAdapter(new OpenAIAdapter());
        Factory.RegisterAdapter(new AnthropicAdapter());
        Factory.RegisterAdapter(new GeminiAdapter());
    }

    /// <summary>
    /// 创建 OpenAI 客户端
    /// </summary>
    protected ILlmClient? CreateOpenAIClient()
    {
        var config = Config.GetOpenAIConfig();
        if (config == null) return null;
        return Factory.Create("openai", config);
    }

    /// <summary>
    /// 创建 DeepSeek 客户端 (使用 OpenAI 适配器)
    /// </summary>
    protected ILlmClient? CreateDeepSeekClient()
    {
        var config = Config.GetDeepSeekConfig();
        if (config == null) return null;
        return Factory.Create("openai", config);
    }

    /// <summary>
    /// 创建 Anthropic 客户端
    /// </summary>
    protected ILlmClient? CreateAnthropicClient()
    {
        var config = Config.GetAnthropicConfig();
        if (config == null) return null;
        return Factory.Create("anthropic", config);
    }

    /// <summary>
    /// 创建 Google Gemini 客户端
    /// </summary>
    protected ILlmClient? CreateGeminiClient()
    {
        var config = Config.GetGeminiConfig();
        if (config == null) return null;
        return Factory.Create("gemini", config);
    }

    /// <summary>
    /// 创建简单的测试工具定义
    /// </summary>
    protected static ToolDefinition CreateWeatherTool()
    {
        return new ToolDefinition
        {
            Name = "get_weather",
            Description = "Get the current weather for a location",
            Schema = JsonSerializer.Deserialize<JsonElement>(@"{
                ""type"": ""object"",
                ""properties"": {
                    ""location"": {
                        ""type"": ""string"",
                        ""description"": ""City name, e.g., Beijing""
                    }
                },
                ""required"": [""location""]
            }")
        };
    }

    /// <summary>
    /// 创建计算器工具执行器
    /// </summary>
    protected static IToolExecutor CreateCalculatorTool()
    {
        return new CalculatorTool();
    }

    /// <summary>
    /// 打印流式事件
    /// </summary>
    protected static async Task<LlmResponse> PrintStreamAsync(LlmStream stream)
    {
        await foreach (var evt in stream)
        {
            switch (evt)
            {
                case StreamStartEvent start:
                    Console.WriteLine($"[Start] Model: {start.Model}, Provider: {start.Provider}");
                    break;

                case TextDeltaEvent text:
                    Console.Write(text.Delta);
                    break;

                case ThinkingDeltaEvent thinking:
                    Console.Write($"[Thinking] {thinking.Delta}");
                    break;

                case ToolCallStartEvent toolStart:
                    Console.WriteLine($"\n[ToolCall Start] {toolStart.ToolName}");
                    break;

                case ToolCallDeltaEvent toolDelta:
                    Console.Write(toolDelta.ArgumentsDelta);
                    break;

                case ToolCallEndEvent toolEnd:
                    Console.WriteLine($"\n[ToolCall End] {toolEnd.ToolCall.Name}({toolEnd.ToolCall.Arguments})");
                    break;

                case DoneEvent done:
                    Console.WriteLine($"\n[Done] Reason: {done.Reason}");
                    Console.WriteLine($"\n[Usage] Input: {done.Usage?.InputTokens}, Output: {done.Usage?.OutputTokens}");
                    break;

                case ErrorEvent error:
                    Console.WriteLine($"\n[Error] {error.Error.Message}");
                    break;
            }
        }

        return await stream.GetResponseAsync();
    }
}
