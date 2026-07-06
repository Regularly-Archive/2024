using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// 工具执行器接口
/// </summary>
public interface IToolExecutor
{
    /// <summary>工具名称</summary>
    string Name { get; }

    /// <summary>工具定义</summary>
    ToolDefinition Definition { get; }

    /// <summary>执行工具</summary>
    Task<ToolResult> ExecuteAsync(
        IDictionary<string, object> args,
        ToolExecutionContext context);
    
    /// <summary>
    /// 拦截工具结果（截断/持久化）后再添加到上下文。
    /// 默认实现：不做任何处理，直接返回。
    /// 重写此方法以实现工具特定的拦截逻辑。
    /// </summary>
    InterceptionResult Intercept(ToolResult result, TruncationContext context)
    {
        return InterceptionResult.NotIntercepted(result);
    }
}
