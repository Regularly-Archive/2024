using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InsightaAI.LLM.Extensions;

/// <summary>
/// IServiceCollection extension methods for LlmClientFactory registration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="LlmClientFactory"/> as a singleton with <see cref="IHttpClientFactory"/> support.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback to register <see cref="IProviderAdapter"/> instances on the factory.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLlmClientFactory(
        this IServiceCollection services,
        Action<LlmClientFactory>? configure = null)
    {
        services.AddHttpClient("LlmClient");

        services.TryAddSingleton<LlmClientFactory>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("LlmClient");

            var factory = new LlmClientFactory(httpClient);
            configure?.Invoke(factory);
            return factory;
        });

        return services;
    }
}
