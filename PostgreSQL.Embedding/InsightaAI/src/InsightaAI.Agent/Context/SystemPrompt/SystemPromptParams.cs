using InsightaAI.Agent.Mcp;
using InsightaAI.Agent.Skills;

namespace InsightaAI.Agent.Context.SystemPrompt;

/// <summary>
/// SystemPromptBuilder 的输入参数
/// </summary>
public sealed record SystemPromptParams
{
    /// <summary>用户自定义指令（来自 AgentConfig）</summary>
    public string CustomInstructions { get; init; } = "";

    /// <summary>项目级上下文（AGENTS.md），来自工作目录</summary>
    public string? AgentsMd { get; init; }

    /// <summary>全部可用 Skills 的元数据（包含已激活的 Skills）</summary>
    public IReadOnlyList<SkillMetadata>? AllSkills { get; init; }

    /// <summary>已激活的 Skills（含 Instructions）</summary>
    public IReadOnlyList<ISkill>? ActivatedSkills { get; init; }

    /// <summary>可用 MCP 服务器</summary>
    public IReadOnlyList<McpServerMetadata>? McpServers { get; init; }

    /// <summary>Memory 索引文本</summary>
    public string? MemoryIndex { get; init; }
}
