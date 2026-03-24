using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.TextGeneration;
using SqlSugar;


namespace PostgreSQL.Embedding.Llm.Connectors.Anthropic
{
    /// <summary>
    /// Anthropic chat completion service for Semantic Kernel.
    /// Implements IChatCompletionService and ITextGenerationService.
    /// </summary>
    public class AnthropicChatCompletionService : IChatCompletionService, ITextGenerationService
    {
        private readonly string _modelId;
        private readonly string _apiKey;
        private readonly Uri _endpoint;
        private readonly HttpClient _httpClient;
        private readonly ILogger? _logger;

        private const string AnthropicApiVersion = "2023-06-01";
        private const string DefaultApiUrl = "https://api.anthropic.com/v1/messages";

        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();
        private readonly AnthropicClient _anthropicClient;
        public AnthropicChatCompletionService(
            string modelId,
            string apiKey,
            Uri? endpoint = null,
            HttpClient? httpClient = null,
            ILoggerFactory? loggerFactory = null)
        {
            _modelId = modelId;
            _apiKey = apiKey;
            _endpoint = endpoint ?? new Uri(DefaultApiUrl);
            _httpClient = httpClient ?? new HttpClient();
            _anthropicClient = new AnthropicClient(client: _httpClient);
            _anthropicClient.Auth = new APIAuthentication(apiKey: _apiKey);
            _anthropicClient.AnthropicVersion = AnthropicApiVersion;
            _logger = loggerFactory?.CreateLogger<AnthropicChatCompletionService>();
        }

        /// <summary>
        /// Creates a new instance from LlmModel entity.
        /// </summary>
        public static AnthropicChatCompletionService FromLlmModel(
            Domain.Entities.LlmModel llmModel,
            HttpClient? httpClient = null,
            ILoggerFactory? loggerFactory = null)
        {
            var endpoint = string.IsNullOrEmpty(llmModel.BaseUrl)
                ? new Uri(DefaultApiUrl)
                : new Uri(llmModel.BaseUrl);

            return new AnthropicChatCompletionService(
                modelId: llmModel.ModelName,
                apiKey: llmModel.ApiKey ?? string.Empty,
                endpoint: endpoint,
                loggerFactory: loggerFactory);
        }

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            var messages = chatHistory.Select(m => new Message(m.Role == AuthorRole.User ? RoleType.User : RoleType.Assistant, m.Content)).ToList();

            var parameters = new MessageParameters()
            {
                Messages = messages,
                MaxTokens = 4096,
                Model = _modelId,
                Stream = false,
                Temperature = 1.0m,
            };

            var response = await _anthropicClient.Messages.GetClaudeMessageAsync(parameters);
            var metadata = new Dictionary<string, object?>()
            {
                { "Usage",  new { InputTokenCount = response.Usage.InputTokens, OutputTokenCount = response.Usage.OutputTokens } }
            };

            return new List<ChatMessageContent> { new ChatMessageContent() { Content = response.Message.ToString(), Metadata =  metadata } };
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var messages = chatHistory.Select(m => new Message(m.Role == AuthorRole.User ? RoleType.User : RoleType.Assistant, m.Content)).ToList();

            var parameters = new MessageParameters()
            {
                Messages = messages,
                MaxTokens = 4096 * 4,
                Model = _modelId,
                Stream = true,
                Temperature = 1.0m,
            };

            await foreach (var res in _anthropicClient.Messages.StreamClaudeMessageAsync(parameters))
            {
                if (res.Delta != null && !string.IsNullOrEmpty(res.Delta.Text))
                {
                    yield return new StreamingChatMessageContent(AuthorRole.Assistant, res.Delta.Text);
                }

                if (res.Usage != null)
                {
                    var metadata = new Dictionary<string, object?>()
                    {
                        { "Usage",  new { InputTokenCount = res.Usage.InputTokens, OutputTokenCount = res.Usage.OutputTokens } }
                    };

                    yield return new StreamingChatMessageContent(AuthorRole.Assistant, string.Empty, metadata: metadata);

                }
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<Microsoft.SemanticKernel.TextContent>> GetTextContentsAsync(
            string prompt,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {

            var parameters = new MessageParameters()
            {
                Messages = new List<Message>() { new Message(RoleType.User, prompt) },
                MaxTokens = 4096,
                Model = _modelId,
                Stream = false,
                Temperature = 1.0m
            };

            var response = await _anthropicClient.Messages.GetClaudeMessageAsync(parameters, cancellationToken);

            return response.Content.Where(x => x.Type == ContentType.text).Select(x => new Microsoft.SemanticKernel.TextContent(x.ToString())).ToList();
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<StreamingTextContent> GetStreamingTextContentsAsync(
            string prompt,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var parameters = new MessageParameters()
            {
                Messages = new List<Message>() { new Message(RoleType.User, prompt) },
                MaxTokens = 4096,
                Model = _modelId,
                Stream = true,
                Temperature = 1.0m,
            };

            await foreach (var res in _anthropicClient.Messages.StreamClaudeMessageAsync(parameters))
            {
                if (res.Delta != null && !string.IsNullOrEmpty(res.Delta.Text))
                {
                    yield return new StreamingTextContent(res.Delta.Text);
                }

                if (res.Usage != null)
                {
                    var metadata = new Dictionary<string, object?>()
                    {
                        { "Usage",  new { InputTokenCount = res.Usage.InputTokens, OutputTokenCount = res.Usage.OutputTokens } }
                    };

                    yield return new StreamingTextContent(string.Empty, metadata: metadata);

                }
            }
        }
    }

}
