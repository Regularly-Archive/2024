using InsightaAI.Agents.Orchestrator.Storage;

namespace InsightaAI.Agents.Orchestrator.Tests.Storage;

public class SharedMemoryTests
{
    [Fact]
    public void Set_And_Get_ShouldWork()
    {
        var memory = new SharedMemory();
        memory.Set("key1", "value1");

        var result = memory.Get<string>("key1");
        Assert.Equal("value1", result);
    }

    [Fact]
    public void Get_WithWrongType_ShouldReturnDefault()
    {
        var memory = new SharedMemory();
        memory.Set("key1", "value1");

        var result = memory.Get<int>("key1");
        Assert.Equal(0, result);
    }

    [Fact]
    public void Get_NonExistentKey_ShouldReturnDefault()
    {
        var memory = new SharedMemory();
        var result = memory.Get<string>("missing");
        Assert.Null(result);
    }

    [Fact]
    public void Has_ShouldReturnTrueForExistingKey()
    {
        var memory = new SharedMemory();
        memory.Set("key1", "value1");

        Assert.True(memory.Has("key1"));
        Assert.False(memory.Has("key2"));
    }

    [Fact]
    public void Delete_ShouldRemoveKey()
    {
        var memory = new SharedMemory();
        memory.Set("key1", "value1");

        var deleted = memory.Delete("key1");
        Assert.True(deleted);
        Assert.False(memory.Has("key1"));
    }

    [Fact]
    public void Delete_NonExistentKey_ShouldReturnFalse()
    {
        var memory = new SharedMemory();
        var deleted = memory.Delete("missing");
        Assert.False(deleted);
    }

    [Fact]
    public void Snapshot_ShouldReturnCurrentState()
    {
        var memory = new SharedMemory();
        memory.Set("a", 1);
        memory.Set("b", 2);

        var snapshot = memory.Snapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal(1, snapshot["a"]);
        Assert.Equal(2, snapshot["b"]);
    }

    [Fact]
    public void Clear_ShouldRemoveAllKeys()
    {
        var memory = new SharedMemory();
        memory.Set("a", 1);
        memory.Set("b", 2);

        memory.Clear();
        Assert.False(memory.Has("a"));
        Assert.False(memory.Has("b"));
    }

    [Fact]
    public void Set_Overwrite_ShouldUpdateValue()
    {
        var memory = new SharedMemory();
        memory.Set("key1", "value1");
        memory.Set("key1", "value2");

        var result = memory.Get<string>("key1");
        Assert.Equal("value2", result);
    }

    [Fact]
    public void Set_NullValue_ShouldBeAllowed()
    {
        var memory = new SharedMemory();
        memory.Set<string?>("key1", null);

        Assert.True(memory.Has("key1"));
        Assert.Null(memory.Get<string?>("key1"));
    }

    [Fact]
    public void SerializeSnapshot_RoundTrip_ShouldPreserveValues()
    {
        var memory = new SharedMemory();
        memory.Set("name", "Alice");
        memory.Set("count", 42);
        memory.Set("active", true);

        var json = memory.SerializeSnapshot();
        var restored = SharedMemory.FromSnapshot(json);

        Assert.Equal("Alice", restored.Get<System.Text.Json.JsonElement>("name").GetString());
        Assert.Equal(42, restored.Get<System.Text.Json.JsonElement>("count").GetInt32());
        Assert.True(restored.Get<System.Text.Json.JsonElement>("active").GetBoolean());
    }

    [Fact]
    public void SerializeSnapshot_WithNull_ShouldPreserveNull()
    {
        var memory = new SharedMemory();
        memory.Set("key", "value");
        memory.Set<string?>("empty", null);

        var json = memory.SerializeSnapshot();
        var restored = SharedMemory.FromSnapshot(json);

        Assert.True(restored.Has("empty"));
        Assert.Null(restored.Get<string?>("empty"));
    }

    [Fact]
    public void SerializeSnapshot_ComplexObject_ShouldWork()
    {
        var memory = new SharedMemory();
        memory.Set("data", new { Name = "test", Values = new[] { 1, 2, 3 } });

        var json = memory.SerializeSnapshot();
        var restored = SharedMemory.FromSnapshot(json);

        var element = restored.Get<System.Text.Json.JsonElement>("data");
        Assert.Equal("test", element.GetProperty("name").GetString());
    }

    [Fact]
    public void DeserializeSnapshot_ShouldMergeIntoExisting()
    {
        var memory = new SharedMemory();
        memory.Set("existing", "keep");

        var json = """{"new_key":"new_value"}""";
        memory.DeserializeSnapshot(json);

        Assert.Equal("keep", memory.Get<string>("existing"));
        Assert.Equal("new_value", memory.Get<System.Text.Json.JsonElement>("new_key").GetString());
    }
}
