using System.Runtime.CompilerServices;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.Agents.Subagents.Invocation;
using InsightaAI.LLM.Abstractions;
using InsightaAI.Agents.Orchestrator.Events;
using InsightaAI.Agents.Orchestrator.Execution;
using InsightaAI.Agents.Orchestrator.HumanInTheLoop;
using InsightaAI.Agents.Orchestrator.Nodes;
using InsightaAI.Agents.Orchestrator.Planning;
using InsightaAI.Agents.Orchestrator.Results;
using InsightaAI.Agents.Orchestrator.Scheduling;
using InsightaAI.Agents.Orchestrator.Storage;

namespace InsightaAI.Agents.Orchestrator.Core;

/// <summary>
/// 编排器 - 多 Agent 编排主入口
/// 支持三种模式：目标优先、手动 DAG、单 Agent
/// </summary>
public sealed class Orchestrator
{
    private readonly Team? _team;
    private readonly ILlmClient _llmClient;
    private readonly NodeExecutor _nodeExecutor;
    private readonly SharedMemory _memory;
    private readonly ArtifactStore _artifactStore;

    public Orchestrator(
        ILlmClient llmClient,
        Team? team = null,
        SubagentDispatcher? subagentDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(llmClient);
        _llmClient = llmClient;
        _team = team;
        _nodeExecutor = new NodeExecutor(team, subagentDispatcher);
        _memory = team?.SharedMemory ?? new SharedMemory();
        _artifactStore = team?.ArtifactStore ?? new ArtifactStore();
    }

    // ===== 公共 API =====

    /// <summary>
    /// 目标优先：LLM 自动拆解目标为 DAG 并执行
    /// </summary>
    public async Task<TeamResult> RunTeamAsync(
        string goal,
        int maxTasks = 10,
        string? plannerModel = null,
        CancellationToken cancellationToken = default)
    {
        if (_team == null)
            throw new InvalidOperationException("Team is required for RunTeamAsync");

        // 1. 使用 TaskPlanner 分解目标
        var model = plannerModel ?? _team.PlannerModel ?? "gpt-4o";
        var planner = new TaskPlanner(_llmClient, model);
        var nodes = await planner.PlanAsync(goal, _team, maxTasks, cancellationToken);

        // 2. 执行
        return await RunTasksAsync(nodes, cancellationToken);
    }

    /// <summary>
    /// 手动 DAG：执行预定义的节点集合（流式 API）
    /// </summary>
    public async IAsyncEnumerable<OrchestratorEvent> RunTasksStreamAsync(
        DAGNode[] nodes,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 联合外部 cancellationToken 和内部 Cts
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Cts.Token);
        var token = linkedCts.Token;

        // 1. 验证 DAG
        var scheduler = new DAGScheduler(nodes);
        var validation = scheduler.Validate();
        if (!validation.IsValid)
        {
            yield return new OrchestratorErrorEvent
            {
                Message = $"DAG validation failed: {string.Join("; ", validation.Errors)}"
            };
            yield break;
        }

        // 2. 触发 OnPlanReady 事件
        var effectiveNodes = nodes;
        if (OnPlanReady != null)
        {
            var context = new PlanApprovalContext
            {
                Nodes = nodes,
                Memory = _memory
            };

            var approval = await OnPlanReady(context);
            if (!approval.Approved)
            {
                yield return new PlanRejectedEvent { Reason = "Plan rejected by user" };
                yield break;
            }

            if (approval.ModifiedNodes != null)
            {
                effectiveNodes = approval.ModifiedNodes;
                scheduler = new DAGScheduler(effectiveNodes);
            }

            yield return new PlanApprovedEvent();
        }

        // 3. 发送计划创建事件
        yield return new PlanCreatedEvent { Nodes = effectiveNodes };

        // 4. 执行循环
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        var allResults = new List<NodeResult>();

