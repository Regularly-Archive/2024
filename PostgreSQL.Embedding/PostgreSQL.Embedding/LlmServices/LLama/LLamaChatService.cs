using LLama.Common;
using Microsoft.SemanticKernel;
using LLamaSharp.SemanticKernel.TextEmbedding;
using LLamaSharp.KernelMemory;
using LLama;
using System;
using PostgreSQL.Embedding.LlmServices.Abstration;
using DocumentFormat.OpenXml.InkML;

namespace PostgreSQL.Embedding.LlmServices.LLama
{
    public class LLamaChatService : ILlmChatService
    {
        private readonly ChatSession _session;
        private readonly LLamaContext _context;
        private readonly ILogger<LLamaChatService> _logger;
        private bool _continue = false;
        private readonly InferenceParams _inferenceParams;

        public LLamaChatService(IWebHostEnvironment environment, IConfiguration configuration, ILogger<LLamaChatService> logger)
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
        }

        public void Dispose()
        {
            _context?.Dispose();
        }

        public async Task<string> ChatAsync(string input)
        {
            if (!_continue) _continue = true;

            _logger.LogInformation("Input: {text}", input);

            HandleChatHistories();
            var outputs = _session.ChatAsync(new ChatHistory.Message(AuthorRole.User, input), _inferenceParams);

            var result = "";
            await foreach (var output in outputs)
            {
                _logger.LogInformation("Message: {output}", output);
                result += output;
            }

            return result;
        }

        public async IAsyncEnumerable<string> ChatStreamAsync(string input)
        {
            _logger.LogInformation(input);
            if (!_continue) _continue = true;

            HandleChatHistories();
            var outputs = _session.ChatAsync(new ChatHistory.Message(AuthorRole.User, input!), _inferenceParams);

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
    }
}
