namespace InsightaAI.Agents.Orchestrator.Nodes;

/// <summary>
/// DAG 节点基类 - 抽象，通过 NodeKind 实现多态
/// </summary>
public abstract class DAGNode
{
    /// <summary>唯一节点标识</summary>
    public required string Id { get; init; }

    /// <summary>人类可读名称</summary>
    public required string Name { get; init; }

    /// <summary>依赖的节点 ID 列表</summary>
    public string[] DependsOn { get; init; } = [];

    /// <summary>节点类型（由子类计算）</summary>
    public abstract NodeKind Kind { get; }

    /// <summary>此节点需要的输入 Artifacts</summary>
    public string[] InputArtifacts { get; init; } = [];

    /// <summary>此节点产出的 Artifacts</summary>
    public string[] OutputArtifacts { get; init; } = [];

    /// <summary>节点描述（可选）</summary>
    public string? Description { get; init; }
}
