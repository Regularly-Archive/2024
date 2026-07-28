using InsightaAI.Agent;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// CLI Agent 创建工厂。
/// </summary>
public interface IAgentFactory
{
    Task<Agent> CreateAsync(
        AgentCreationOptions options,
        CancellationToken cancellationToken = default);
}