        while (!scheduler.IsComplete && !token.IsCancellationRequested)
        {
            var readyTasks = scheduler.GetReadyTasks();
            if (readyTasks.Length == 0 && !scheduler.IsComplete)
            {
                // 安全检查：不应该发生
                break;
            }

            // 发送批次开始事件
            yield return new BatchStartEvent
            {
                NodeIds = readyTasks.Select(n => n.Id).ToArray()
            };

            // 并行执行就绪任务
            var tasks = readyTasks.Select(node => ExecuteNodeAsync(node, scheduler, token));
            var results = await Task.WhenAll(tasks);

            foreach (var result in results)
            {
                allResults.Add(result);

                if (result.Status == NodeResultStatus.Success)
                {
                    scheduler.MarkComplete(result.NodeId, result.Output);

                    // 存储 Artifacts
                    var completedNode = scheduler.GetNode(result.NodeId) ?? effectiveNodes.First(n => n.Id == result.NodeId);
                    StoreArtifacts(completedNode, result);
                }
                else
                {
                    scheduler.MarkFailed(result.NodeId, new Exception(result.Error));
                    scheduler.MarkDownstreamSkipped(result.NodeId);
                }

                // 发送节点完成事件
                yield return new NodeCompleteEvent { Result = result };

                // Human-in-the-loop 回调
                if (OnTaskComplete != null && result.Status == NodeResultStatus.Success)
                {
                    var node = scheduler.GetNode(result.NodeId);
                    var approvalContext = new TaskApprovalContext
                    {
                        Node = node ?? effectiveNodes.First(n => n.Id == result.NodeId),
                        Result = result.Output,
                        Memory = _memory
                    };

                    var approval = await OnTaskComplete(approvalContext);
                    switch (approval)
                    {
                        case TaskApprovalResult.Abort:
                            yield return new OrchestratorCompleteEvent
                            {
                                Result = CreateResult(TeamResultStatus.Aborted, allResults, totalSw)
                            };
                            yield break;
                        case TaskApprovalResult.Pause:
                            throw new NotSupportedException(
                                "Pause is not yet implemented. Use Abort and re-run from plan instead.");
                    }
                }
            }

            // 发送批次完成事件
            yield return new BatchCompleteEvent
            {
                NodeIds = readyTasks.Select(n => n.Id).ToArray()
            };
        }

        totalSw.Stop();

        // 5. 发送完成事件
        var status = token.IsCancellationRequested
            ? TeamResultStatus.Cancelled
            : scheduler.HasFailures
                ? TeamResultStatus.Failed
                : TeamResultStatus.Completed;

