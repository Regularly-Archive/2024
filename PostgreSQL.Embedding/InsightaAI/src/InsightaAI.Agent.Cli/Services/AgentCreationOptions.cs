using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Skills;
using InsightaAI.Agent.Context.Summary;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.LLM.Abstractions;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// 创建一个 Agent 所需的运行时上下文。
/// </summary>
public sealed record AgentCreationOptions
{
    public required CliConfig Config { get; init; }
    public required AuthConfig Auth { get; init; }
    public required ILlmClient LlmClient { get; init; }
    public required ModelEntry Model { get; init; }
    public required ToolRegistry ToolRegistry { get; init; }
    public required SkillRegistry SkillRegistry { get; init; }
    public required ISummaryService SummaryService { get; init; }
    public McpRegistry? McpRegistry { get; init; }
    public string? SessionId { get; init; }
}
