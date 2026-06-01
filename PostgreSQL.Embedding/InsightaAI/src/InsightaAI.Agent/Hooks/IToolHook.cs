using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Hooks;

/// <summary>
/// 工具调用钩子接口 - 在工具执行前后进行拦截
/// </summary>
public interface IToolHook
{
    /// <summary>
    /// 该钩子适用的工具名称列表。为空表示适用于所有工具。
    /// </summary>
    IReadOnlyList<string>? TargetTools => null;

    /// <summary>
    /// 工具执行前的钩子
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="arguments">工具参数</param>
    /// <param name="context">执行上下文</param>
    /// <returns>钩子结果，决定是否继续执行</returns>
    Task<ToolHookResult> OnBeforeExecutionAsync(
        string toolName,
        string arguments,
        ToolExecutionContext context);

    /// <summary>
    /// 工具执行后的钩子
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="result">工具执行结果</param>
    /// <param name="context">执行上下文</param>
    Task OnAfterExecutionAsync(
        string toolName,
        ToolResult result,
        ToolExecutionContext context) => Task.CompletedTask;
}

/// <summary>
/// 工具钩子结果
/// </summary>
public enum ToolHookResult
{
    /// <summary>允许本次执行</summary>
    Allow,

    /// <summary>本次会话内始终允许</summary>
    AllowAlways,

    /// <summary>拒绝执行</summary>
    Deny
}
