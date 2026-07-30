using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Compaction;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent;

/// <summary>
/// ILoopContext 的默认实现，封装消息列表和上下文管理器
/// </summary>
public sealed class LoopContext : ILoopContext
{
    private readonly List<Message> _messages;
    private readonly IContextManager? _contextManager;

    public LoopContext(string sessionId, string agentId, IContextManager? contextManager = null)
    {
        SessionId = sessionId;
        AgentId = agentId;
        _contextManager = contextManager;
        _messages = [];
    }

    public string SessionId { get; }
    public string AgentId { get; }

    public IReadOnlyList<Message> Messages => _messages;

    public Action<Message>? OnMessageAdded { get; set; }

    public void AddMessage(Message message)
    {
        _messages.Add(message);
        OnMessageAdded?.Invoke(message);
    }

    public void AddMessages(IEnumerable<Message> messages)
    {
        foreach (var message in messages)
        {
            _messages.Add(message);
            OnMessageAdded?.Invoke(message);
        }
    }

    public void ReplaceMessage(int index, Message message)
    {
        _messages[index] = message;
    }

    public async Task<CompactionResult?> CompactIfNeededAsync(CancellationToken ct)
    {
        if (_contextManager == null) return null;
        return await _contextManager.CompactIfNeededAsync(_messages, ct);
    }

    public int EstimateTokens()
    {
        return _contextManager?.EstimateTokens(_messages) ?? 0;
    }

    public int MaxContextTokens => _contextManager?.MaxContextTokens ?? 0;

    public int AvailableInputTokens => _contextManager?.AvailableInputTokens ?? 0;
}
