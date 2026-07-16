using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Skills;

namespace InsightaAI.Agent.Context.SystemPrompt;

/// <summary>
/// SystemPromptBuilder 的输入参数
/// </summary>
public sealed record SystemPromptParams
{
    /// <summary>Agent 系统提示词（来自 AgentConfig）</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>项目级上下文（AGENTS.md），来自工作目录</summary>
    public string? AgentsMd { get; init; }

    /// <summary>全部可用 Skills（Builder 内部排除已激活的）</summary>
    public IReadOnlyList<SkillMetadata>? AllSkills { get; init; }

    /// <summary>已激活的 Skills（含 Instructions）</summary>
    public IReadOnlyList<ISkill>? ActivatedSkills { get; init; }

    /// <summary>可用 MCP 服务器</summary>
    public IReadOnlyList<McpServerMetadata>? McpServers { get; init; }

    /// <summary>Memory 索引文本</summary>
    public string? MemoryIndex { get; init; }
}
