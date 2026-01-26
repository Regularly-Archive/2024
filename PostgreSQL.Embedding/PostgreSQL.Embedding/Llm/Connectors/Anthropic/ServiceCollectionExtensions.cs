using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.TextGeneration;
using System.Net.Http;

namespace PostgreSQL.Embedding.Llm.Connectors.Anthropic;

/// <summary>
/// Extension methods for adding Anthropic chat completion service to the kernel builder.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string DefaultEndpoint = "https://api.anthropic.com/v1/messages";

    /// <summary>
    /// Adds Anthropic chat completion service to the kernel builder.
    /// </summary>
    /// <param name="builder">The kernel builder.</param>
    /// <param name="modelId">The model identifier (e.g., "claude-sonnet-4-20250514").</param>
    /// <param name="apiKey">The Anthropic API key.</param>
    /// <param name="endpoint">The endpoint URL (optional, defaults to https://api.anthropic.com/v1/messages).</param>
    /// <param name="serviceId">The optional service ID.</param>
    /// <param name="httpClient">The optional HTTP client.</param>
    /// <returns>The kernel builder.</returns>
    public static IKernelBuilder AddAnthropicChatCompletion(
        this IKernelBuilder builder,
        string modelId,
        string apiKey,
        string? endpoint = null,
        string? serviceId = null,
        HttpClient? httpClient = null)
    {
        _ = builder ?? throw new ArgumentNullException(nameof(builder));
        _ = modelId ?? throw new ArgumentNullException(nameof(modelId));
        _ = apiKey ?? throw new ArgumentNullException(nameof(apiKey));

        var endpointUri = string.IsNullOrEmpty(endpoint) ? new Uri(DefaultEndpoint) : new Uri(endpoint);

        IChatCompletionService ChatCompletionFactory(IServiceProvider serviceProvider, object? _) =>
            new AnthropicChatCompletionService(
                modelId: modelId,
                apiKey: apiKey,
                endpoint: endpointUri,
                httpClient: httpClient ?? new HttpClient(),
                loggerFactory: serviceProvider.GetService<ILoggerFactory>());

        ITextGenerationService TextGenerationFactory(IServiceProvider serviceProvider, object? _) =>
            new AnthropicChatCompletionService(
                modelId: modelId,
                apiKey: apiKey,
                endpoint: endpointUri,
                httpClient: httpClient ?? new HttpClient(),
                loggerFactory: serviceProvider.GetService<ILoggerFactory>());

        builder.Services.AddKeyedSingleton<IChatCompletionService>(serviceId, ChatCompletionFactory);
        builder.Services.AddKeyedSingleton<ITextGenerationService>(serviceId, TextGenerationFactory);

        return builder;
    }
}
