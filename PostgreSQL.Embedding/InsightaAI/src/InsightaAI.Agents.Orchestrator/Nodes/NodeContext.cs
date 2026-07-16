using InsightaAI.Agents.Orchestrator.Storage;

namespace InsightaAI.Agents.Orchestrator.Nodes;

/// <summary>
/// 节点执行上下文 - 传递给 FunctionNode 的 Execute 委托
/// </summary>
public sealed record NodeContext
{
    /// <summary>节点输入（来自依赖节点输出的聚合文本）</summary>
    public required string Input { get; init; }

    /// <summary>依赖节点的输出字典 { nodeId -> output }</summary>
    public IReadOnlyDictionary<string, object?> Dependencies { get; init; } =
        new Dictionary<string, object?>();

    /// <summary>共享内存（全局读写）</summary>
    public required SharedMemory Memory { get; init; }

    /// <summary>Artifact 存储（数据契约）</summary>
    public required ArtifactStore Artifacts { get; init; }

    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }
}
