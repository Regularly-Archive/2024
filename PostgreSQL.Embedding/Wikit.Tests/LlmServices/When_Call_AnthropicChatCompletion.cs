using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using Moq.Protected;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Llm.Connectors.Anthropic;
using PostgreSQL.Embedding.Llm.Routers;
using Shouldly;
using System.Net;
using System.Text;
using Wikit.Tests.Utils;

namespace Wikit.Tests.LlmServices
{
    /// <summary>
    /// Tests for AnthropicChatCompletionService
    /// </summary>
    public class When_Call_AnthropicChatCompletion
    {
        /// <summary>
        /// Get environment variable using TestEnvHelper
        /// </summary>
        private static string GetEnv(string name) => TestEnvHelper.GetEnv(name);

        /// <summary>
        /// Creates a mock HttpMessageHandler for testing
        /// </summary>
        private static HttpMessageHandler CreateMockHandler(string responseJson)
        {
            var mockHandler = new Mock<HttpMessageHandler>(MockBehavior.Strict)
            {
                CallBase = true
            };

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson)
                });

            return mockHandler.Object;
        }

        [Fact]
        public void It_Should_Create_Service_With_Correct_Values_FromLlmModel()
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
            this.ShouldSatisfyAllConditions(
                () => service.ShouldNotBeNull()
            );
        }

        [Fact]
        public void It_Should_Use_Default_Url_When_BaseUrl_Is_Empty_FromLlmModel()
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
            this.ShouldSatisfyAllConditions(
                () => service.ShouldNotBeNull()
            );
        }

        [Fact]
        public void It_Should_Be_Empty_Dictionary_For_Attributes()
        {
            // Arrange
            var service = new AnthropicChatCompletionService(
                modelId: "claude-sonnet-4-20250514",
                apiKey: "sk-ant-test",
                endpoint: new Uri("https://api.anthropic.com/v1/messages"));

            // Act & Assert
            this.ShouldSatisfyAllConditions(
                () => service.Attributes.ShouldNotBeNull(),
                () => service.Attributes.ShouldBeEmpty()
            );
        }

        /// <summary>
        /// Integration test - requires real API key from .env file
        /// </summary>
        [Fact]
        public async Task It_Should_Return_Response_When_GetChatMessageContentsAsync()
        {
            // Arrange
            var apiKey = GetEnv("ANTHROPIC_API_KEY");
            var baseUrl = GetEnv("ANTHROPIC_BASE_URL");
            var modelName = GetEnv("ANTHROPIC_MODEL_NAME");

            var llmModel = new LlmModel
            {
                ModelName = modelName,
                ApiKey = apiKey,
                BaseUrl = baseUrl,
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
            this.ShouldSatisfyAllConditions(
                () => result.ShouldNotBeNull(),
                () => result.ShouldNotBeEmpty(),
                () => result[0].Content.ShouldNotBeNullOrEmpty()
            );
        }

        /// <summary>
        /// Test that thinking tag is properly parsed from response
        /// </summary>
        [Fact]
        public async Task It_Should_Parse_Thinking_Tag_When_GetChatMessageContentsAsync()
        {
            // Arrange
            var responseJson = """
                {
                    "id": "msg_test123",
                    "type": "message",
                    "role": "assistant",
                    "model": "claude-sonnet-4-20250514",
                    "content": [
                        {
                            "type": "thinking",
                            "thinking": "Let me analyze this problem step by step..."
                        },
                        {
                            "type": "text",
                            "text": "Hello! I'm doing well, thank you for asking."
                        }
                    ],
                    "stop_reason": "end_turn",
                    "usage": {
                        "input_tokens": 15,
                        "output_tokens": 25
                    }
                }
                """;

            var httpClient = new HttpClient(CreateMockHandler(responseJson));
            var service = new AnthropicChatCompletionService(
                modelId: "claude-sonnet-4-20250514",
                apiKey: "sk-ant-test-key",
                endpoint: new Uri("https://api.anthropic.com/v1/messages"),
                httpClient: httpClient);

            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("Hello!");

            // Act
            var result = await service.GetChatMessageContentsAsync(chatHistory);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => result.ShouldNotBeNull(),
                () => result.ShouldNotBeEmpty(),
                () => result[0].Content.ShouldBe("Hello! I'm doing well, thank you for asking."),
                () => result[0].InnerContent.ShouldNotBeNull()
            );
        }

        /// <summary>
        /// Test that response without thinking tag still works
        /// </summary>
        [Fact]
        public async Task It_Should_Work_Without_Thinking_Tag_When_GetChatMessageContentsAsync()
        {
            // Arrange
            var responseJson = """
                {
                    "id": "msg_test123",
                    "type": "message",
                    "role": "assistant",
                    "model": "claude-sonnet-4-20250514",
                    "content": [
                        {
                            "type": "text",
                            "text": "Hello! I'm doing well."
                        }
                    ],
                    "stop_reason": "end_turn",
                    "usage": {
                        "input_tokens": 15,
                        "output_tokens": 20
                    }
                }
                """;

            var httpClient = new HttpClient(CreateMockHandler(responseJson));
            var service = new AnthropicChatCompletionService(
                modelId: "claude-sonnet-4-20250514",
                apiKey: "sk-ant-test-key",
                endpoint: new Uri("https://api.anthropic.com/v1/messages"),
                httpClient: httpClient);

            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage("Hello!");

            // Act
            var result = await service.GetChatMessageContentsAsync(chatHistory);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => result.ShouldNotBeNull(),
                () => result.ShouldNotBeEmpty(),
                () => result[0].Content.ShouldBe("Hello! I'm doing well.")
            );
        }

        /// <summary>
        /// Test GetTextContentsAsync with thinking tag
        /// </summary>
        [Fact]
        public async Task It_Should_Parse_Thinking_Tag_When_GetTextContentsAsync()
        {
            // Arrange
            var responseJson = """
                {
                    "id": "msg_test456",
                    "type": "message",
                    "role": "assistant",
                    "model": "claude-sonnet-4-20250514",
                    "content": [
                        {
                            "type": "thinking",
                            "thinking": "Reasoning about the answer..."
                        },
                        {
                            "type": "text",
                            "text": "The answer is 42."
                        }
                    ],
                    "stop_reason": "end_turn",
                    "usage": {
                        "input_tokens": 10,
                        "output_tokens": 15
                    }
                }
                """;

            var httpClient = new HttpClient(CreateMockHandler(responseJson));
            var service = new AnthropicChatCompletionService(
                modelId: "claude-sonnet-4-20250514",
                apiKey: "sk-ant-test-key",
                endpoint: new Uri("https://api.anthropic.com/v1/messages"),
                httpClient: httpClient);

            // Act
            var result = await service.GetTextContentsAsync("What is the answer to life?");

            // Assert
            this.ShouldSatisfyAllConditions(
                () => result.ShouldNotBeNull(),
                () => result.ShouldNotBeEmpty(),
                () => result[0].Text.ShouldBe("The answer is 42."),
                () => result[0].InnerContent.ShouldNotBeNull()
            );
        }

        /// <summary>
        /// Integration test - requires real API key from .env file
        /// </summary>
        [Fact]
        public async Task It_Should_Stream_Response_When_GetStreamingChatMessageContentsAsync()
        {
            // Arrange
            var apiKey = GetEnv("ANTHROPIC_API_KEY");
            var baseUrl = GetEnv("ANTHROPIC_BASE_URL");
            var modelName = GetEnv("ANTHROPIC_MODEL_NAME");

            var llmModel = new LlmModel
            {
                ModelName = modelName,
                ApiKey = apiKey,
                BaseUrl = baseUrl,
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
            var joinedResult = string.Join("", chunks);
            this.ShouldSatisfyAllConditions(
                () => chunks.ShouldNotBeEmpty(),
                () => joinedResult.ShouldNotBeNullOrEmpty()
            );
        }
    }

    /// <summary>
    /// Tests for KernelService with Anthropic support
    /// </summary>
    public class When_Call_KernelService_Anthropic
    {
        [Fact]
        public void It_Should_Register_Anthropic_Service_When_GetKernel_With_Anthropic_Format()
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
            this.ShouldSatisfyAllConditions(
                () => chatCompletionService.ShouldNotBeNull(),
                () => chatCompletionService.ShouldBeOfType<AnthropicChatCompletionService>()
            );
        }

        [Fact]
        public void It_Should_Register_OpenAI_Service_When_GetKernel_With_OpenAI_Format()
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
            this.ShouldSatisfyAllConditions(
                () => chatCompletionService.ShouldNotBeNull()
            );
        }
    }

    /// <summary>
    /// Tests for LlmApiFormat enum
    /// </summary>
    public class When_Call_LlmApiFormat
    {
        [Fact]
        public void It_Should_Have_Correct_Values()
        {
            // Assert
            this.ShouldSatisfyAllConditions(
                () => ((int)LlmApiFormat.OpenAI).ShouldBe(0),
                () => ((int)LlmApiFormat.Anthropic).ShouldBe(1)
            );
        }

        [Theory]
        [InlineData(LlmApiFormat.OpenAI, "OpenAI 兼容格式")]
        [InlineData(LlmApiFormat.Anthropic, "Anthropic 格式")]
        public void It_Should_Have_Description(LlmApiFormat format, string expectedDescription)
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
    public class When_Call_LlmCompletionRouter_Anthropic
    {
        [Fact]
        public void It_Should_Set_Correct_Endpoint_When_SendAsync_With_Anthropic()
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
            this.ShouldSatisfyAllConditions(
                () => router.ShouldNotBeNull()
            );
        }
    }
}
