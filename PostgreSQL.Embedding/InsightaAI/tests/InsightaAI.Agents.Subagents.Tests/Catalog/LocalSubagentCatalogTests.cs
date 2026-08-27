using System.Text.Json;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agents.Subagents.Definitions;

namespace InsightaAI.Agents.Subagents.Tests.Catalog;

public sealed class LocalSubagentDefinitionStoreTests : IDisposable
{
    private readonly string _rootDirectory = Path.Combine(Path.GetTempPath(), "insighta-subagent-catalog-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FindAsync_ValidDescriptor_ReturnsDefinition()
    {
        await WriteDescriptorAsync("explorer", new InsightaSubagentDefinition
        {
            Id = "explorer",
            Name = "Explorer",
            ToolNames = ["read_file"]
        });
        var catalog = new LocalSubagentDefinitionStore(_rootDirectory);

        var definition = await catalog.FindAsync("explorer");

        var insighta = Assert.IsType<InsightaSubagentDefinition>(definition);
        Assert.Equal("Explorer", insighta.Name);
        Assert.Equal(["read_file"], insighta.ToolNames);
        Assert.False(insighta.Capabilities.EnableMemory);
    }

    [Fact]
    public async Task ListAsync_ReturnsDescriptorsInIdOrder()
    {
        await WriteDescriptorAsync("reviewer", new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" });
        await WriteDescriptorAsync("explorer", new InsightaSubagentDefinition { Id = "explorer", Name = "Explorer" });
        var catalog = new LocalSubagentDefinitionStore(_rootDirectory);

        var ids = new List<string>();
        await foreach (var definition in catalog.ListAsync())
            ids.Add(definition.Id);

        Assert.Equal(["explorer", "reviewer"], ids);
    }

    [Fact]
    public async Task ListAsync_TemplateDescriptorWithoutOptionalFields_ReturnsDefinition()
    {
        await WriteRawDescriptorAsync("reviewer", """
        {
          "id": "reviewer",
          "name": "Reviewer",
          "description": "Reviews code.",
          "instructions": "Review the supplied scope.",
          "maxToolRounds": 8,
          "toolNames": ["read_file", "grep", "glob"],
          "includeProjectInstructions": true
        }
        """);
        var catalog = new LocalSubagentDefinitionStore(_rootDirectory);

        var definitions = new List<SubagentDefinition>();
        await foreach (var definition in catalog.ListAsync())
            definitions.Add(definition);

        var reviewer = Assert.IsType<InsightaSubagentDefinition>(Assert.Single(definitions));
        Assert.Equal("reviewer", reviewer.Id);
        Assert.Equal(["read_file", "grep", "glob"], reviewer.ToolNames);
    }

    [Fact]
    public async Task FindAsync_MismatchedDirectoryId_ThrowsClearError()
    {
        await WriteDescriptorAsync("explorer", new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" });
        var catalog = new LocalSubagentDefinitionStore(_rootDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await catalog.FindAsync("explorer"));

        Assert.Contains("must match its directory", exception.Message);
    }

    [Fact]
    public async Task FindAsync_PathTraversalId_RejectsInput()
    {
        var catalog = new LocalSubagentDefinitionStore(_rootDirectory);

        await Assert.ThrowsAsync<ArgumentException>(async () => await catalog.FindAsync(".."));
    }

    [Fact]
    public async Task FindAsync_MalformedDescriptor_ThrowsClearError()
    {
        await WriteRawDescriptorAsync("reviewer", "{ not valid json }");
        var catalog = new LocalSubagentDefinitionStore(_rootDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await catalog.FindAsync("reviewer"));

        Assert.Contains("invalid JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListAsync_MalformedDescriptor_DoesNotHideValidDescriptors()
    {
        await WriteRawDescriptorAsync("broken", "{ not valid json }");
        await WriteDescriptorAsync("reviewer", new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" });
        var catalog = new LocalSubagentDefinitionStore(_rootDirectory);

        var ids = new List<string>();
        await foreach (var definition in catalog.ListAsync())
            ids.Add(definition.Id);

        Assert.Equal(["reviewer"], ids);
    }

    [Fact]
    public async Task ListAsync_MissingRoot_ReturnsNoDescriptors()
    {
        var catalog = new LocalSubagentDefinitionStore(_rootDirectory);
        var ids = new List<string>();

        await foreach (var definition in catalog.ListAsync())
            ids.Add(definition.Id);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task CreateUpdateDeleteAsync_ManagesDefinitionsThroughTheStoreContract()
    {
        var store = new LocalSubagentDefinitionStore(_rootDirectory);
        var created = new InsightaSubagentDefinition
        {
            Id = "reviewer",
            Name = "Reviewer",
            Description = "Initial description"
        };

        await store.CreateAsync(created);
        var loaded = Assert.IsType<InsightaSubagentDefinition>(await store.FindAsync("reviewer"));
        Assert.Equal("Initial description", loaded.Description);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync(created));

        await store.UpdateAsync(created with { Description = "Updated description" });
        loaded = Assert.IsType<InsightaSubagentDefinition>(await store.FindAsync("reviewer"));
        Assert.Equal("Updated description", loaded.Description);

        Assert.True(await store.DeleteAsync("reviewer"));
        Assert.False(await store.DeleteAsync("reviewer"));
        Assert.Null(await store.FindAsync("reviewer"));
    }

    private async Task WriteDescriptorAsync(string id, InsightaSubagentDefinition definition)
    {
        var directory = Path.Combine(_rootDirectory, id);
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, "subagent.json"), json);
    }

    private async Task WriteRawDescriptorAsync(string id, string json)
    {
        var directory = Path.Combine(_rootDirectory, id);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "subagent.json"), json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, recursive: true);
    }
}
