using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Hooks;

/// <summary>
/// Agent 级别钩子接口 - 在轮次/会话级别进行拦截
/// </summary>
public interface IAgentHook
{
    /// <summary>Hook 唯一标识</summary>
    string Id { get; }

    /// <summary>
    /// 每轮开始前的钩子
    /// </summary>
    /// <param name="message">用户输入消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task OnRoundStartAsync(
        string message,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// 每轮结束后的钩子（在工具执行完成、LLM 回复后触发）
    /// </summary>
    /// <param name="context">Hook 上下文（含 LLM 客户端和会话 ID）</param>
    /// <param name="round">当前轮次</param>
    /// <param name="messages">当前对话历史</param>
    /// <param name="assistantMessage">本轮助手回复</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task OnRoundEndAsync(
        HookContext context,
        int round,
        IReadOnlyList<Message> messages,
        Message? assistantMessage,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// 会话结束时的钩子
    /// </summary>
    /// <param name="messages">完整对话历史</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task OnSessionEndAsync(
        HookContext context,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
