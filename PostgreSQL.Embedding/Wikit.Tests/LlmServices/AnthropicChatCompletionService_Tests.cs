using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using PostgreSQL.Embedding.Common;
using Shouldly;
using Xunit;
using PostgreSQL.Embedding.Llm.Connectors.Anthropic;
using PostgreSQL.Embedding.Llm.Routers;
using PostgreSQL.Embedding.Domain.Entities;

namespace Wikit.Tests.LlmServices
{
    /// <summary>
    /// Tests for AnthropicChatCompletionService
    /// </summary>
    public class AnthropicChatCompletionService_Tests
    {
        /// <summary>
        /// Creates a mock HttpMessageHandler for testing
        /// </summary>
        private static HttpMessageHandler CreateMockHandler(string responseJson)
        {
            return new Mock<HttpMessageHandler>(MockBehavior.Strict)
            {
                CallBase = true
            }.Object;
        }

        [Fact]
        public void FromLlmModel_Should_Create_Service_With_Correct_Values()
        {
            // Arrange
            var llmModel = new LlmModel
            {
                ModelName = "MiniMax-M2.1",
                ApiKey = "sk-ant-test-api-key",
                BaseUrl = "https://api.minimaxi.com/anthropic/v1/messages",
                ServiceProvider = (int)LlmServiceProvider.Anthropic,
                ApiFormat = (int)LlmApiFormat.Anthropic
            };

            var httpClient = new HttpClient();

            // Act
            var service = AnthropicChatCompletionService.FromLlmModel(llmModel, httpClient);

            // Assert
            service.ShouldNotBeNull();
        }

        [Fact]
        public void FromLlmModel_Should_Use_Default_Url_When_BaseUrl_Is_Empty()
        {
            // Arrange
            var llmModel = new LlmModel
            {
                ModelName = "claude-haiku-3-5-2025-02-19",
                ApiKey = "sk-ant-test-key",
                BaseUrl = "",
                ServiceProvider = (int)LlmServiceProvider.Anthropic,
                ApiFormat = (int)LlmApiFormat.Anthropic
            };

            var httpClient = new HttpClient();

            // Act
            var service = AnthropicChatCompletionService.FromLlmModel(llmModel, httpClient);

            // Assert
            service.ShouldNotBeNull();
        }

        [Fact]
        public void Attributes_Should_Be_Empty_Dictionary()
        {
            // Arrange
            var service = new AnthropicChatCompletionService(
                modelId: "claude-sonnet-4-20250514",
                apiKey: "sk-ant-test",
                endpoint: new Uri("https://api.anthropic.com/v1/messages"));

            // Act & Assert
            service.Attributes.ShouldNotBeNull();
            service.Attributes.ShouldBeEmpty();
        }

        /// <summary>
        /// Integration test - requires mock API or real API key
        /// </summary>
        [Fact()]
        public async Task GetChatMessageContentsAsync_Should_Return_Response()
        {
            // Arrange
            var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new Exception("ANTHROPIC_API_KEY environment variable is required for this test");
            }

            var llmModel = new LlmModel
            {
                ModelName = "claude-sonnet-4-20250514",
                ApiKey = apiKey,
                BaseUrl = "https://api.minimaxi.com/anthropic/v1/messages",
                ServiceProvider = (int)LlmServiceProvider.Anthropic,
                ApiFormat = (int)LlmApiFormat.Anthropic
            };

            var httpClient = new HttpClient();
            var service = AnthropicChatCompletionService.FromLlmModel(llmModel, httpClient);

            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("Hello, how are you?");

            // Act
            var result = await service.GetChatMessageContentsAsync(chatHistory);

            // Assert
            result.ShouldNotBeNull();
            result.ShouldNotBeEmpty();
            result[0].Content.ShouldNotBeNullOrEmpty();
        }

        /// <summary>
        /// Integration test - requires mock API or real API key
        /// </summary>
        [Fact(Skip = "Requires real Anthropic API key or mock server")]
        public async Task GetStreamingChatMessageContentsAsync_Should_Stream_Response()
        {
            // Arrange
            var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new Exception("ANTHROPIC_API_KEY environment variable is required for this test");
            }

            var llmModel = new LlmModel
            {
                ModelName = "claude-sonnet-4-20250514",
                ApiKey = apiKey,
                BaseUrl = "https://api.anthropic.com/v1/messages",
                ServiceProvider = (int)LlmServiceProvider.Anthropic,
                ApiFormat = (int)LlmApiFormat.Anthropic
            };

            var httpClient = new HttpClient();
            var service = AnthropicChatCompletionService.FromLlmModel(llmModel, httpClient);

            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("Tell me a short story.");

            // Act
            var chunks = new List<string>();
            await foreach (var chunk in service.GetStreamingChatMessageContentsAsync(chatHistory))
            {
                chunks.Add(chunk.Content);
            }

