using InsightaAI.Agent.Prompts;
using System.Text;

namespace InsightaAI.Agent.Context.SystemPrompt;

/// <summary>
/// System Prompt 组装器 — 纯函数，接收参数，返回完整的 system prompt 文本。
/// 每次调用从头构建，不持有状态。
/// </summary>
public static class SystemPromptBuilder
{
    private static string? _coreInstructions;

    /// <summary>
    /// 组装 system prompt
    /// Layer 1: Core Instructions → Layer 2: AGENTS.md → Layer 3: Agent SystemPrompt → Layer 4: Dynamic Context
    /// </summary>
    public static async Task<string> BuildAsync(SystemPromptParams p)
    {
        var sb = new StringBuilder();

        // Layer 1: Core Instructions（框架内置，不可配置，懒加载缓存）
        _coreInstructions ??= await PromptTemplate.LoadAsync("core-instructions");
        sb.Append(_coreInstructions);

        // Layer 2: AGENTS.md（项目级上下文）
        if (!string.IsNullOrWhiteSpace(p.AgentsMd))
        {
            sb.AppendLine();
            sb.Append(p.AgentsMd);
        }

        // Layer 3: Agent SystemPrompt（用户定制指令）
        if (!string.IsNullOrWhiteSpace(p.SystemPrompt))
        {
            sb.AppendLine();
            sb.Append(p.SystemPrompt);
        }

        // Layer 4A: 可用 Skills（排除已激活的）
        if (p.AllSkills is { Count: > 0 })
        {
            var activatedNames = p.ActivatedSkills?
                .Select(s => s.Metadata.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

            var available = p.AllSkills
                .Where(s => !activatedNames.Contains(s.Name))
                .OrderBy(s => s.Name, StringComparer.Ordinal)
                .ToList();

            if (available.Count > 0)
            {
                var skillsList = string.Join("\n",
                    available.Select(s => $"- **{s.Name}**: {s.Description}"));

                sb.Append(await PromptTemplate.RenderAsync("available-skills",
                    new Dictionary<string, string> { ["skills_list"] = skillsList }));
            }
        }

        // Layer 4B: 可用 MCP 服务器
        if (p.McpServers is { Count: > 0 })
        {
            var serversList = string.Join("\n",
                p.McpServers.OrderBy(s => s.Name, StringComparer.Ordinal)
                    .Select(s => $"- **{s.Name}**: {s.Description}"));

            sb.Append(await PromptTemplate.RenderAsync("available-mcps",
                new Dictionary<string, string> { ["mcp_servers_list"] = serversList }));
        }

        // Layer 4C: Memory 索引
        if (!string.IsNullOrWhiteSpace(p.MemoryIndex))
        {
            sb.Append(await PromptTemplate.RenderAsync("available-memories",
                new Dictionary<string, string>
                {
                    ["memory_index"] = string.IsNullOrWhiteSpace(p.MemoryIndex)
                        ? "_No memories stored yet._"
                        : p.MemoryIndex
                }));
        }

        // Layer 4D: 已激活 Skills 的 Instructions
        if (p.ActivatedSkills is { Count: > 0 })
        {
            var instructionsText = string.Join("\n\n",
                p.ActivatedSkills
                    .OrderBy(s => s.Metadata.Name, StringComparer.Ordinal)
                    .Select(s => s.Instructions));

            sb.Append(await PromptTemplate.RenderAsync("activated-skills",
                new Dictionary<string, string> { ["activated_skills_list"] = instructionsText }));
        }

        return sb.ToString();
    }
}
