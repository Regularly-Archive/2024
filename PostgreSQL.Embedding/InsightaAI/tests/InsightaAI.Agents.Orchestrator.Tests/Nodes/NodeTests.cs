using InsightaAI.Agents.Orchestrator.Nodes;

namespace InsightaAI.Agents.Orchestrator.Tests.Nodes;

public class NodeTests
{
    [Fact]
    public void FunctionNode_Kind_ShouldBeFunction()
    {
        var node = new FunctionNode
        {
            Id = "f1",
            Name = "Test",
            Execute = ctx => Task.FromResult<object?>(null)
        };

        Assert.Equal(NodeKind.Function, node.Kind);
    }

    [Fact]
    public void AgentNode_WithToolNames_ShouldBePresetAgent()
    {
        var node = new AgentNode
        {
            Id = "a1",
            Name = "Test",
            AgentId = "analyst",
            ToolNames = ["jupyter", "duckdb"]
        };

        Assert.Equal(NodeKind.PresetAgent, node.Kind);
    }

    [Fact]
    public void AgentNode_WithoutToolNames_ShouldBeSubAgent()
    {
        var node = new AgentNode
        {
            Id = "s1",
            Name = "Test",
            AgentId = "default"
        };

        Assert.Equal(NodeKind.SubAgent, node.Kind);
    }

    [Fact]
    public void AgentNode_EmptyToolNames_ShouldBePresetAgent()
    {
        var node = new AgentNode
        {
            Id = "a1",
            Name = "Test",
            AgentId = "default",
            ToolNames = []
        };

        // 空数组 != null，所以是 PresetAgent
        Assert.Equal(NodeKind.PresetAgent, node.Kind);
    }

    [Fact]
    public void DAGNode_DefaultValues_ShouldBeCorrect()
    {
        var node = new FunctionNode
        {
            Id = "test",
            Name = "Test",
            Execute = ctx => Task.FromResult<object?>(null)
        };

        Assert.Empty(node.DependsOn);
        Assert.Empty(node.InputArtifacts);
        Assert.Empty(node.OutputArtifacts);
        Assert.Null(node.Description);
    }

    [Fact]
    public void DAGNode_WithDependencies_ShouldStoreThem()
    {
        var node = new FunctionNode
        {
            Id = "test",
            Name = "Test",
            DependsOn = ["dep1", "dep2"],
            Execute = ctx => Task.FromResult<object?>(null)
        };

        Assert.Equal(2, node.DependsOn.Length);
        Assert.Contains("dep1", node.DependsOn);
        Assert.Contains("dep2", node.DependsOn);
    }
}