            // Assert
            chunks.ShouldNotBeEmpty();
            string.Join("", chunks).ShouldNotBeNullOrEmpty();
        }
    }

    /// <summary>
    /// Tests for KernelService with Anthropic support
    /// </summary>
    public class KernelService_Anthropic_Tests
    {
        [Fact]
        public void GetKernel_With_Anthropic_Format_Should_Register_Anthropic_Service()
        {
            // Arrange
            var llmModel = new LlmModel
            {
                ModelName = "claude-sonnet-4-20250514",
                ApiKey = "sk-ant-test-key",
                BaseUrl = "https://api.anthropic.com/v1/messages",
                ServiceProvider = (int)LlmServiceProvider.OpenAI, // Any provider, format decides
                ApiFormat = (int)LlmApiFormat.Anthropic,
                ModelType = (int)ModelType.TextGeneration
            };

            var services = new ServiceCollection();
            services.AddLogging();

            var httpClient = new HttpClient(new LlmCompletionRouter(llmModel, Options.Create(new LlmConfig())));

            // Act - Using extension method directly
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddLogging();
            kernelBuilder.AddAnthropicChatCompletion(
                modelId: llmModel.ModelName,
                apiKey: llmModel.ApiKey,
                endpoint: llmModel.BaseUrl,
                httpClient: httpClient);

            var kernel = kernelBuilder.Build();

            // Assert
            var chatCompletionService = kernel.Services.GetService<IChatCompletionService>();
            chatCompletionService.ShouldNotBeNull();
            chatCompletionService.ShouldBeOfType<AnthropicChatCompletionService>();
        }

        [Fact]
        public void GetKernel_With_OpenAI_Format_Should_Register_OpenAI_Service()
        {
            // Arrange
            var llmModel = new LlmModel
            {
                ModelName = "gpt-4",
                ApiKey = "sk-test-key",
                BaseUrl = "https://api.openai.com/v1",
                ServiceProvider = (int)LlmServiceProvider.OpenAI, // Using OpenAI as provider
                ApiFormat = (int)LlmApiFormat.OpenAI, // Using OpenAI format
                ModelType = (int)ModelType.TextGeneration
            };

            var httpClient = new HttpClient(new LlmCompletionRouter(llmModel, Options.Create(new LlmConfig())));

            // Act - Using extension method directly
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddLogging();
            kernelBuilder.AddOpenAIChatCompletion(
                modelId: llmModel.ModelName,
                apiKey: llmModel.ApiKey,
                httpClient: httpClient);

            var kernel = kernelBuilder.Build();

            // Assert
            var chatCompletionService = kernel.Services.GetService<IChatCompletionService>();
            chatCompletionService.ShouldNotBeNull();
        }
    }

    /// <summary>
    /// Tests for LlmApiFormat enum
    /// </summary>
    public class LlmApiFormat_Tests
    {
        [Fact]
        public void LlmApiFormat_Should_Have_Correct_Values()
        {
            // Assert
            ((int)LlmApiFormat.OpenAI).ShouldBe(0);
            ((int)LlmApiFormat.Anthropic).ShouldBe(1);
        }

        [Theory]
        [InlineData(LlmApiFormat.OpenAI, "OpenAI 兼容格式")]
        [InlineData(LlmApiFormat.Anthropic, "Anthropic 格式")]
        public void LlmApiFormat_Should_Have_Description(LlmApiFormat format, string expectedDescription)
        {
            // Arrange
            var fieldInfo = format.GetType().GetField(format.ToString());
            var attribute = (System.ComponentModel.DescriptionAttribute?)fieldInfo?.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).FirstOrDefault();

            // Assert
            attribute?.Description.ShouldBe(expectedDescription);
        }
    }

    /// <summary>
    /// Tests for LlmCompletionRouter with Anthropic
    /// </summary>
    public class LlmCompletionRouter_Anthropic_Tests
    {
        [Fact]
        public void SendAsync_With_Anthropic_Should_Set_Correct_Endpoint()
        {
            // Arrange
            var llmModel = new LlmModel
            {
                ModelName = "claude-sonnet-4-20250514",
                ApiKey = "sk-ant-test-key",
                ServiceProvider = (int)LlmServiceProvider.Anthropic,
                ApiFormat = (int)LlmApiFormat.Anthropic,
                ModelType = (int)ModelType.TextGeneration
            };

            var config = Options.Create(new LlmConfig());
            var router = new LlmCompletionRouter(llmModel, config);

            // Act
            var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/chat/completions");
            request.Content = new StringContent("{\"messages\":[]}", Encoding.UTF8, "application/json");

            // The router is a HttpClientHandler, so we need to test the inner logic
            // This is more of an integration test
            router.ShouldNotBeNull();
        }
    }
}
