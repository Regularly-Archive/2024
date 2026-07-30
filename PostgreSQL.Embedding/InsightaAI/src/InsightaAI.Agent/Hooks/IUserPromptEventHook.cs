using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Hooks;

/// <summary>
/// 用户输入已被 Agent 接收后的后置 Hook。
/// 实现应仅执行异步观察或副作用；它不能修改、拒绝或阻塞用户输入。
/// </summary>
public interface IUserPromptEventHook
{
    /// <summary>Hook 唯一标识。</summary>
    string Id { get; }

    /// <summary>
    /// 在用户消息写入当前 Turn 后触发。
    /// </summary>
    /// <param name="context">不可变的用户输入事件上下文。</param>
    /// <param name="userMessage">刚被接收的用户消息。</param>
    /// <param name="cancellationToken">当前 Turn 的取消令牌。</param>
    Task OnUserPromptReceivedAsync(
        AgentEventHookContext context,
        Message userMessage,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
