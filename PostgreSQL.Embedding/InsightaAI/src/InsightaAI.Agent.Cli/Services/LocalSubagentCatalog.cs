using System.Text.Json;
using InsightaAI.Agents.Subagents.Catalog;
using InsightaAI.Agents.Subagents.Definitions;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>Loads named Insighta subagents from <c>.insighta/subagents/{id}/subagent.json</c>.</summary>
public sealed class LocalSubagentCatalog : ISubagentCatalog
{
    private const string DescriptorFileName = "subagent.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _rootDirectory;

    public LocalSubagentCatalog(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        _rootDirectory = Path.Combine(Path.GetFullPath(workingDirectory), ".insighta", "subagents");
    }

    public async ValueTask<SubagentDefinition?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        var path = Path.Combine(_rootDirectory, id, DescriptorFileName);
        if (!File.Exists(path))
            return null;

        return await LoadAsync(path, id, cancellationToken);
    }

    public async IAsyncEnumerable<SubagentDefinition> ListAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootDirectory))
            yield break;

        foreach (var directory in Directory.EnumerateDirectories(_rootDirectory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(directory);
            var path = Path.Combine(directory, DescriptorFileName);
            if (File.Exists(path))
                yield return await LoadAsync(path, id, cancellationToken);
        }
    }

    private static async Task<InsightaSubagentDefinition> LoadAsync(
        string path,
        string directoryId,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var definition = JsonSerializer.Deserialize<InsightaSubagentDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Subagent descriptor '{path}' is empty or invalid.");

        ValidateId(definition.Id);
        if (!string.Equals(definition.Id, directoryId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Subagent descriptor '{path}' has id '{definition.Id}', which must match its directory '{directoryId}'.");
        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new InvalidOperationException($"Subagent descriptor '{path}' requires a non-empty name.");
        return definition;
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            id.Contains(Path.DirectorySeparatorChar) || id.Contains(Path.AltDirectorySeparatorChar) || id is "." or "..")
        {
            throw new ArgumentException("Subagent id must be a single valid directory name.", nameof(id));
        }
    }
}
