using System.Text.Json;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agents.Subagents.Definitions;

namespace InsightaAI.Agents.Subagents.Tests.Catalog;

public sealed class LocalSubagentCatalogTests : IDisposable
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
        var catalog = new LocalSubagentCatalog(_rootDirectory);

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
        var catalog = new LocalSubagentCatalog(_rootDirectory);

        var ids = new List<string>();
        await foreach (var definition in catalog.ListAsync())
            ids.Add(definition.Id);

        Assert.Equal(["explorer", "reviewer"], ids);
    }

    [Fact]
    public async Task FindAsync_MismatchedDirectoryId_ThrowsClearError()
    {
        await WriteDescriptorAsync("explorer", new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" });
        var catalog = new LocalSubagentCatalog(_rootDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await catalog.FindAsync("explorer"));

        Assert.Contains("must match its directory", exception.Message);
    }

    [Fact]
    public async Task FindAsync_PathTraversalId_RejectsInput()
    {
        var catalog = new LocalSubagentCatalog(_rootDirectory);

        await Assert.ThrowsAsync<ArgumentException>(async () => await catalog.FindAsync(".."));
    }

    [Fact]
    public async Task FindAsync_MalformedDescriptor_ThrowsClearError()
    {
        await WriteRawDescriptorAsync("reviewer", "{ not valid json }");
        var catalog = new LocalSubagentCatalog(_rootDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await catalog.FindAsync("reviewer"));

        Assert.Contains("invalid JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListAsync_MalformedDescriptor_DoesNotHideValidDescriptors()
    {
        await WriteRawDescriptorAsync("broken", "{ not valid json }");
        await WriteDescriptorAsync("reviewer", new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" });
        var catalog = new LocalSubagentCatalog(_rootDirectory);

        var ids = new List<string>();
        await foreach (var definition in catalog.ListAsync())
            ids.Add(definition.Id);

        Assert.Equal(["reviewer"], ids);
    }

    [Fact]
    public async Task ListAsync_MissingRoot_ReturnsNoDescriptors()
    {
        var catalog = new LocalSubagentCatalog(_rootDirectory);
        var ids = new List<string>();

        await foreach (var definition in catalog.ListAsync())
            ids.Add(definition.Id);

        Assert.Empty(ids);
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
