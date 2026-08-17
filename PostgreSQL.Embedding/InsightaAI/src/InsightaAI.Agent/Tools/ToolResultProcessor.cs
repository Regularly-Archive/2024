using System.Text;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Security;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools;

public sealed record ProcessedToolResult
{
    public required ToolResult Result { get; init; }
    public required ToolResultState State { get; init; }
    public int CurrentLength { get; init; }
}

/// <summary>统一处理工具结果的落盘和上下文投影。</summary>
public sealed class ToolResultProcessor
{
    public const int DefaultPersistenceThresholdBytes = 30 * 1024;

    private readonly ToolRegistry _toolRegistry;
    private readonly IToolResultArtifactStore _artifactStore;
    private readonly ISecretRedactor _secretRedactor;

    public ToolResultProcessor(
        ToolRegistry toolRegistry,
        IToolResultArtifactStore artifactStore,
        ISecretRedactor? secretRedactor = null)
    {
        _toolRegistry = toolRegistry;
        _artifactStore = artifactStore;
        _secretRedactor = secretRedactor ?? SecretRedactionPipeline.CreateDefault();
    }

    public async Task<ProcessedToolResult> ProcessAsync(string sessionId, ToolCallBlock toolCall,
        ToolResult result, bool enabled, CancellationToken cancellationToken = default)
    {
        var redactedResult = RedactTextContent(result, ToolRedactionContextFactory.Create(toolCall));
        var text = string.Join("\n", redactedResult.Content.OfType<TextBlock>().Select(x => x.Text));
        var lineCount = new Lazy<int>(() => text.Length == 0 ? 0 : text.Count(c => c == '\n') + 1);
        var projector = _toolRegistry.GetExecutor(toolCall.Name) as IToolResultProjector;
        var effectiveProjector = projector ?? DefaultToolResultProjector.Instance;
        var policy = projector?.RetentionPolicy ?? DefaultToolResultProjector.DefaultPolicy;

        ToolResultArtifactInfo? artifact = null;
        var shouldPersist = enabled && Encoding.UTF8.GetByteCount(text) > DefaultPersistenceThresholdBytes;
        if (shouldPersist)
            artifact = await _artifactStore.SaveAsync(sessionId, toolCall.Name, toolCall.Id, text, cancellationToken);

        var context = new ToolResultProjectionContext
        {
            ToolName = toolCall.Name,
            ToolCallId = toolCall.Id,
            OriginalLength = text.Length,
            OriginalLineCount = lineCount,
            Artifact = artifact
        };

        var projection = shouldPersist
            ? effectiveProjector.CreatePreview(redactedResult, context)
            : new ToolResultProjection { Content = redactedResult.Content, Level = ToolResultRetentionLevel.Full };

        var processed = redactedResult with { Content = projection.Content };
        return new ProcessedToolResult
        {
            Result = processed,
            CurrentLength = string.Join("\n", projection.Content.OfType<TextBlock>().Select(x => x.Text)).Length,
            State = new ToolResultState
            {
                RetentionLevel = projection.Level,
                Artifact = artifact,
                OriginalLength = text.Length,
                CanReplay = policy.CanReplay,
                HasSideEffects = policy.HasSideEffects,
                MinimumLevel = policy.MinimumLevel
            }
        };
    }

    private ToolResult RedactTextContent(ToolResult result, RedactionContext context)
    {
        var content = result.Content.Select(block => block is TextBlock textBlock
            ? (ContentBlock)(textBlock with { Text = _secretRedactor.Redact(textBlock.Text, context).Content })
            : block).ToArray();
        return result with { Content = content };
    }
}

internal sealed class DefaultToolResultProjector : IToolResultProjector
{
    public static readonly DefaultToolResultProjector Instance = new();
    public static readonly ToolResultRetentionPolicy DefaultPolicy = new();
    public ToolResultRetentionPolicy RetentionPolicy => DefaultPolicy;

    public ToolResultProjection CreatePreview(ToolResult result, ToolResultProjectionContext context)
    {
        var text = string.Join("\n", result.Content.OfType<TextBlock>().Select(x => x.Text));
        var lines = text.Split('\n');
        var preview = string.Join("\n", lines.Take(100));
        var omitted = Math.Max(0, lines.Length - 200);
        if (omitted > 0)
            preview += $"\n\n[... omitted {omitted} lines ...]\n\n" + string.Join("\n", lines.TakeLast(100));
        if (context.Artifact != null)
            preview += $"\n\n[Full output saved as artifact {context.Artifact.Id}: {context.Artifact.Path}]";

        return new ToolResultProjection
        {
            Content = [new TextBlock { Text = preview }],
            Level = ToolResultRetentionLevel.Preview
        };
    }

    public ToolResultProjection CreatePlaceholder(ToolResultProjectionContext context) => new()
    {
        Content = [new TextBlock { Text = CreatePlaceholderText(context) }],
        Level = ToolResultRetentionLevel.Placeholder
    };

    internal static string CreatePlaceholderText(ToolResultProjectionContext context) => context.Artifact != null
        ? $"[Previous {context.ToolName} result omitted to reduce context. Full output is available as artifact {context.Artifact.Id} ({context.Artifact.Path}).]"
        : $"[Previous {context.ToolName} result omitted to reduce context. Re-run the tool if needed.]";
}
