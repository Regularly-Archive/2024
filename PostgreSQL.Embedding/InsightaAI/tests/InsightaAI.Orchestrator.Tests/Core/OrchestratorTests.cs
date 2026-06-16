using InsightaAI.Orchestrator.Events;
using InsightaAI.Orchestrator.Nodes;
using InsightaAI.Orchestrator.Results;
using InsightaAI.Tests.Shared;
using OrchestratorImpl = InsightaAI.Orchestrator.Core.Orchestrator;

namespace InsightaAI.Orchestrator.Tests.Core;

public class OrchestratorTests
{
    // 使用 null LlmClient 的情况下只能测试 FunctionNode 路径
    // AgentNode 路径需要真实的 LlmClient，留作集成测试

    [Fact]
    public async Task RunTasksAsync_SingleFunctionNode_ShouldSucceed()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());

        var nodes = new DAGNode[]
        {
            new FunctionNode
            {
                Id = "task1",
                Name = "Simple Task",
                Execute = ctx => Task.FromResult<object?>("hello world")
            }
        };

        var result = await orchestrator.RunTasksAsync(nodes);

        Assert.Equal(TeamResultStatus.Completed, result.Status);
        Assert.Single(result.NodeResults);
        Assert.Equal(NodeResultStatus.Success, result.NodeResults[0].Status);
        Assert.Equal("hello world", result.NodeResults[0].Output);
    }

    [Fact]
    public async Task RunTasksAsync_MultipleNodesWithDependencies_ShouldSucceed()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());
        var executionOrder = new List<string>();

        var nodes = new DAGNode[]
        {
            new FunctionNode
            {
                Id = "A",
                Name = "Task A",
                Execute = ctx =>
                {
                    executionOrder.Add("A");
                    return Task.FromResult<object?>("output-A");
                }
            },
            new FunctionNode
            {
                Id = "B",
                Name = "Task B",
                DependsOn = ["A"],
                Execute = ctx =>
                {
                    executionOrder.Add("B");
                    return Task.FromResult<object?>("output-B");
                }
            },
            new FunctionNode
            {
                Id = "C",
                Name = "Task C",
                DependsOn = ["A"],
                Execute = ctx =>
                {
                    executionOrder.Add("C");
                    return Task.FromResult<object?>("output-C");
                }
            }
        };

        var result = await orchestrator.RunTasksAsync(nodes);

        Assert.Equal(TeamResultStatus.Completed, result.Status);
        Assert.Equal(3, result.NodeResults.Length);
        Assert.True(result.NodeResults.All(r => r.Status == NodeResultStatus.Success));

        // A must execute before B and C
        Assert.Equal("A", executionOrder[0]);
        Assert.Contains("B", executionOrder);
        Assert.Contains("C", executionOrder);
    }

    [Fact]
    public async Task RunTasksAsync_WithFailedNode_ShouldReportFailed()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());

        var nodes = new DAGNode[]
        {
            new FunctionNode
            {
                Id = "A",
                Name = "Failing Task",
                Execute = ctx => throw new InvalidOperationException("test error")
            }
        };

        var result = await orchestrator.RunTasksAsync(nodes);

        Assert.Equal(TeamResultStatus.Failed, result.Status);
        Assert.Single(result.NodeResults);
        Assert.Equal(NodeResultStatus.Failed, result.NodeResults[0].Status);
        Assert.Contains("test error", result.NodeResults[0].Error);
    }

    [Fact]
    public async Task RunTasksAsync_DependenciesPassedCorrectly()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());

        var nodes = new DAGNode[]
        {
            new FunctionNode
            {
                Id = "producer",
                Name = "Producer",
                Execute = ctx => Task.FromResult<object?>("data-from-producer")
            },
            new FunctionNode
            {
                Id = "consumer",
                Name = "Consumer",
                DependsOn = ["producer"],
                Execute = ctx =>
                {
                    // 验证依赖输出被正确传递
                    Assert.True(ctx.Dependencies.ContainsKey("producer"));
                    Assert.Equal("data-from-producer", ctx.Dependencies["producer"]);
                    return Task.FromResult<object?>("consumed");
                }
            }
        };

        var result = await orchestrator.RunTasksAsync(nodes);

        Assert.Equal(TeamResultStatus.Completed, result.Status);
        Assert.Equal("consumed", result.NodeResults[1].Output);
    }

    [Fact]
    public async Task RunTasksStreamAsync_ShouldEmitEvents()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());

        var nodes = new DAGNode[]
        {
            new FunctionNode
            {
                Id = "task1",
                Name = "Simple Task",
                Execute = ctx => Task.FromResult<object?>("done")
            }
        };

        var events = new List<OrchestratorEvent>();
        await foreach (var evt in orchestrator.RunTasksStreamAsync(nodes))
        {
            events.Add(evt);
        }

        // 应该有: PlanCreated, BatchStart, NodeComplete, BatchComplete, OrchestratorComplete
        Assert.Contains(events, e => e is PlanCreatedEvent);
        Assert.Contains(events, e => e is BatchStartEvent);
        Assert.Contains(events, e => e is NodeCompleteEvent);
        Assert.Contains(events, e => e is BatchCompleteEvent);
        Assert.Contains(events, e => e is OrchestratorCompleteEvent);
    }

    [Fact]
    public async Task RunTasksAsync_InvalidDAG_ShouldReturnFailed()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());

        // 创建一个有环的 DAG
        var nodes = new DAGNode[]
        {
            new FunctionNode
            {
                Id = "A",
                Name = "Task A",
                DependsOn = ["B"],
                Execute = ctx => Task.FromResult<object?>("a")
            },
            new FunctionNode
            {
                Id = "B",
                Name = "Task B",
                DependsOn = ["A"],
                Execute = ctx => Task.FromResult<object?>("b")
            }
        };

        var result = await orchestrator.RunTasksAsync(nodes);

        Assert.Equal(TeamResultStatus.Failed, result.Status);
    }

    [Fact]
    public async Task RunTasksAsync_OnPlanReady_CanRejectPlan()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());

        orchestrator.OnPlanReady += ctx =>
        {
            return Task.FromResult(new InsightaAI.Orchestrator.HumanInTheLoop.PlanApprovalResult
            {
                Approved = false
            });
        };

        var nodes = new DAGNode[]
        {
            new FunctionNode
            {
                Id = "task1",
                Name = "Task",
                Execute = ctx => Task.FromResult<object?>("done")
            }
        };

        var result = await orchestrator.RunTasksAsync(nodes);

        Assert.Equal(TeamResultStatus.Failed, result.Status);
    }

    [Fact]
    public async Task RunTasksAsync_OnPlanReady_CanModifyPlan()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());
        bool modifiedNodeExecuted = false;

        orchestrator.OnPlanReady += ctx =>
        {
            // 修改计划：替换为新节点
            var modifiedNodes = new DAGNode[]
            {
                new FunctionNode
                {
                    Id = "modified",
                    Name = "Modified Task",
                    Execute = ctx2 =>
                    {
                        modifiedNodeExecuted = true;
                        return Task.FromResult<object?>("modified-output");
                    }
                }
            };

            return Task.FromResult(new InsightaAI.Orchestrator.HumanInTheLoop.PlanApprovalResult
            {
                Approved = true,
                ModifiedNodes = modifiedNodes
            });
        };

        var nodes = new DAGNode[]
        {
            new FunctionNode
            {
                Id = "original",
                Name = "Original Task",
                Execute = ctx => Task.FromResult<object?>("original-output")
            }
        };

        var result = await orchestrator.RunTasksAsync(nodes);

        Assert.Equal(TeamResultStatus.Completed, result.Status);
        Assert.True(modifiedNodeExecuted);
    }

    [Fact]
    public async Task RunTasksAsync_OnTaskComplete_CanAbort()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());
        int executedCount = 0;

        orchestrator.OnTaskComplete += ctx =>
        {
            executedCount++;
            return Task.FromResult(InsightaAI.Orchestrator.HumanInTheLoop.TaskApprovalResult.Abort);
        };

        var nodes = new DAGNode[]
        {
            new FunctionNode
            {
                Id = "task1",
                Name = "Task 1",
                Execute = ctx =>
                {
                    Interlocked.Increment(ref executedCount);
                    return Task.FromResult<object?>("done");
                }
            },
            new FunctionNode
            {
                Id = "task2",
                Name = "Task 2",
                Execute = ctx =>
                {
                    Interlocked.Increment(ref executedCount);
                    return Task.FromResult<object?>("done");
                }
            }
        };

        var result = await orchestrator.RunTasksAsync(nodes);

        Assert.Equal(TeamResultStatus.Aborted, result.Status);
    }

    [Fact]
    public void CreatePlan_ShouldSerializeCorrectly()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());

        var nodes = new DAGNode[]
        {
            new FunctionNode
            {
                Id = "f1",
                Name = "Func1",
                Execute = ctx => Task.FromResult<object?>("x")
            },
            new AgentNode
            {
                Id = "a1",
                Name = "Agent1",
                AgentId = "analyst",
                ToolNames = ["jupyter", "duckdb"],
                TaskDescription = "Analyze data"
            }
        };

        var plan = orchestrator.CreatePlan(nodes, "test goal");

        Assert.Equal("test goal", plan.Goal);
        Assert.Equal(2, plan.Nodes.Length);
        Assert.Equal("f1", plan.Nodes[0].Id);
        Assert.Equal(NodeKind.Function, plan.Nodes[0].Kind);
        Assert.Equal("a1", plan.Nodes[1].Id);
        Assert.Equal(NodeKind.PresetAgent, plan.Nodes[1].Kind);
        Assert.Equal("analyst", plan.Nodes[1].AgentId);
    }

    [Fact]
    public void CreatePlan_SubAgent_ShouldHaveCorrectKind()
    {
        var orchestrator = new OrchestratorImpl(new MockLlmClient());

        var nodes = new DAGNode[]
        {
            new AgentNode
            {
                Id = "sub1",
                Name = "SubAgent",
                AgentId = "default",
                // ToolNames = null → SubAgent
                TaskDescription = "Dynamic task"
            }
        };

        var plan = orchestrator.CreatePlan(nodes);

        Assert.Single(plan.Nodes);
        Assert.Equal(NodeKind.SubAgent, plan.Nodes[0].Kind);
    }
}
