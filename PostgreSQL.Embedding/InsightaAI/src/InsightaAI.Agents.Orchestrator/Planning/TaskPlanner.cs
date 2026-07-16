using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.Agents.Orchestrator.Core;
using InsightaAI.Agents.Orchestrator.Nodes;

namespace InsightaAI.Agents.Orchestrator.Planning;

/// <summary>
/// 基于 LLM 的任务规划器 - 将目标分解为 DAG 节点
/// 适配现有 TaskPlanner 模式
/// </summary>
public sealed partial class TaskPlanner
{
    private readonly ILlmClient _llmClient;
    private readonly string _model;
    private readonly string _plannerPrompt;

    [GeneratedRegex(@"```json\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase)]
    private static partial Regex JsonBlockRegex();

    public TaskPlanner(ILlmClient llmClient, string model, string? customPrompt = null)
    {
        ArgumentNullException.ThrowIfNull(llmClient);
        ArgumentNullException.ThrowIfNull(model);
        _llmClient = llmClient;
        _model = model;
        _plannerPrompt = customPrompt ?? LoadDefaultPrompt();
    }

    /// <summary>
    /// 使用 LLM 将目标分解为 DAG 节点
    /// </summary>
    public async Task<DAGNode[]> PlanAsync(
        string goal,
        Team team,
        int maxTasks = 10,
        CancellationToken cancellationToken = default)
    {
        // 1. 构建 prompt
        var agentDescriptions = BuildAgentDescriptions(team);
        var prompt = _plannerPrompt
            .Replace("{{$input}}", goal)
            .Replace("{{$language}}", "Chinese")
            .Replace("{{$limit}}", maxTasks.ToString())
            .Replace("{{$agents}}", agentDescriptions);

        // 2. 调用 LLM
        var request = new LlmRequest
        {
            Model = _model,
            Messages =
            [
                Message.FromUser(prompt)
            ],
            Stream = false
        };

        var response = await _llmClient.CompleteAsync(request, cancellationToken);
        var responseText = response.Content
            .OfType<TextBlock>()
            .FirstOrDefault()?.Text ?? "";

        // 3. 解析 JSON 响应
        var json = ExtractJson(responseText);
        var plannerResult = JsonSerializer.Deserialize<PlannerResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (plannerResult == null || plannerResult.Tasks.Length == 0)
            throw new InvalidOperationException("Failed to parse planner result");

        // 4. 转换为 DAGNode[]
        return ConvertToNodes(plannerResult, team);
    }

    /// <summary>
    /// 将 PlannerResult 转换为 DAGNode[]
    /// </summary>
    private static DAGNode[] ConvertToNodes(PlannerResult result, Team team)
    {
        var nodes = new List<DAGNode>();
        var idMapping = new Dictionary<int, string>();

        // 第一遍：创建 ID 映射
        foreach (var task in result.Tasks)
        {
            idMapping[task.Id] = task.Id.ToString();
        }

        // 第二遍：创建节点
        foreach (var task in result.Tasks)
        {
            var dependsOn = task.DependsOn
                .Where(d => idMapping.ContainsKey(d))
                .Select(d => idMapping[d])
                .ToArray();

            // 检查是否有可用工具
            if (task.AvailableTools.Length > 0)
            {
                // PresetAgent：有预配置工具
                var agentId = FindBestAgent(team, task.AvailableTools);
                nodes.Add(new AgentNode
                {
                    Id = idMapping[task.Id],
                    Name = task.Name,
                    Description = task.Desc,
                    DependsOn = dependsOn,
                    AgentId = agentId ?? team.Agents.First().Id,
                    ToolNames = task.AvailableTools,
                    TaskDescription = task.Desc,
                    InputArtifacts = task.RequiredArtifacts,
                    OutputArtifacts = task.OutputArtifacts
                });
            }
            else
            {
                // SubAgent：无预配置工具，由 LLM 动态分配
                var agentId = team.Agents.FirstOrDefault()?.Id ?? "default";
                nodes.Add(new AgentNode
                {
                    Id = idMapping[task.Id],
                    Name = task.Name,
                    Description = task.Desc,
                    DependsOn = dependsOn,
                    AgentId = agentId,
                    ToolNames = null, // SubAgent
                    TaskDescription = task.Desc,
                    InputArtifacts = task.RequiredArtifacts,
                    OutputArtifacts = task.OutputArtifacts
                });
            }
        }

        return nodes.ToArray();
    }

    /// <summary>
    /// 查找最适合的 Agent（基于工具匹配）
    /// </summary>
    private static string? FindBestAgent(Team team, string[] requiredTools)
    {
        // 简单匹配：找到包含最多所需工具的 Agent
        // 实际实现可能需要更复杂的匹配逻辑
        return team.Agents.FirstOrDefault()?.Id;
    }

    /// <summary>
    /// 构建 Agent 描述字符串
    /// </summary>
    private static string BuildAgentDescriptions(Team team)
    {
        var sb = new StringBuilder();
        foreach (var agent in team.Agents)
        {
            sb.AppendLine($"- {agent.Id}: {agent.SystemPrompt?.Truncate(100) ?? "No description"}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 从 LLM 响应中提取 JSON（处理 ```json 代码块）
    /// </summary>
    private static string ExtractJson(string text)
    {
        var match = JsonBlockRegex().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
    }

    /// <summary>
    /// 加载默认 prompt
    /// </summary>
    private static string LoadDefaultPrompt()
    {
        return """
        # Role
        You are a Task Decomposition Expert. Your core goal is to break down complex user requests
        into executable subtasks structured as a Directed Acyclic Graph (DAG).

        # Context
        - Language: {{$language}}
        - Available Agents: {{$agents}}
        - Maximum Subtasks: {{$limit}}

        # Output Format (Strict JSON)
        {
          "thought": "Concise analysis...",
          "tasks": [
            {
              "id": 0,
              "name": "Task Name",
              "desc": "Detailed description...",
              "depends_on": [],
              "available_tools": [],
              "required_artifacts": [],
              "output_artifacts": []
            }
          ]
        }

        # Current Request
        {{$input}}

        Decompose the above request. Output ONLY the valid JSON object.
        """;
    }
}

/// <summary>
/// 字符串截断扩展
/// </summary>
internal static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
