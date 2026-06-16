using InsightaAI.Orchestrator.Nodes;

namespace InsightaAI.Orchestrator.Planning;

/// <summary>
/// 可序列化的 DAG 计划（用于持久化保存/恢复）
/// </summary>
public sealed record DAGPlan
{
    /// <summary>原始目标</summary>
    public string? Goal { get; init; }

    /// <summary>节点 DTO 列表</summary>
    public required DAGNodeDto[] Nodes { get; init; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 可序列化的节点 DTO（不包含不可序列化的委托）
/// 注意：FunctionNode 的 Func 委托无法序列化，DAGPlan 仅支持 AgentNode 数据
/// </summary>
public sealed record DAGNodeDto
{
    /// <summary>节点 ID</summary>
    public required string Id { get; init; }

    /// <summary>节点名称</summary>
    public required string Name { get; init; }

    /// <summary>节点类型</summary>
    public required NodeKind Kind { get; init; }

    /// <summary>依赖的节点 ID 列表</summary>
    public string[] DependsOn { get; init; } = [];

    /// <summary>输入 Artifacts</summary>
    public string[] InputArtifacts { get; init; } = [];

    /// <summary>输出 Artifacts</summary>
    public string[] OutputArtifacts { get; init; } = [];

    /// <summary>节点描述</summary>
    public string? Description { get; init; }

    // AgentNode 特有字段
    /// <summary>Agent ID</summary>
    public string? AgentId { get; init; }

    /// <summary>工具名称列表</summary>
    public string[]? ToolNames { get; init; }

    /// <summary>System Prompt</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>任务描述</summary>
    public string? TaskDescription { get; init; }
}
