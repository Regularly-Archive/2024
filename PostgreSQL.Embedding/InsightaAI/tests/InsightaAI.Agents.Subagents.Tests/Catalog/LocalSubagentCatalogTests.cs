using System.Text.Json;
using InsightaAI.Agent.Cli.Services;
using InsightaAI.Agents.Subagents.Definitions;

namespace InsightaAI.Agents.Subagents.Tests.Catalog;

public sealed class LocalSubagentCatalogTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), "insighta-subagent-catalog-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FindAsync_ValidDescriptor_ReturnsDefinition()
    {
        await WriteDescriptorAsync("explorer", new InsightaSubagentDefinition
        {
            Id = "explorer",
            Name = "Explorer",
            ToolNames = ["read_file"]
        });
        var catalog = new LocalSubagentCatalog(_workingDirectory);

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
        var catalog = new LocalSubagentCatalog(_workingDirectory);

        var ids = new List<string>();
        await foreach (var definition in catalog.ListAsync())
            ids.Add(definition.Id);

        Assert.Equal(["explorer", "reviewer"], ids);
    }

    [Fact]
    public async Task FindAsync_MismatchedDirectoryId_ThrowsClearError()
    {
        await WriteDescriptorAsync("explorer", new InsightaSubagentDefinition { Id = "reviewer", Name = "Reviewer" });
        var catalog = new LocalSubagentCatalog(_workingDirectory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await catalog.FindAsync("explorer"));

        Assert.Contains("must match its directory", exception.Message);
    }

    [Fact]
    public async Task FindAsync_PathTraversalId_RejectsInput()
    {
        var catalog = new LocalSubagentCatalog(_workingDirectory);

        await Assert.ThrowsAsync<ArgumentException>(async () => await catalog.FindAsync(".."));
    }

    private async Task WriteDescriptorAsync(string id, InsightaSubagentDefinition definition)
    {
        var directory = Path.Combine(_workingDirectory, ".insighta", "subagents", id);
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, "subagent.json"), json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
            Directory.Delete(_workingDirectory, recursive: true);
    }
}
