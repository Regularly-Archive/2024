using System.Collections.Concurrent;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Internal;

namespace InsightaAI.LLM;

/// <summary>
/// LLM 客户端工厂
/// </summary>
public class LlmClientFactory
{
    private readonly ConcurrentDictionary<string, IProviderAdapter> _adapters = new();
    private readonly HttpClient? _sharedHttpClient;

    /// <summary>
    /// 创建工厂实例
    /// </summary>
    /// <param name="httpClient">可选的共享 HttpClient</param>
    public LlmClientFactory(HttpClient? httpClient = null)
    {
        _sharedHttpClient = httpClient;
    }

    /// <summary>
    /// 注册 Provider 适配器
    /// </summary>
    public LlmClientFactory RegisterAdapter(IProviderAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapters[adapter.Name.ToLowerInvariant()] = adapter;
        return this;
    }

    /// <summary>
    /// 根据 Provider 名称创建客户端
    /// </summary>
    public ILlmClient Create(string provider, ProviderConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(config);

        var key = provider.ToLowerInvariant();
        if (!_adapters.TryGetValue(key, out var adapter))
        {
            throw new ArgumentException(
                $"Provider '{provider}' not registered. Available providers: {string.Join(", ", _adapters.Keys)}");
        }

        return new DefaultLlmClient(adapter, config, _sharedHttpClient);
    }

    /// <summary>
    /// 根据 "provider/model" 格式创建客户端
    /// </summary>
    public (ILlmClient Client, string Model) FromModel(string modelId, ProviderConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(config);

        var parts = modelId.Split('/', 2);
        if (parts.Length != 2)
        {
            throw new ArgumentException(
                $"Invalid model format '{modelId}'. Expected format: 'provider/model' (e.g., 'openai/gpt-4o')");
        }

        var client = Create(parts[0], config);
        return (client, parts[1]);
    }

    /// <summary>
    /// 获取已注册的 Adapter 名称
    /// </summary>
    public IEnumerable<string> GetRegisteredAdapters() => _adapters.Keys;

    /// <summary>
    /// 检查 Adapter 是否已注册
    /// </summary>
    public bool HasAdapter(string provider) => _adapters.ContainsKey(provider.ToLowerInvariant());
}
