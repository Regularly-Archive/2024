using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.RAG;
using PostgreSQL.Embedding.Llm.Connectors.Anthropic;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Llm.Routers;
using PostgreSQL.Embedding.Llm.Services;
using Shouldly;
using System;
using Wikit.Tests.Utils;
using Xunit;

namespace Wikit.Tests.LlmServices
{
    /// <summary>
    /// Integration tests for CitationService.ExtractCitations
    /// Requires real LLM calls for citation injection
    /// </summary>
    public class When_Call_CitationService_ExtractCitations
    {
        private CitationService _citationService;
        private Kernel _kernel;

        public When_Call_CitationService_ExtractCitations()
        {
            var promptTemplateService = new PromptTemplateService();
            _citationService = new CitationService(promptTemplateService);
            _kernel = CreateKernel();
        }

        private static Kernel CreateKernel()
        {
            // Load environment variables
            var (baseUrl, apiKey, modelName) = GetAnthropicConfig();

            var llmModel = new LlmModel
            {
                ModelName = modelName,
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                ServiceProvider = (int)LlmServiceProvider.Anthropic,
                ApiFormat = (int)LlmApiFormat.Anthropic
            };

            var httpClient = new HttpClient(new LlmCompletionRouter(llmModel, null))
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            var service = AnthropicChatCompletionService.FromLlmModel(llmModel, httpClient);

            var builder = Kernel.CreateBuilder();
            builder.Services.AddScoped<AgentExecutionContext>();
            builder.AddChatCompletionFromModel(llmModel, httpClient);

            return builder.Build();
        }

        private static (string BaseUrl, string ApiKey, string ModelName) GetAnthropicConfig()
        {
            var baseUrl = TestEnvHelper.GetEnvOrDefault("ANTHROPIC_BASE_URL", "https://api.anthropic.com");
            var apiKey = TestEnvHelper.GetEnv("ANTHROPIC_API_KEY");
            var modelName = TestEnvHelper.GetEnv("ANTHROPIC_MODEL_NAME");
            return (baseUrl, apiKey, modelName);
        }

        private List<LlmCitationModel> CreateCitations(params (int Index, string Text, string Url, string FileName, string Type)[] items)
        {
            return items.Select(x => new LlmCitationModel
            {
                Index = x.Index,
                Text = x.Text,
                Url = x.Url,
                FileName = x.FileName,
                Type = x.Type
            }).ToList();
        }

        [Fact]
        public async Task It_Should_Inject_Citations_Into_Answer()
        {
            // Arrange
            var citations = CreateCitations(
                (1, "Paris is the capital of France.", "http://example.com/france", "France Article", "document"),
                (2, "Berlin is the capital of Germany.", "http://example.com/germany", "Germany Article", "document")
            );
            var answer = "Paris is the capital of France. Berlin is the capital of Germany.";

            // Act
            var result = await _citationService.ExtractCitations(answer, citations, _kernel);

            // Assert
            result.ShouldNotBeEmpty();
            result.Count.ShouldBeGreaterThanOrEqualTo(1);
            foreach (var item in result)
            {
                item.Id.ShouldNotBeNullOrEmpty();
                item.Positions.ShouldNotBeEmpty();
            }
        }

        [Fact]
        public async Task It_Should_Map_Citations_To_Sources()
        {
            // Arrange
            var citations = CreateCitations(
                (1, "Claude is an AI assistant developed by Anthropic.", "http://example.com/claude", "Claude Article", "document"),
                (2, "GPT-4 is developed by OpenAI.", "http://example.com/gpt4", "GPT-4 Article", "document")
            );
            var answer = "Claude is an AI assistant. GPT-4 is also an AI model.";

            // Act
            var result = await _citationService.ExtractCitations(answer, citations, _kernel);

            // Assert
            result.ShouldNotBeEmpty();

            // Each citation should have source info
            foreach (var item in result)
            {
                item.Id.ShouldNotBeNullOrEmpty();
                item.Positions.ShouldNotBeEmpty();
            }
        }

        [Fact]
        public async Task It_Should_Handle_Multiple_Citations_Per_Source()
        {
            // Arrange
            var citations = CreateCitations(
                (1, "Water boils at 100 degrees Celsius.", "http://example.com/boiling", "Boiling Point", "document"),
                (2, "Ice melts at 0 degrees Celsius.", "http://example.com/melting", "Melting Point", "document")
            );
            var answer = "Water boils at 100C. Ice melts at 0C. Both are properties of water.";

            // Act
            var result = await _citationService.ExtractCitations(answer, citations, _kernel);

            // Assert
            result.ShouldNotBeEmpty();
        }

        [Fact]
        public async Task It_Should_Handle_Empty_Answer()
        {
            // Arrange
            var citations = CreateCitations(
                (1, "Some reference text.", "http://example.com/ref", "Reference", "document")
            );
            var answer = "";

            // Act
            var result = await _citationService.ExtractCitations(answer, citations, _kernel);

            // Assert
            result.ShouldBeEmpty();
        }

        [Fact]
        public async Task It_Should_Handle_Empty_Citations()
        {
            // Arrange
            var citations = new List<LlmCitationModel>();
            var answer = "Some answer without any references.";

            // Act
            var result = await _citationService.ExtractCitations(answer, citations, _kernel);

            // Assert
            result.ShouldBeEmpty();
        }
    }
}
