using InsightaAI.Orchestrator.Storage;

namespace InsightaAI.Orchestrator.Tests.Storage;

public class ArtifactStoreTests
{
    [Fact]
    public void Set_And_Get_ShouldWork()
    {
        var store = new ArtifactStore();
        store.Set("report", "Hello World");

        var result = store.Get<string>("report");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Has_ShouldReturnTrueForExistingArtifact()
    {
        var store = new ArtifactStore();
        store.Set("report", "data");

        Assert.True(store.Has("report"));
        Assert.False(store.Has("missing"));
    }

    [Fact]
    public void AreDependenciesMet_ShouldReturnTrueWhenAllExist()
    {
        var store = new ArtifactStore();
        store.Set("input1", "data1");
        store.Set("input2", "data2");

        Assert.True(store.AreDependenciesMet(["input1", "input2"]));
    }

    [Fact]
    public void AreDependenciesMet_ShouldReturnFalseWhenMissing()
    {
        var store = new ArtifactStore();
        store.Set("input1", "data1");

        Assert.False(store.AreDependenciesMet(["input1", "input2"]));
    }

    [Fact]
    public void AreDependenciesMet_EmptyArray_ShouldReturnTrue()
    {
        var store = new ArtifactStore();
        Assert.True(store.AreDependenciesMet([]));
    }

    [Fact]
    public void Snapshot_ShouldReturnCurrentState()
    {
        var store = new ArtifactStore();
        store.Set("a", 1);
        store.Set("b", "hello");

        var snapshot = store.Snapshot();
        Assert.Equal(2, snapshot.Count);
    }

    [Fact]
    public void Clear_ShouldRemoveAllArtifacts()
    {
        var store = new ArtifactStore();
        store.Set("a", 1);
        store.Set("b", 2);

        store.Clear();
        Assert.False(store.Has("a"));
        Assert.False(store.Has("b"));
    }
}