        yield return new OrchestratorCompleteEvent
        {
            Result = CreateResult(status, allResults, totalSw)
        };
    }

    /// <summary>
    /// 手动 DAG：执行预定义的节点集合（非流式便捷方法）
    /// </summary>
    public async Task<TeamResult> RunTasksAsync(
        DAGNode[] nodes,
        CancellationToken cancellationToken = default)
    {
        TeamResult? result = null;
        await foreach (var evt in RunTasksStreamAsync(nodes, cancellationToken))
        {
            if (evt is OrchestratorCompleteEvent complete)
                result = complete.Result;
        }
        return result ?? new TeamResult
        {
            Status = TeamResultStatus.Failed,
            NodeResults = [],
            Error = "No completion event received"
        };
    }

    /// <summary>
    /// 单 Agent：直接运行一个 Agent（无 DAG）
    /// </summary>
    public async Task<AgentResult> RunAgentAsync(
        AgentConfig config,
        string input,
        CancellationToken cancellationToken = default)
    {
        var toolRegistry = new ToolRegistry();
        // TODO: 从 config 加载工具
        var agent = new InsightaAI.Agent.Agent(config, _llmClient, toolRegistry);
        return await agent.RunAsync(input, null, cancellationToken);
    }

    /// <summary>
    /// 从保存的计划恢复执行（跳过规划阶段）
    /// </summary>
    public Task<TeamResult> RunFromPlanAsync(
        DAGPlan plan,
        CancellationToken cancellationToken = default)
    {
        var nodes = ConvertFromPlan(plan);
        return RunTasksAsync(nodes, cancellationToken);
    }

    /// <summary>创建可序列化的计划</summary>
    public DAGPlan CreatePlan(DAGNode[] nodes, string? goal = null)
    {
        return new DAGPlan
        {
            Goal = goal,
            Nodes = nodes.Select(n => new DAGNodeDto
            {
                Id = n.Id,
                Name = n.Name,
                Kind = n.Kind,
                DependsOn = n.DependsOn,
                InputArtifacts = n.InputArtifacts,
                OutputArtifacts = n.OutputArtifacts,
                Description = n.Description,
                AgentId = n is AgentNode an ? an.AgentId : null,
                ToolNames = n is AgentNode an2 ? an2.ToolNames : null,
                SystemPrompt = n is AgentNode an3 ? an3.SystemPrompt : null,
                TaskDescription = n is AgentNode an4 ? an4.TaskDescription : null
            }).ToArray()
        };
    }

    // ===== Human-in-the-loop 事件 =====

    /// <summary>执行前审批整个计划</summary>
    public event Func<PlanApprovalContext, Task<PlanApprovalResult>>? OnPlanReady;

    /// <summary>每个任务完成后回调</summary>
    public event Func<TaskApprovalContext, Task<TaskApprovalResult>>? OnTaskComplete;

    /// <summary>
    /// 取消令牌源（可通过 Cts.Cancel() 外部触发取消）
    /// 与传入的 CancellationToken 联合使用
    /// </summary>
    public CancellationTokenSource Cts { get; } = new();

    // ===== 内部辅助方法 =====

    /// <summary>
    /// 执行单个节点
    /// </summary>
    private async Task<NodeResult> ExecuteNodeAsync(
        DAGNode node,
        DAGScheduler scheduler,
        CancellationToken cancellationToken)
    {
        var context = BuildNodeContext(node, scheduler);
        return await _nodeExecutor.ExecuteAsync(node, context, cancellationToken);
    }

    /// <summary>
    /// 构建节点执行上下文
    /// </summary>
    private NodeContext BuildNodeContext(DAGNode node, DAGScheduler scheduler)
    {
        var dependencies = new Dictionary<string, object?>();
        foreach (var depId in node.DependsOn)
        {
            var result = scheduler.GetResult(depId);
            if (result != null)
                dependencies[depId] = result;
        }

        return new NodeContext
        {
            Input = string.Join("\n\n", dependencies.Values.Where(v => v != null).Select(v => v!.ToString())),
            Dependencies = dependencies,
            Memory = _memory,
            Artifacts = _artifactStore,
            CancellationToken = Cts.Token
        };
    }

    /// <summary>
    /// 存储节点输出的 Artifacts
    /// </summary>
    private void StoreArtifacts(DAGNode node, NodeResult result)
    {
        if (node.OutputArtifacts.Length > 0 && result.Output != null)
        {
            foreach (var artifactName in node.OutputArtifacts)
            {
                _artifactStore.Set(artifactName, result.Output);
            }
        }
    }

    /// <summary>
    /// 创建 TeamResult
    /// </summary>
    private static TeamResult CreateResult(
        TeamResultStatus status,
        List<NodeResult> results,
        System.Diagnostics.Stopwatch sw)
    {
        var lastSuccessResult = results.LastOrDefault(r => r.Status == NodeResultStatus.Success);

        return new TeamResult
        {
            Status = status,
            NodeResults = results.ToArray(),
            TotalDurationMs = sw.ElapsedMilliseconds,
            FinalOutput = lastSuccessResult?.Output?.ToString()
        };
    }

    /// <summary>
    /// 从 DAGPlan 转换为 DAGNode[]
    /// 注意：FunctionNode 无法从计划恢复（委托不可序列化）
    /// </summary>
    private static DAGNode[] ConvertFromPlan(DAGPlan plan)
    {
        return plan.Nodes.Select(dto => (DAGNode)new AgentNode
        {
            Id = dto.Id,
            Name = dto.Name,
            DependsOn = dto.DependsOn,
            InputArtifacts = dto.InputArtifacts,
            OutputArtifacts = dto.OutputArtifacts,
            Description = dto.Description,
            AgentId = dto.AgentId ?? "default",
            ToolNames = dto.ToolNames,
            SystemPrompt = dto.SystemPrompt,
            TaskDescription = dto.TaskDescription
        }).ToArray();
    }
}
