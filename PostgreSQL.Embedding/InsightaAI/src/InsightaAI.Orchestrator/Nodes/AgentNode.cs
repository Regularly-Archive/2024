namespace InsightaAI.Orchestrator.Nodes;

/// <summary>
/// Agent 节点 - 通过 L2 Agent 运行时执行
/// ToolNames 为 null 时 => SubAgent（LLM 动态分配工具）
/// ToolNames 不为 null 时 => PresetAgent（静态绑定工具）
/// </summary>
public sealed class AgentNode : DAGNode
{
    /// <summary>引用 Team 中的 AgentConfig Id</summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// 要绑定的工具名称列表。null = SubAgent（LLM 动态分配）
    /// 非 null = PresetAgent（仅这些工具可用）
    /// </summary>
    public string[]? ToolNames { get; init; }

    /// <summary>覆盖 Agent 的默认 SystemPrompt</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// 此 Agent 节点的任务描述
    /// 作为用户输入传递给 Agent
    /// 可以包含模板变量如 {fetch_output} 引用依赖输出
    /// </summary>
    public string? TaskDescription { get; init; }

    /// <summary>节点类型（根据 ToolNames 自动判断）</summary>
    public override NodeKind Kind => ToolNames == null ? NodeKind.SubAgent : NodeKind.PresetAgent;
}
