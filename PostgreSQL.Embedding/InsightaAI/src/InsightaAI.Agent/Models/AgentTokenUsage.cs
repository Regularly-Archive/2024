namespace InsightaAI.Agent.Models;

public sealed record AgentTokenUsage
{
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
}