using InsightaAI.Agent.Models;
using InsightaAI.Orchestrator.Storage;

namespace InsightaAI.Orchestrator.Core;

/// <summary>
/// Team - 编排基础设施容器
/// 持有 Agent 配置、共享内存和 Artifact 存储
/// </summary>
public sealed class Team
{
    /// <summary>Team 名称</summary>
    public required string Name { get; init; }

    /// <summary>此 Team 中可用的 Agent 配置</summary>
    public required AgentConfig[] Agents { get; init; }

    /// <summary>TaskPlanner 使用的模型（可选，默认 gpt-4o）</summary>
    public string? PlannerModel { get; init; }

    /// <summary>全局共享内存</summary>
    public SharedMemory SharedMemory { get; } = new();

    /// <summary>Artifact 存储（节点间数据契约）</summary>
    public ArtifactStore ArtifactStore { get; } = new();

    /// <summary>
    /// 根据 ID 获取 Agent 配置
    /// </summary>
    public AgentConfig? GetAgent(string agentId)
    {
        return Agents.FirstOrDefault(a => a.Id == agentId);
    }
}
