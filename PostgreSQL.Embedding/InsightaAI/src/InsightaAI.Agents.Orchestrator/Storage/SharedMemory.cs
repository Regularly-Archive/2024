using System.Collections.Concurrent;
using System.Text.Json;

namespace InsightaAI.Agents.Orchestrator.Storage;

/// <summary>
/// 共享内存 - 全局 KV 存储，节点间共享状态
/// 线程安全，基于 ConcurrentDictionary
/// </summary>
public sealed class SharedMemory
{
    private readonly ConcurrentDictionary<string, object?> _store = new();

    private static readonly JsonSerializerOptions s_defaultOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 获取值（自动处理 JsonElement → T 的反序列化）
    /// </summary>
    public T? Get<T>(string key)
    {
        if (!_store.TryGetValue(key, out var value))
            return default;

        if (value is T t)
            return t;

        // 从持久化恢复后，值可能是 JsonElement，按需反序列化
        if (value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                return default;

            try
            {
                return element.Deserialize<T>(s_defaultOptions);
            }
            catch (JsonException)
            {
                // 类型不匹配（如 string → int），返回 default
                return default;
            }
        }

        return default;
    }

    /// <summary>
    /// 设置值
    /// </summary>
    public void Set<T>(string key, T value)
    {
        _store[key] = value;
    }

    /// <summary>
    /// 检查是否存在
    /// </summary>
    public bool Has(string key)
    {
        return _store.ContainsKey(key);
    }

    /// <summary>
    /// 删除值
    /// </summary>
    public bool Delete(string key)
    {
        return _store.TryRemove(key, out _);
    }

    /// <summary>
    /// 获取所有数据快照
    /// </summary>
    public IReadOnlyDictionary<string, object?> Snapshot()
    {
        return new Dictionary<string, object?>(_store);
    }

    /// <summary>
    /// 清空所有数据
    /// </summary>
    public void Clear()
    {
        _store.Clear();
    }

    /// <summary>
    /// 序列化为 JSON（用于持久化保存/恢复）
    /// </summary>
    public string SerializeSnapshot(JsonSerializerOptions? options = null)
    {
        var snapshot = Snapshot();
        var serializable = new Dictionary<string, JsonElement?>();

        foreach (var (key, value) in snapshot)
        {
            if (value is JsonElement je)
            {
                serializable[key] = je;
            }
            else if (value is null)
            {
                serializable[key] = null;
            }
            else
            {
                serializable[key] = JsonSerializer.SerializeToElement(value, value.GetType(), options ?? s_defaultOptions);
            }
        }

        return JsonSerializer.Serialize(serializable, options ?? s_defaultOptions);
    }

    /// <summary>
    /// 从 JSON 反序列化恢复状态
    /// </summary>
    public void DeserializeSnapshot(string json, JsonSerializerOptions? options = null)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement?>>(json, options ?? s_defaultOptions);
        if (data == null) return;

        foreach (var (key, element) in data)
        {
            if (element is null || element.Value.ValueKind == JsonValueKind.Null || element.Value.ValueKind == JsonValueKind.Undefined)
            {
                _store[key] = null;
            }
            else
            {
                _store[key] = element.Value;
            }
        }
    }

    /// <summary>
    /// 从 JSON 反序列化创建新实例
    /// </summary>
    public static SharedMemory FromSnapshot(string json, JsonSerializerOptions? options = null)
    {
        var memory = new SharedMemory();
        memory.DeserializeSnapshot(json, options);
        return memory;
    }
}
