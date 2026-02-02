using Microsoft.AspNetCore.Mvc;

namespace PostgreSQL.Embedding.Common.Streaming;

/// <summary>
/// IActionResult that streams IAsyncEnumerable as SSE events
/// Usage: return new SseResult(ChatStream(...));
/// </summary>
public class SseResult : IActionResult
{
    private readonly IAsyncEnumerable<ISseEvent> _events;
    private readonly string? _model;

    public SseResult(IAsyncEnumerable<ISseEvent> events, string? model = null)
    {
        _events = events;
        _model = model;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        // Configure SSE response
        context.HttpContext.Response.ConfigureSseResponse();

        try
        {
            await foreach (var @event in _events.WithCancellation(context.HttpContext.RequestAborted))
            {
                await context.HttpContext.Response.WriteSseEventAsync(@event, context.HttpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected, ignore
        }
        catch (Exception ex)
        {
            // Log error
            Console.WriteLine($"SSE stream error: {ex.Message}");
            throw;
        }
    }
}

/// <summary>
/// Extension method to create SSE result
/// </summary>
public static class SseResultExtensions
{
    /// <summary>
    /// Create a streaming SSE result from IAsyncEnumerable
    /// Usage: return events.SseStream();
    /// </summary>
    public static SseResult SseStream(this IAsyncEnumerable<ISseEvent> events, string? model = null)
    {
        return new SseResult(events, model);
    }
}
