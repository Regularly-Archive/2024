namespace InsightaAI.Agents.Orchestrator.Nodes;

/// <summary>
/// 节点类型枚举
/// </summary>
public enum NodeKind
{
    /// <summary>纯函数/委托，无 LLM 调用</summary>
    Function,

    /// <summary>预配置工具的 Agent（构建时静态绑定工具）</summary>
    PresetAgent,

    /// <summary>LLM 动态分配工具的 Agent，不支持嵌套</summary>
    SubAgent
}
