using Microsoft.KernelMemory.AI.OpenAI;
using Microsoft.KernelMemory.AI;
using Microsoft.KernelMemory;
using Microsoft.Extensions.Logging;
using Microsoft.KernelMemory.Diagnostics;
using Microsoft.ML.Tokenizers;
using static Org.BouncyCastle.Math.EC.ECCurve;
using Microsoft.SemanticKernel.Embeddings;
using ZstdSharp.Unsafe;
using PostgreSQL.Embedding.Common.Models;
using Newtonsoft.Json;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using System.Net.Http;
using System.Diagnostics.CodeAnalysis;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class KernelMemoryExtensions
    {
        [Experimental("KMEXP01")]
        public static IKernelMemoryBuilder WithOpenAICompatibleTextEmbeddingGeneration(
            this IKernelMemoryBuilder builder,
            OpenAIConfig config,
            HttpClient? httpClient = null)
        {
            config.Validate();
            builder.Services.AddOpenAICompatibleTextEmbeddingGenerator(config, httpClient);
            return builder;
        }

        [Experimental("KMEXP01")]
        public static IServiceCollection AddOpenAICompatibleTextEmbeddingGenerator(this IServiceCollection services, OpenAIConfig config, HttpClient httpClient)
        {
            return services
            .AddSingleton<ITextEmbeddingGenerator>(
                serviceProvider => new OpenAICompatibleTextEmbeddingGenerator(
                    config: config,
                    httpClient,
                    loggerFactory: serviceProvider.GetService<ILoggerFactory>()
                )
            );
        }
    }

    [Experimental("KMEXP01")]
    public class OpenAICompatibleTextEmbeddingGenerator : ITextEmbeddingGenerator, ITextEmbeddingBatchGenerator
    {
        private ITextTokenizer _textTokenizer;

        private readonly HttpClient _httpClient;
        private readonly OpenAIConfig _openAIConfig;
        private readonly ILogger<OpenAICompatibleTextEmbeddingGenerator> _logger;

        public int MaxTokens { get; }

        public int MaxBatchSize { get; }

        public OpenAICompatibleTextEmbeddingGenerator(
            OpenAIConfig config,
            HttpClient httpClient,
            ILoggerFactory? loggerFactory = null
        )
        {
            _logger = (loggerFactory ?? DefaultLogger.Factory).CreateLogger<OpenAICompatibleTextEmbeddingGenerator>();
            MaxTokens = config.EmbeddingModelMaxTokenTotal;
            MaxBatchSize = config.MaxEmbeddingBatchSize;
            _openAIConfig = config;
            _httpClient = httpClient;

            GetTextTokenizer();
        }

        public async Task<Microsoft.KernelMemory.Embedding> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            var content = JsonContent.Create(new
            {
                model = _openAIConfig.EmbeddingModel,
                input = new List<string> { text },
                encoding_format = "float"
            });

            var request = new HttpRequestMessage() { Method = HttpMethod.Post, Content = content, RequestUri = new Uri("https://api.openai.com/v1/embeddings") };
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var embeddingResult = JsonConvert.DeserializeObject<OpenAICompatibleEmbeddingResult>(responseBody);
            return new Microsoft.KernelMemory.Embedding(embeddingResult.Data[0].Embedding.ToArray());
        }

        public async Task<Microsoft.KernelMemory.Embedding[]> GenerateEmbeddingBatchAsync(IEnumerable<string> textList, CancellationToken cancellationToken = default)
        {
            var content = JsonContent.Create(new
            {
                model = _openAIConfig.EmbeddingModel,
                input = textList,
                encoding_format = "float"
            });

            var request = new HttpRequestMessage() { Method = HttpMethod.Post, Content = content, RequestUri = new Uri("https://api.openai.com/v1/embeddings") };
            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var embeddingResult = JsonConvert.DeserializeObject<OpenAICompatibleEmbeddingResult>(responseBody);
            return embeddingResult.Data.Select(x => new Microsoft.KernelMemory.Embedding(x.Embedding.ToArray())).ToArray();
        }

        public int CountTokens(string text)
        {
            return _textTokenizer.CountTokens(text);
        }

        public IReadOnlyList<string> GetTokens(string text)
        {
            return _textTokenizer.GetTokens(text);
        }

        private void GetTextTokenizer()
        {
            if (_textTokenizer == null && !string.IsNullOrEmpty(_openAIConfig.EmbeddingModelTokenizer))
            {
                _textTokenizer = TokenizerFactory.GetTokenizerForEncoding(_openAIConfig.EmbeddingModelTokenizer);
            }

            _textTokenizer ??= TokenizerFactory.GetTokenizerForModel(_openAIConfig.EmbeddingModel);

            if (_textTokenizer == null)
            {
                _textTokenizer = new CL100KTokenizer();
                _logger.LogWarning(
                    "Tokenizer not specified, will use {0}. The token count might be incorrect, causing unexpected errors",
                    _textTokenizer.GetType().FullName);
            }
        }
    }
}
