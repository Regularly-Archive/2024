using System.Diagnostics;
using InsightaAI.Agent;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using InsightaAI.Orchestrator.Core;
using InsightaAI.Orchestrator.Nodes;
using InsightaAI.Orchestrator.Results;
using InsightaAI.Orchestrator.Storage;

namespace InsightaAI.Orchestrator.Execution;

/// <summary>
/// 内部节点执行器 - 根据 NodeKind 分发执行
/// </summary>
internal sealed class NodeExecutor
{
    private readonly ILlmClient _llmClient;
    private readonly Team? _team;
    private readonly Func<string[], ToolRegistry>? _toolRegistryFactory;

    public NodeExecutor(ILlmClient llmClient, Team? team, Func<string[], ToolRegistry>? toolRegistryFactory = null)
    {
        _llmClient = llmClient;
        _team = team;
        _toolRegistryFactory = toolRegistryFactory;
    }

    /// <summary>
    /// 执行单个节点并返回结果
    /// </summary>
    public async Task<NodeResult> ExecuteAsync(
        DAGNode node,
        NodeContext context,
        CancellationToken cancellationToken)
    {
        return node switch
        {
            FunctionNode fn => await ExecuteFunctionNode(fn, context, cancellationToken),
            AgentNode an => await ExecuteAgentNode(an, context, cancellationToken),
            _ => throw new NotSupportedException($"Unknown node type: {node.GetType()}")
        };
    }

    /// <summary>
    /// 执行 FunctionNode
    /// </summary>
    private async Task<NodeResult> ExecuteFunctionNode(
        FunctionNode node,
        NodeContext context,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var output = await node.Execute(context);
            sw.Stop();
            return new NodeResult
            {
                NodeId = node.Id,
                NodeName = node.Name,
                NodeKind = node.Kind,
                Status = NodeResultStatus.Success,
                Output = output,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NodeResult
            {
                NodeId = node.Id,
                NodeName = node.Name,
                NodeKind = node.Kind,
                Status = NodeResultStatus.Failed,
                Error = ex.Message,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// 执行 AgentNode
    /// </summary>
    private async Task<NodeResult> ExecuteAgentNode(
        AgentNode node,
        NodeContext context,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. 查找 AgentConfig
            if (_team == null)
                throw new InvalidOperationException("Team is required for AgentNode execution");

            var agentConfig = _team.GetAgent(node.AgentId);
            if (agentConfig == null)
                throw new InvalidOperationException($"Agent '{node.AgentId}' not found in team");

            // 2. 构建 ToolRegistry（通过工厂或创建空注册表）
            var toolRegistry = node.ToolNames != null && _toolRegistryFactory != null
                ? _toolRegistryFactory(node.ToolNames)
                : new ToolRegistry();

            // 3. 覆盖 SystemPrompt
            var config = agentConfig;
            if (!string.IsNullOrEmpty(node.SystemPrompt))
            {
                config = config with { SystemPrompt = node.SystemPrompt };
            }

            // 4. 构建输入文本
            var input = BuildAgentInput(node, context);

            // 5. 创建 Agent 实例并执行
            var agent = new Agent.Agent(config, _llmClient, toolRegistry);
            var result = await agent.RunAsync(input, null, cancellationToken);

            sw.Stop();

            // 6. 提取输出
            var output = result.Message?.Content?
                .OfType<TextBlock>()
                .FirstOrDefault()?.Text ?? "";

            return new NodeResult
            {
                NodeId = node.Id,
                NodeName = node.Name,
                NodeKind = node.Kind,
                Status = NodeResultStatus.Success,
                Output = output,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new NodeResult
            {
                NodeId = node.Id,
                NodeName = node.Name,
                NodeKind = node.Kind,
                Status = NodeResultStatus.Failed,
                Error = ex.Message,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    /// <summary>
    /// 构建 Agent 节点的输入文本
    /// </summary>
    private static string BuildAgentInput(AgentNode node, NodeContext context)
    {
        var sb = new System.Text.StringBuilder();

        // 添加任务描述
        if (!string.IsNullOrEmpty(node.TaskDescription))
        {
            sb.AppendLine(node.TaskDescription);
        }

        // 添加依赖输出
        if (context.Dependencies.Count > 0)
        {
            sb.AppendLine("\n## Dependencies:");
            foreach (var (depId, depOutput) in context.Dependencies)
            {
                sb.AppendLine($"### {depId}:");
                sb.AppendLine(depOutput?.ToString() ?? "(null)");
            }
        }

        // 添加上下文输入
        if (!string.IsNullOrEmpty(context.Input))
        {
            sb.AppendLine("\n## Input:");
            sb.AppendLine(context.Input);
        }

        return sb.ToString();
    }
}
