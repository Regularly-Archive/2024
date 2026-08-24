using InsightaAI.Agents.Subagents.Definitions;

namespace InsightaAI.Agents.Subagents.Catalog;

/// <summary>
/// Mutable storage for named subagent definitions. Implementations may use local files,
/// a database, or a remote service; callers do not depend on the backing store.
/// </summary>
public interface ISubagentDefinitionStore : ISubagentCatalog
{
    Task CreateAsync(SubagentDefinition definition, CancellationToken cancellationToken = default);
    Task UpdateAsync(SubagentDefinition definition, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
