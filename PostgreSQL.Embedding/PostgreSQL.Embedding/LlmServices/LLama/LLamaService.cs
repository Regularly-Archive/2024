using LLama;
using LLama.Abstractions;
using LLama.Common;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;
using ChatHistory = LLama.Common.ChatHistory;

namespace PostgreSQL.Embedding.LlmServices.LLama
{
    public class LLamaService : ILlmService
    {
        private readonly ChatSession _session;
        private readonly LLamaContext _context;
        private readonly ILogger<LLamaService> _logger;
        private bool _continue = false;
        private readonly InferenceParams _inferenceParams;
        private LLamaEmbedder _embedder;

        public LLamaService(IWebHostEnvironment environment, IConfiguration configuration, ILogger<LLamaService> logger)
        {
            var modelPath = Path.Combine(
                environment.ContentRootPath,
                configuration["LlamaConfig:ModelPath"]!
            );

            var contextSize = configuration.GetValue<uint?>("LLamaConfig:ContextSize") ?? 2048;

            var @params = new ModelParams(modelPath) { ContextSize = contextSize };
            using var weights = LLamaWeights.LoadFromFile(@params);

            _logger = logger;
            _context = new LLamaContext(weights, @params);

            _session = new ChatSession(new InteractiveExecutor(_context));
            _session.WithOutputTransform(
                new LLamaTransforms.KeywordTextOutputStreamTransform(
                    new string[] { "User:", "System:" },
                    redundancyLength: 9
                )
            );
            _session.History.AddMessage(AuthorRole.System, "Assistant is a large language model.");

            _inferenceParams = new InferenceParams()
            {
                RepeatPenalty = 1.0f,
                AntiPrompts = new string[] { "User:" },
            };

            _embedder = new LLamaEmbedder(weights, @params);
        }

        public void Dispose()
        {
            _context?.Dispose();
        }

        public async Task<string> ChatAsync(OpenAIModel request)
        {
            if (!_continue) _continue = true;

            _logger.LogInformation("Input: {text}", request.messages.LastOrDefault().content);

            HandleChatHistories();
            var outputs = _session.ChatAsync(new ChatHistory.Message(AuthorRole.User, request.messages.LastOrDefault().content), _inferenceParams);

            var result = "";
            await foreach (var output in outputs)
            {
                _logger.LogInformation("Message: {output}", output);
                result += output;
            }

            return result;
        }

        public async IAsyncEnumerable<string> ChatStreamAsync(OpenAIModel request)
        {
            _logger.LogInformation(request.messages.LastOrDefault().content);
            if (!_continue) _continue = true;

            HandleChatHistories();
            var outputs = _session.ChatAsync(new ChatHistory.Message(AuthorRole.User, request.messages.LastOrDefault().content!), _inferenceParams);

            await foreach (var output in outputs)
            {
                _logger.LogInformation(output);
                yield return output;
            }
        }

        private void HandleChatHistories()
        {
            _session.History.Messages.RemoveAll(x => x.AuthorRole != AuthorRole.System);
        }

        public Task<string> CompletionAsync(OpenAICompletionModel request)
        {
            throw new NotImplementedException();
        }

        public async Task<List<float>> Embedding(OpenAIEmbeddingModel embeddingModel)
        {
            float[] embeddings = await _embedder.GetEmbeddings(embeddingModel.input[0]);
            return embeddings.ToList();
        }
    }
}
