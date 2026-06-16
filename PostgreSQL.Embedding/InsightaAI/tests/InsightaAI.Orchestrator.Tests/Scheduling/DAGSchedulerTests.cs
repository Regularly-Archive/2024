using InsightaAI.Orchestrator.Nodes;
using InsightaAI.Orchestrator.Scheduling;

namespace InsightaAI.Orchestrator.Tests.Scheduling;

public class DAGSchedulerTests
{
    private static FunctionNode CreateNode(string id, params string[] dependsOn)
    {
        return new FunctionNode
        {
            Id = id,
            Name = $"Node {id}",
            DependsOn = dependsOn,
            Execute = ctx => Task.FromResult<object?>($"output-{id}")
        };
    }

    [Fact]
    public void SingleNode_ShouldBeReady()
    {
        var nodes = new DAGNode[] { CreateNode("A") };
        var scheduler = new DAGScheduler(nodes);

        var ready = scheduler.GetReadyTasks();
        Assert.Single(ready);
        Assert.Equal("A", ready[0].Id);
    }

    [Fact]
    public void LinearChain_ShouldExecuteInOrder()
    {
        // A -> B -> C
        var nodes = new DAGNode[]
        {
            CreateNode("A"),
            CreateNode("B", "A"),
            CreateNode("C", "B")
        };
        var scheduler = new DAGScheduler(nodes);

        // First batch: A
        var ready = scheduler.GetReadyTasks();
        Assert.Single(ready);
        Assert.Equal("A", ready[0].Id);

        scheduler.MarkComplete("A", "out-A");

        // Second batch: B
        ready = scheduler.GetReadyTasks();
        Assert.Single(ready);
        Assert.Equal("B", ready[0].Id);

        scheduler.MarkComplete("B", "out-B");

        // Third batch: C
        ready = scheduler.GetReadyTasks();
        Assert.Single(ready);
        Assert.Equal("C", ready[0].Id);

        scheduler.MarkComplete("C", "out-C");

        Assert.True(scheduler.IsComplete);
    }

    [Fact]
    public void ParallelNodes_ShouldBeReadyTogether()
    {
        // A, B (no deps) -> C (depends on A, B)
        var nodes = new DAGNode[]
        {
            CreateNode("A"),
            CreateNode("B"),
            CreateNode("C", "A", "B")
        };
        var scheduler = new DAGScheduler(nodes);

        var ready = scheduler.GetReadyTasks();
        Assert.Equal(2, ready.Length);
        Assert.Contains(ready, n => n.Id == "A");
        Assert.Contains(ready, n => n.Id == "B");
    }

    [Fact]
    public void DiamondDAG_ShouldWorkCorrectly()
    {
        //    A
        //   / \
        //  B   C
        //   \ /
        //    D
        var nodes = new DAGNode[]
        {
            CreateNode("A"),
            CreateNode("B", "A"),
            CreateNode("C", "A"),
            CreateNode("D", "B", "C")
        };
        var scheduler = new DAGScheduler(nodes);

        // Batch 1: A
        var ready = scheduler.GetReadyTasks();
        Assert.Single(ready);
        Assert.Equal("A", ready[0].Id);
        scheduler.MarkComplete("A", null);

        // Batch 2: B, C (parallel)
        ready = scheduler.GetReadyTasks();
        Assert.Equal(2, ready.Length);
        scheduler.MarkComplete("B", null);
        scheduler.MarkComplete("C", null);

        // Batch 3: D
        ready = scheduler.GetReadyTasks();
        Assert.Single(ready);
        Assert.Equal("D", ready[0].Id);
        scheduler.MarkComplete("D", null);

        Assert.True(scheduler.IsComplete);
    }

    [Fact]
    public void CycleDetection_ShouldFailValidation()
    {
        // A -> B -> A (cycle)
        var nodes = new DAGNode[]
        {
            CreateNode("A", "B"),
            CreateNode("B", "A")
        };
        var scheduler = new DAGScheduler(nodes);

        var validation = scheduler.Validate();
        Assert.False(validation.IsValid);
        Assert.Contains("cycle", validation.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingDependency_ShouldThrowInConstructor()
    {
        var nodes = new DAGNode[]
        {
            CreateNode("A", "NONEXISTENT")
        };

        Assert.Throws<ArgumentException>(() => new DAGScheduler(nodes));
    }

    [Fact]
    public void GetResult_ShouldReturnMarkedResult()
    {
        var nodes = new DAGNode[]
        {
            CreateNode("A"),
            CreateNode("B", "A")
        };
        var scheduler = new DAGScheduler(nodes);

        scheduler.MarkComplete("A", "result-A");

        Assert.Equal("result-A", scheduler.GetResult("A"));
        Assert.Null(scheduler.GetResult("B"));
    }

    [Fact]
    public void MarkFailed_ShouldTrackFailures()
    {
        var nodes = new DAGNode[]
        {
            CreateNode("A"),
            CreateNode("B", "A")
        };
        var scheduler = new DAGScheduler(nodes);

        scheduler.MarkComplete("A", null);
        scheduler.MarkFailed("B", new Exception("test error"));

        Assert.True(scheduler.HasFailures);
    }

    [Fact]
    public void MarkDownstreamSkipped_ShouldSkipDependents()
    {
        // A -> B -> C
        var nodes = new DAGNode[]
        {
            CreateNode("A"),
            CreateNode("B", "A"),
            CreateNode("C", "B")
        };
        var scheduler = new DAGScheduler(nodes);

        scheduler.MarkComplete("A", null);
        scheduler.MarkFailed("B", new Exception("fail"));
        scheduler.MarkDownstreamSkipped("B");

        // C should be skipped, no ready tasks
        var ready = scheduler.GetReadyTasks();
        Assert.Empty(ready);
    }

    [Fact]
    public void GetParallelBatches_ShouldReturnCorrectBatches()
    {
        //    A
        //   / \
        //  B   C
        //   \ /
        //    D
        var nodes = new DAGNode[]
        {
            CreateNode("A"),
            CreateNode("B", "A"),
            CreateNode("C", "A"),
            CreateNode("D", "B", "C")
        };
        var scheduler = new DAGScheduler(nodes);

        var batches = scheduler.GetParallelBatches();
        Assert.Equal(3, batches.Length);
        Assert.Single(batches[0]); // A
        Assert.Equal(2, batches[1].Length); // B, C
        Assert.Single(batches[2]); // D
    }

    [Fact]
    public void TopologicalSort_ShouldReturnAllNodes()
    {
        var nodes = new DAGNode[]
        {
            CreateNode("A"),
            CreateNode("B", "A"),
            CreateNode("C", "A"),
            CreateNode("D", "B", "C")
        };
        var scheduler = new DAGScheduler(nodes);

        var sorted = scheduler.TopologicalSort();
        Assert.Equal(4, sorted.Length);

        // A must come before B and C
        Assert.True(Array.IndexOf(sorted, "A") < Array.IndexOf(sorted, "B"));
        Assert.True(Array.IndexOf(sorted, "A") < Array.IndexOf(sorted, "C"));
        // B and C must come before D
        Assert.True(Array.IndexOf(sorted, "B") < Array.IndexOf(sorted, "D"));
        Assert.True(Array.IndexOf(sorted, "C") < Array.IndexOf(sorted, "D"));
    }

    [Fact]
    public void EmptyDAG_ShouldBeComplete()
    {
        var scheduler = new DAGScheduler([]);
        Assert.True(scheduler.IsComplete);
        Assert.Empty(scheduler.GetReadyTasks());
    }
}
