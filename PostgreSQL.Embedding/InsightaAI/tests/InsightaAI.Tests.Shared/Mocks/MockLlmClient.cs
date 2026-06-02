using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Tests.Shared;

/// <summary>
/// Mock LLM Client for testing
/// </summary>
public class MockLlmClient : ILlmClient
{
    private readonly string? _response;
    private readonly ToolCallBlock[]? _firstResponseToolCalls;
    private readonly string? _secondResponse;
    private readonly ToolCallBlock[]? _alwaysToolCalls;
    private int _callCount = 0;

    public string ProviderName => "mock";
    public bool SupportsReasoning => false;

    public MockLlmClient(
        string? response = null,
        ToolCallBlock[]? firstResponseToolCalls = null,
        string? secondResponse = null,
        ToolCallBlock[]? alwaysToolCalls = null)
    {
        _response = response ?? "Default response";
        _firstResponseToolCalls = firstResponseToolCalls;
        _secondResponse = secondResponse;
        _alwaysToolCalls = alwaysToolCalls;
    }

    public LlmStream Stream(LlmRequest request)
    {
        _callCount++;

        ToolCallBlock[]? toolCalls = null;
        string text;

        if (_alwaysToolCalls != null)
        {
            toolCalls = _alwaysToolCalls;
            text = "";
        }
        else if (_callCount == 1 && _firstResponseToolCalls != null)
        {
            toolCalls = _firstResponseToolCalls;
            text = "";
        }
        else
        {
            text = _callCount == 1 ? _response : (_secondResponse ?? _response);
        }

        return new MockLlmStream(text, toolCalls);
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        _callCount++;
        var text = _callCount == 1 ? _response : (_secondResponse ?? _response);

        var content = new List<ContentBlock> { new TextBlock { Text = text } };

        if (_firstResponseToolCalls != null && _callCount == 1)
        {
            content.AddRange(_firstResponseToolCalls);
        }

        return Task.FromResult(new LlmResponse
        {
            Model = request.Model,
            Content = content.ToArray(),
            FinishReason = _firstResponseToolCalls != null && _callCount == 1
                ? DoneReason.ToolCalls
                : DoneReason.Complete,
            Usage = new TokenUsage { InputTokens = 10, OutputTokens = 20 }
        });
    }
}

/// <summary>
/// Mock LLM Stream for testing
/// </summary>
public class MockLlmStream : LlmStream
{
    private readonly string _text;
    private readonly ToolCallBlock[]? _toolCalls;

    public bool IsCompleted { get; private set; }
    public bool IsAborted { get; private set; }

    public MockLlmStream(string text, ToolCallBlock[]? toolCalls = null)
    {
        _text = text;
        _toolCalls = toolCalls;
    }

    public void Abort()
    {
        IsAborted = true;
    }

    public async IAsyncEnumerable<StreamEvent> GetStreamEventsAsync()
    {
        yield return new StreamStartEvent { Model = "test-model", Provider = "mock" };

        if (!string.IsNullOrEmpty(_text))
        {
            yield return new TextDeltaEvent { Delta = _text, ContentIndex = 0 };
        }

        if (_toolCalls != null)
        {
            foreach (var toolCall in _toolCalls)
            {
                yield return new ToolCallStartEvent
                {
                    ContentIndex = 0,
                    ToolName = toolCall.Name,
                    ToolCallId = toolCall.Id
                };
                yield return new ToolCallDeltaEvent
                {
                    ContentIndex = 0,
                    ArgumentsDelta = toolCall.Arguments.GetRawText()
                };
            }
        }

        yield return new DoneEvent
        {
            Reason = _toolCalls?.Length > 0 ? DoneReason.ToolCalls : DoneReason.Complete
        };

        IsCompleted = true;
    }

    public IAsyncEnumerator<StreamEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return GetStreamEventsAsync().GetAsyncEnumerator(cancellationToken);
    }

    public Task<LlmResponse> GetResponseAsync(CancellationToken cancellationToken = default)
    {
        var content = new List<ContentBlock>();

        if (!string.IsNullOrEmpty(_text))
        {
            content.Add(new TextBlock { Text = _text });
        }

        if (_toolCalls != null)
        {
            content.AddRange(_toolCalls);
        }

        return Task.FromResult(new LlmResponse
        {
            Model = "test-model",
            Content = content.ToArray(),
            FinishReason = _toolCalls?.Length > 0 ? DoneReason.ToolCalls : DoneReason.Complete,
            Usage = new TokenUsage { InputTokens = 10, OutputTokens = 20 }
        });
    }
}
