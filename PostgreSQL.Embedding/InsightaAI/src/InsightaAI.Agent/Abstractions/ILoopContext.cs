using InsightaAI.Agent.Context.Compaction;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// Agent Loop 运行时上下文，负责消息管理和上下文压缩
/// </summary>
public interface ILoopContext
{
    /// <summary>会话 ID</summary>
    string SessionId { get; }

    /// <summary>Agent ID</summary>
    string AgentId { get; }

    /// <summary>当前消息列表（只读）</summary>
    IReadOnlyList<Message> Messages { get; }

    /// <summary>追加一条消息</summary>
    void AddMessage(Message message);

    /// <summary>追加多条消息</summary>
    void AddMessages(IEnumerable<Message> messages);

    /// <summary>替换指定位置的消息，不触发消息持久化回调。</summary>
    void ReplaceMessage(int index, Message message);

    /// <summary>检查并执行上下文压缩（如需要）</summary>
    Task<CompactionResult?> CompactIfNeededAsync(CancellationToken ct);

    /// <summary>估算当前消息的 token 数</summary>
    int EstimateTokens();

    /// <summary>最大上下文窗口大小</summary>
    int MaxContextTokens { get; }

    /// <summary>扣除输出预留后的可用输入预算</summary>
    int AvailableInputTokens { get; }

    /// <summary>
    /// 消息添加回调（可选，用于自动持久化）
    /// 当 LoopContext.AddMessage 被调用时触发
    /// </summary>
    Action<Message>? OnMessageAdded { get; set; }
}
