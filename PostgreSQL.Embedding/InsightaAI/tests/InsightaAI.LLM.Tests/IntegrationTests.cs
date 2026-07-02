using InsightaAI.LLM.Abstractions;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.Tests.Shared;
using Xunit;

namespace InsightaAI.LLM.Tests;

/// <summary>
/// 集成测试 - 展示完整的工具调用流程
/// </summary>
public class IntegrationTests : TestBase
{
    [Fact]
    public async Task Full_Tool_Call_Flow_With_OpenAI()
    {
        if (!Config.HasOpenAI || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateOpenAIClient()!;
        var registry = new ToolRegistry();
        registry.Register(new CalculatorTool());

        // 第一轮：发送带工具的请求
        var request = new LlmRequest
        {
            Model = Config.OpenAIModel,
            Messages = [Message.FromUser("Calculate 123 * 456")],
            Tools = registry.GetDefinitions(),
            MaxTokens = 500
        };

        Console.WriteLine("=== Round 1: Initial Request ===");
        var stream = client.Streaming(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);

        // 检查是否有工具调用
        var toolCalls = response.GetToolCalls();
        if (toolCalls.Length > 0)
        {
            Console.WriteLine($"\n=== Tool Calls ({toolCalls.Length}) ===");

            // 第二轮：执行工具并返回结果
            var messages = new List<Message>
            {
                Message.FromUser("Calculate 123 * 456"),
                response.ToMessage() // 助手消息（包含工具调用）
            };

            foreach (var toolCall in toolCalls)
            {
                Console.WriteLine($"Executing: {toolCall.Name}({toolCall.Arguments})");

                var context = new ToolExecutionContext
                {
                    AgentId = "test-agent",
                    ToolCallId = toolCall.Id
                };

                var result = await registry.ExecuteAsync(toolCall, context);
                Console.WriteLine($"Result: {result.Content[0]}");

                messages.Add(Message.FromToolResult(
                    toolCall.Id,
                    toolCall.Name,
                    result.Content,
                    result.IsError
                ));
            }

            // 第三轮：带工具结果的请求
            Console.WriteLine("\n=== Round 2: With Tool Results ===");
            var followUpRequest = new LlmRequest
            {
                Model = Config.OpenAIModel,
                Messages = messages.ToArray(),
                Tools = registry.GetDefinitions(),
                MaxTokens = 500
            };

            var followUpStream = client.Streaming(followUpRequest);
            var finalResponse = await PrintStreamAsync(followUpStream);

            Assert.NotNull(finalResponse);
            Console.WriteLine("\n=== Final Answer ===");
            Console.WriteLine(finalResponse.GetTextContent());
        }
    }

    [Fact]
    public async Task Full_Tool_Call_Flow_With_Anthropic()
    {
        if (!Config.HasAnthropic || Config.SkipRealApiCalls)
        {
            return;
        }

        var client = CreateAnthropicClient()!;
        var registry = new ToolRegistry();
        registry.Register(new CalculatorTool());

        // 第一轮：发送带工具的请求
        var request = new LlmRequest
        {
            Model = Config.AnthropicModel,
            Messages = [Message.FromUser("Calculate 123 * 456")],
            Tools = registry.GetDefinitions(),
            MaxTokens = 500
        };

        Console.WriteLine("=== Round 1: Initial Request ===");
        var stream = client.Streaming(request);
        var response = await PrintStreamAsync(stream);

        Assert.NotNull(response);

        // 检查是否有工具调用
        var toolCalls = response.GetToolCalls();
        if (toolCalls.Length > 0)
        {
            Console.WriteLine($"\n=== Tool Calls ({toolCalls.Length}) ===");

            // 第二轮：执行工具并返回结果
            var messages = new List<Message>
            {
                Message.FromUser("Calculate 123 * 456"),
                response.ToMessage() // 助手消息（包含工具调用）
            };

            foreach (var toolCall in toolCalls)
            {
                Console.WriteLine($"Executing: {toolCall.Name}({toolCall.Arguments})");

                var context = new ToolExecutionContext
                {
                    AgentId = "test-agent",
                    ToolCallId = toolCall.Id
                };

                var result = await registry.ExecuteAsync(toolCall, context);
                Console.WriteLine($"Result: {result.Content[0]}");

                messages.Add(Message.FromToolResult(
                    toolCall.Id,
                    toolCall.Name,
                    result.Content,
                    result.IsError
                ));
            }

            // 第三轮：带工具结果的请求
            Console.WriteLine("\n=== Round 2: With Tool Results ===");
            var followUpRequest = new LlmRequest
            {
                Model = Config.AnthropicModel,
                Messages = messages.ToArray(),
                Tools = registry.GetDefinitions(),
                MaxTokens = 500
            };

            var followUpStream = client.Streaming(followUpRequest);
            var finalResponse = await PrintStreamAsync(followUpStream);

            Assert.NotNull(finalResponse);
            Console.WriteLine("\n=== Final Answer ===");
            Console.WriteLine(finalResponse.GetTextContent());
        }
    }

    [Fact]
    public void Factory_FromModel_Should_Parse_Correctly()
    {
        var (client1, model1) = Factory.FromModel("openai/gpt-4o", new ProviderConfig { ApiKey = "test" });
        Assert.Equal("openai", client1.ProviderName);
        Assert.Equal("gpt-4o", model1);

        var (client2, model2) = Factory.FromModel("anthropic/claude-3-opus", new ProviderConfig { ApiKey = "test" });
        Assert.Equal("anthropic", client2.ProviderName);
        Assert.Equal("claude-3-opus", model2);
    }

    [Fact]
    public void ProviderConfig_Should_Support_Custom_Headers()
    {
        var config = new ProviderConfig
        {
            ApiKey = "test-key",
            BaseUrl = "https://custom.api.com",
            Headers = new Dictionary<string, string>
            {
                ["X-Custom-Header"] = "custom-value"
            }
        };

        var client = Factory.Create("openai", config);
        Assert.NotNull(client);
    }
}
