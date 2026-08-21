using InsightaAI.Agents.Subagents.Definitions;
using InsightaAI.Agents.Subagents.Invocation;

namespace InsightaAI.Agents.Subagents.Tests.Invocation;

public class SubagentDispatcherTests
{
    [Fact]
    public async Task InvokeAsync_OneMatchingAdapter_DelegatesRequest()
    {
        var adapter = new RecordingAdapter();
        var dispatcher = new SubagentDispatcher([adapter]);
        var request = CreateRequest();

        var result = await dispatcher.InvokeAsync(request);

        Assert.Same(request, adapter.Request);
        Assert.Equal(SubagentInvocationStatus.Completed, result.Status);
    }

    [Fact]
    public async Task InvokeAsync_NoMatchingAdapter_ThrowsClearError()
    {
        var dispatcher = new SubagentDispatcher([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.InvokeAsync(CreateRequest()));

        Assert.Contains("No subagent adapter", exception.Message);
    }

    [Fact]
    public async Task InvokeAsync_MultipleMatchingAdapters_RejectsAmbiguity()
    {
        var dispatcher = new SubagentDispatcher([new RecordingAdapter(), new RecordingAdapter()]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.InvokeAsync(CreateRequest()));

        Assert.Contains("Multiple subagent adapters", exception.Message);
    }

    private static SubagentInvocationRequest CreateRequest() => new()
    {
        Definition = new InsightaSubagentDefinition { Id = "explorer", Name = "Explorer" },
        Input = "Inspect the repository"
    };

    private sealed class RecordingAdapter : ISubagentAdapter
    {
        public SubagentInvocationRequest? Request { get; private set; }

        public bool CanInvoke(SubagentDefinition definition) => definition is InsightaSubagentDefinition;

        public Task<SubagentInvocationResult> InvokeAsync(
            SubagentInvocationRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new SubagentInvocationResult
            {
                InvocationId = "test",
                Status = SubagentInvocationStatus.Completed
            });
        }
    }
}
