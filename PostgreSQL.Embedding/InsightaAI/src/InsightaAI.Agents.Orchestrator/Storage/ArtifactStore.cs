using System.Collections.Concurrent;

namespace InsightaAI.Agents.Orchestrator.Storage;

/// <summary>
/// Artifact 定义 - 声明数据契约
/// </summary>
public sealed record ArtifactDefinition
{
    /// <summary>Artifact 名称</summary>
    public required string Name { get; init; }

    /// <summary>描述</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Artifact 存储 - 节点间声明式数据流
/// 与 SharedMemory（全局任意 KV）不同，Artifact 是结构化的数据流声明
/// </summary>
public sealed class ArtifactStore
{
    private readonly ConcurrentDictionary<string, object?> _artifacts = new();

    /// <summary>
    /// 读取 artifact
    /// </summary>
    public T? Get<T>(string name)
    {
        return _artifacts.TryGetValue(name, out var value) && value is T t ? t : default;
    }

    /// <summary>
    /// 写入 artifact（节点完成后自动存储）
    /// </summary>
    public void Set<T>(string name, T value)
    {
        _artifacts[name] = value;
    }

    /// <summary>
    /// 检查 artifact 是否存在
    /// </summary>
    public bool Has(string name)
    {
        return _artifacts.ContainsKey(name);
    }

    /// <summary>
    /// 检查所有依赖是否满足
    /// </summary>
    public bool AreDependenciesMet(string[] required)
    {
        return required.All(name => _artifacts.ContainsKey(name));
    }

    /// <summary>
    /// 获取所有 artifact 快照
    /// </summary>
    public IReadOnlyDictionary<string, object?> Snapshot()
    {
        return new Dictionary<string, object?>(_artifacts);
    }

    /// <summary>
    /// 清空所有 artifacts
    /// </summary>
    public void Clear()
    {
        _artifacts.Clear();
    }
}
