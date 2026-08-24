using System.Text.Json;
using InsightaAI.Agents.Subagents.Catalog;
using InsightaAI.Agents.Subagents.Definitions;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>Loads named Insighta subagents from <c>~/.insighta/subagents/{id}/subagent.json</c>.</summary>
public sealed class LocalSubagentDefinitionStore : ISubagentDefinitionStore
{
    private const string DescriptorFileName = "subagent.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _rootDirectory;

    /// <summary>
    /// Creates a catalog rooted at the global Insighta subagent directory. Tests may supply an
    /// isolated root directory without changing the production lookup scope.
    /// </summary>
    public LocalSubagentDefinitionStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".insighta", "subagents")
            : Path.GetFullPath(rootDirectory);
    }

    public async ValueTask<SubagentDefinition?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        var path = Path.Combine(_rootDirectory, id, DescriptorFileName);
        if (!File.Exists(path))
            return null;

        try
        {
            return await LoadAsync(path, id, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Subagent descriptor '{path}' contains invalid JSON.", exception);
        }
    }

    public async Task CreateAsync(SubagentDefinition definition, CancellationToken cancellationToken = default)
    {
        var insightaDefinition = ValidateDefinition(definition);
        var descriptorPath = GetDescriptorPath(insightaDefinition.Id);
        if (File.Exists(descriptorPath))
            throw new InvalidOperationException($"Subagent '{insightaDefinition.Id}' already exists.");

        await WriteAsync(insightaDefinition, descriptorPath, overwrite: false, cancellationToken);
    }

    public async Task UpdateAsync(SubagentDefinition definition, CancellationToken cancellationToken = default)
    {
        var insightaDefinition = ValidateDefinition(definition);
        var descriptorPath = GetDescriptorPath(insightaDefinition.Id);
        if (!File.Exists(descriptorPath))
            throw new InvalidOperationException($"Subagent '{insightaDefinition.Id}' was not found.");

        await WriteAsync(insightaDefinition, descriptorPath, overwrite: true, cancellationToken);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateId(id);
        var directory = Path.Combine(_rootDirectory, id);
        if (!Directory.Exists(directory))
            return Task.FromResult(false);

        Directory.Delete(directory, recursive: true);
        return Task.FromResult(true);
    }

    public async IAsyncEnumerable<SubagentDefinition> ListAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string[] directories;
        try
        {
            directories = Directory.GetDirectories(_rootDirectory);
        }
        catch (DirectoryNotFoundException)
        {
            yield break;
        }

        foreach (var directory in directories.OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(directory);
            var path = Path.Combine(directory, DescriptorFileName);
            if (File.Exists(path))
            {
                var definition = await TryLoadForListAsync(path, id, cancellationToken);
                if (definition != null)
                    yield return definition;
            }
        }
    }

    private static async Task<InsightaSubagentDefinition?> TryLoadForListAsync(
        string path,
        string directoryId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await LoadAsync(path, directoryId, cancellationToken);
        }
        catch (JsonException)
        {
            // A malformed descriptor must not hide the other global subagents.
            return null;
        }
        catch (InvalidOperationException)
        {
            // Ignore invalid descriptor metadata while enumerating the catalog.
            return null;
        }
        catch (IOException)
        {
            // The descriptor may have been removed or replaced during enumeration.
            return null;
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

    private static InsightaSubagentDefinition ValidateDefinition(SubagentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition is not InsightaSubagentDefinition insightaDefinition)
            throw new InvalidOperationException("The local subagent store only supports Insighta subagent definitions.");

        ValidateId(insightaDefinition.Id);
        if (string.IsNullOrWhiteSpace(insightaDefinition.Name))
            throw new InvalidOperationException("Subagent definitions require a non-empty name.");
        return insightaDefinition;
    }

    private async Task WriteAsync(
        InsightaSubagentDefinition definition,
        string descriptorPath,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(descriptorPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{DescriptorFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(definition, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, descriptorPath, overwrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private string GetDescriptorPath(string id)
    {
        ValidateId(id);
        return Path.Combine(_rootDirectory, id, DescriptorFileName);
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
