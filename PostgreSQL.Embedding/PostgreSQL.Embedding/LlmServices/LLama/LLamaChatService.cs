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
        private const string SystemPrompt = "You are a helpful AI bot.";

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
            _session.History.AddMessage(AuthorRole.System, SystemPrompt);
        }

        public void Dispose()
        {
            _context?.Dispose();
        }

        public async Task<string> ChatAsync(string input)
        {

            if (!_continue)
            {
                _logger.LogInformation("Prompt: {text}", SystemPrompt);
                _continue = true;
            }
            _logger.LogInformation("Input: {text}", input);
            HandleChatHistories();
            var outputs = _session.ChatAsync(
                new ChatHistory.Message(AuthorRole.User, input),
                new InferenceParams()
                {
                    RepeatPenalty = 1.0f,
                    AntiPrompts = new string[] { "User:" },
                });

            var result = "";
            await foreach (var output in outputs)
            {
                _logger.LogInformation("Message: {output}", output);
                result += output;
            }

            return result.Trim();
        }

        public async IAsyncEnumerable<string> ChatStreamAsync(string input)
        {
            if (!_continue)
            {
                _logger.LogInformation(SystemPrompt);
                _continue = true;
            }

            _logger.LogInformation(input);
            HandleChatHistories();
            var outputs = _session.ChatAsync(
                new ChatHistory.Message(AuthorRole.User, input!)
                , new InferenceParams()
                {
                    RepeatPenalty = 1.0f,
                    AntiPrompts = new string[] { "User:" },
                });

            await foreach (var output in outputs)
            {
                _logger.LogInformation(output);
                yield return output;
            }
        }

        private void HandleChatHistories()
        {

            _session.History.Messages.Clear();
            _session.History.AddMessage(AuthorRole.System, SystemPrompt);
        }
    }
}
