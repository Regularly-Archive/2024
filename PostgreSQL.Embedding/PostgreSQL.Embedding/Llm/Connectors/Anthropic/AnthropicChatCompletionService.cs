using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.TextGeneration;
using System.Text.Json;
using System.Text.Json.Serialization;

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
            var request = BuildRequest(chatHistory, executionSettings);
            var response = await SendRequestAsync(request, cancellationToken);

            var reasoningContent = response.Content.FirstOrDefault(x => x.Type == "thinking");
            var responseContent = response.Content.FirstOrDefault(x => x.Type == "text");
#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var content = new ChatMessageContent(AuthorRole.Assistant, responseContent.Text, _modelId, innerContent: new ReasoningContent(responseContent.Text));
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            return new List<ChatMessageContent> { content };
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = BuildRequest(chatHistory, executionSettings);
            request.Stream = true;

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(request)
            };

            httpRequest.Headers.Add("x-api-key", _apiKey);
            httpRequest.Headers.Add("anthropic-version", AnthropicApiVersion);
            httpRequest.Headers.Add("anthropic-dangerous-direct-browser-access", "true");

            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Anthropic uses SSE format: "data: {...}"
                if (line.StartsWith("data: "))
                {
                    var jsonData = line["data: ".Length..];
                    if (jsonData == "[DONE]") yield break;

                    StreamingChatMessageContent? content = null;

                    try
                    {
                        var chunk = JsonSerializer.Deserialize<AnthropicStreamingResponse>(jsonData, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (chunk.Type != "content_block_delta") continue;
                        var metadata = new Dictionary<string, object> { { "type", chunk.Type }, { "index", chunk.Index } };

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                        content = chunk?.Delta?.Text == null
                            ? new StreamingChatMessageContent(role: AuthorRole.Assistant, content: string.Empty, choiceIndex: chunk.Index, modelId: _modelId, innerContent: new StreamingReasoningContent(chunk.Delta.Thinking), metadata: metadata)
                            : new StreamingChatMessageContent(role: AuthorRole.Assistant, content: chunk?.Delta.Text, choiceIndex: chunk.Index, modelId: _modelId, metadata: metadata);
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    }
                    catch (JsonException ex)
                    {
                        _logger?.LogWarning(ex, "Failed to parse streaming response chunk");
                    }

                    if (content != null)
                    {
                        yield return content;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<TextContent>> GetTextContentsAsync(
            string prompt,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(prompt);

            var request = BuildRequest(chatHistory, executionSettings);
            var response = await SendRequestAsync(request, cancellationToken);

            var reasoningContent = response.Content.FirstOrDefault(x => x.Type == "thinking");
            var responseContent = response.Content.FirstOrDefault(x => x.Type == "text");

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            var content = new TextContent(responseContent.Text, _modelId, innerContent:new ReasoningContent(responseContent.Text));
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            return new List<TextContent> { content };
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<StreamingTextContent> GetStreamingTextContentsAsync(
            string prompt,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(prompt);

            var request = BuildRequest(chatHistory, executionSettings);
            request.Stream = true;

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(request)
            };

            httpRequest.Headers.Add("x-api-key", _apiKey);
            httpRequest.Headers.Add("anthropic-version", AnthropicApiVersion);
            httpRequest.Headers.Add("anthropic-dangerous-direct-browser-access", "true");

            using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("data: "))
                {
                    var jsonData = line["data: ".Length..];
                    if (jsonData == "[DONE]") yield break;

                    StreamingTextContent? content = null;

                    try
                    {
                        var chunk = JsonSerializer.Deserialize<AnthropicStreamingResponse>(jsonData, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (chunk.Type != "content_block_delta") continue;
                        var metadata = new Dictionary<string, object> { { "type", chunk.Type }, { "index", chunk.Index } };

#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                        content = chunk?.Delta?.Text == null
                            ? new StreamingTextContent(text: string.Empty, choiceIndex: chunk.Index, modelId: _modelId, innerContent: new StreamingReasoningContent(chunk.Delta.Thinking), metadata: metadata)
                            : new StreamingTextContent(text: chunk?.Delta.Text, choiceIndex: chunk.Index, modelId: _modelId, metadata: metadata);
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
                    }
                    catch (JsonException ex)
                    {
                        _logger?.LogWarning(ex, "Failed to parse streaming response chunk");
                    }

                    if (content != null)
                    {
                        yield return content;
                    }
                }
            }
        }

        private AnthropicRequest BuildRequest(ChatHistory chatHistory, PromptExecutionSettings? executionSettings)
        {
            var messages = chatHistory.Select(m => new AnthropicMessage
            {
                Role = m.Role == AuthorRole.User ? "user" : "assistant",
                Content = m.Content
            }).ToList();

            // Extract settings from extension data
            int maxTokens = 4096;
            double temperature = 1.0;
            double topP = 0.999;

            if (executionSettings?.ExtensionData != null)
            {
                if (executionSettings.ExtensionData.TryGetValue("max_tokens", out var mt) && mt != null)
                    maxTokens = Convert.ToInt32(mt);
                if (executionSettings.ExtensionData.TryGetValue("temperature", out var tp) && tp != null)
                    temperature = Convert.ToDouble(tp);
                if (executionSettings.ExtensionData.TryGetValue("top_p", out var topPVal) && topPVal != null)
                    topP = Convert.ToDouble(topPVal);
            }

            return new AnthropicRequest
            {
                Model = _modelId,
                Messages = messages,
                MaxTokens = maxTokens,
                Temperature = temperature,
                TopP = topP,
                Stream = false
            };
        }

        private async Task<AnthropicResponse> SendRequestAsync(AnthropicRequest request, CancellationToken cancellationToken)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(request)
            };

            httpRequest.Headers.Add("x-api-key", _apiKey);
            httpRequest.Headers.Add("anthropic-version", AnthropicApiVersion);
            httpRequest.Headers.Add("anthropic-dangerous-direct-browser-access", "true");

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<AnthropicResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Failed to deserialize Anthropic response");
        }
    }

    #region Request/Response Models

    internal class AnthropicRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<AnthropicMessage> Messages { get; set; } = new();

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 4096;

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 1.0;

        [JsonPropertyName("top_p")]
        public double TopP { get; set; } = 0.999;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;
    }

    internal class AnthropicMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    internal class AnthropicResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public List<AnthropicContent> Content { get; set; } = new();

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; set; }

        [JsonPropertyName("stop_sequence")]
        public string? StopSequence { get; set; }

        [JsonPropertyName("usage")]
        public AnthropicUsage? Usage { get; set; }
    }

    internal class AnthropicContent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    internal class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }

    internal class AnthropicStreamingResponse
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("content")]
        public List<AnthropicContent> Content { get; set; } = new();

        [JsonPropertyName("delta")]
        public AnthropicDelta? Delta { get; set; }

        [JsonPropertyName("content_block")]
        public AnthropicContentBlock? ContentBlock { get; set; }
    }

    internal class AnthropicDelta
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("thinking")]
        public string Thinking { get; set; } = string.Empty;
    }

    internal class AnthropicContentBlock
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("thinking")]
        public string Thinking { get; set; } = string.Empty;
    }

    #endregion
}
