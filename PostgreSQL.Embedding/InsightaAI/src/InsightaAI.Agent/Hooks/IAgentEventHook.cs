using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Hooks;

/// <summary>
/// Agent 级别钩子接口 - 在轮次/会话级别进行拦截
/// </summary>
public interface IAgentEventHook
{
    /// <summary>Hook 唯一标识</summary>
    string Id { get; }

    /// <summary>
    /// Agent 启动时的钩子（在任何轮次开始前触发，用于初始化会话级资源）
    /// </summary>
    /// <param name="context">Hook 上下文</param>
    /// <param name="message">用户输入消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task OnAgentSessionStartedAsync(
        HookContext context,
        string message,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// 每轮开始前的钩子
    /// </summary>
    /// <param name="message">用户输入消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task OnAgentRoundStartedAsync(
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
    Task OnAgentRoundEndedAsync(
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
    Task OnAgentSessionEndedAsync(
        HookContext context,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
