namespace InsightaAI.Orchestrator.Nodes;

/// <summary>
/// 函数节点 - 执行纯函数/委托，无 LLM 调用
/// 典型用途：数据转换、格式化、API 调用
/// </summary>
public sealed class FunctionNode : DAGNode
{
    /// <summary>节点类型</summary>
    public override NodeKind Kind => NodeKind.Function;

    /// <summary>
    /// 要执行的函数。接收 NodeContext，返回可选结果。
    /// </summary>
    public required Func<NodeContext, Task<object?>> Execute { get; init; }
}
