using InsightaAI.Agents.Subagents.Definitions;

namespace InsightaAI.Agents.Subagents.Catalog;

/// <summary>Retrieves named definitions. It does not prescribe filesystem, database, or API storage.</summary>
public interface ISubagentCatalog
{
    ValueTask<SubagentDefinition?> FindAsync(string id, CancellationToken cancellationToken = default);
    IAsyncEnumerable<SubagentDefinition> ListAsync(CancellationToken cancellationToken = default);
}
