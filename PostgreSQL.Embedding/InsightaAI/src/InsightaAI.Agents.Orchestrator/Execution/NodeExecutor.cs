using System.Diagnostics;
using InsightaAI.Agent.Models;
using InsightaAI.Agents.Subagents.Definitions;
using InsightaAI.Agents.Subagents.Invocation;
using InsightaAI.Agents.Orchestrator.Core;
using InsightaAI.Agents.Orchestrator.Nodes;
using InsightaAI.Agents.Orchestrator.Results;
using InsightaAI.Agents.Orchestrator.Storage;

namespace InsightaAI.Agents.Orchestrator.Execution;

/// <summary>
/// 内部节点执行器 - 根据 NodeKind 分发执行
/// </summary>
internal sealed class NodeExecutor
{
    private readonly Team? _team;
    private readonly SubagentDispatcher? _subagentDispatcher;

    public NodeExecutor(Team? team, SubagentDispatcher? subagentDispatcher)
    {
        _team = team;
        _subagentDispatcher = subagentDispatcher;
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

            if (_subagentDispatcher == null)
                throw new InvalidOperationException(
                    "AgentNode execution requires a SubagentDispatcher supplied by the host.");

            // 2. 使用节点指令覆盖 Agent 的 CustomInstructions
            var definition = new InsightaSubagentDefinition
            {
                Id = agentConfig.Id,
                Name = agentConfig.Name,
                Model = agentConfig.Model,
                Instructions = node.SystemPrompt ?? agentConfig.CustomInstructions,
                MaxTokens = agentConfig.MaxTokens,
                MaxToolRounds = agentConfig.MaxToolRounds,
                ToolNames = node.ToolNames ?? [],
                Capabilities = InsightaSubagentCapabilities.RestrictedDefault
            };

            // 3. 构建输入文本
            var input = BuildAgentInput(node, context);

            // 4. 委托宿主创建和运行独立 Agent，Orchestrator 不直接构造 Agent。
            var invocation = await _subagentDispatcher.InvokeAsync(new SubagentInvocationRequest
            {
                Definition = definition,
                Input = input,
            }, cancellationToken);

            sw.Stop();

            if (invocation.Status != SubagentInvocationStatus.Completed)
            {
                return new NodeResult
                {
                    NodeId = node.Id,
                    NodeName = node.Name,
                    NodeKind = node.Kind,
                    Status = NodeResultStatus.Failed,
                    Error = invocation.Error ?? $"Subagent invocation ended with status '{invocation.Status}'.",
                    DurationMs = sw.ElapsedMilliseconds
                };
            }

            return new NodeResult
            {
                NodeId = node.Id,
                NodeName = node.Name,
                NodeKind = node.Kind,
                Status = NodeResultStatus.Success,
                Output = invocation.Output ?? string.Empty,
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
