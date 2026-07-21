using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Context.Summary;

public interface ISummaryService
{
    Task<SummaryResult> SummarizeAsync(
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default);

    Task<SummaryResult> UpdateAsync(
        string previousSummary,
        IReadOnlyList<Message> newMessages,
        CancellationToken cancellationToken = default);

    Task<string?> GenerateTitleAsync(
        string initialUserMessage,
        CancellationToken cancellationToken = default);
}
