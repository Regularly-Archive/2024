using System.Text.RegularExpressions;
using InsightaAI.Agent.Prompts;
using InsightaAI.LLM;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Context.Summary;

public sealed class SummaryService : ISummaryService
{
    private readonly SummaryOptions _options;

    public SummaryService(SummaryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
        ArgumentNullException.ThrowIfNull(options.ClientFactory);

        if (options.MaxTokens < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxTokens must be >= 1");
        if (options.TargetTokens < 1 || options.TargetTokens >= options.MaxTokens)
            throw new ArgumentOutOfRangeException(nameof(options), "TargetTokens must be between 1 and MaxTokens - 1");
        if (options.MaxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxAttempts must be >= 1");
        if (options.TitleMaxTokens < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "TitleMaxTokens must be >= 1");
        if (options.TitleMaxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "TitleMaxAttempts must be >= 1");
        if (options.TitleMaxCharacters < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "TitleMaxCharacters must be >= 1");
        if (options.TitleFallbackMaxCharacters < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "TitleFallbackMaxCharacters must be >= 1");

        _options = options;
    }

    public Task<SummaryResult> SummarizeAsync(
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default) =>
        GenerateAsync(SummaryMode.Full, messages, null, cancellationToken);

    public Task<SummaryResult> UpdateAsync(
        string previousSummary,
        IReadOnlyList<Message> newMessages,
        CancellationToken cancellationToken = default) =>
        GenerateAsync(SummaryMode.Incremental, newMessages, previousSummary, cancellationToken);

    public async Task<string?> GenerateTitleAsync(
        string initialUserMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(initialUserMessage))
            return null;

        for (var attempt = 1; attempt <= _options.TitleMaxAttempts; attempt++)
        {
            try
            {
                var client = _options.ClientFactory(_options.Model);
                var modelName = ModelRef.TryParse(_options.Model, out var modelRef)
                    ? modelRef.ModelId
                    : _options.Model;
                var prompt = await PromptTemplate.RenderAsync("session-title", new Dictionary<string, string>
                {
                    ["INITIAL_USER_MESSAGE"] = initialUserMessage.Trim(),
                    ["MAX_CHARACTERS"] = _options.TitleMaxCharacters.ToString()
                });
                var maxTokens = checked(_options.TitleMaxTokens * attempt);
                var response = await client.CompleteAsync(new LlmRequest
                {
                    Model = modelName,
                    Messages = [Message.FromUser(prompt)],
                    Tools = [],
                    ToolChoice = ToolChoiceMode.None,
                    MaxTokens = maxTokens,
                    Temperature = 0.3,
                    Reasoning = new ReasoningConfig { Enabled = false, Effort = ReasoningEffort.Low }
                }, cancellationToken);

                var title = NormalizeTitle(response.GetTextContent());
                if (title != null)
                    return title;

                System.Diagnostics.Debug.WriteLine(
                    $"[SummaryService] Title generation returned no text. " +
                    $"FinishReason={response.FinishReason}, Attempt={attempt}, MaxTokens={maxTokens}");

                if (response.FinishReason != DoneReason.MaxTokens)
                    break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[SummaryService] Title generation failed on attempt {attempt}: {ex.Message}");
                break;
            }
        }

        var fallbackTitle = CreateFallbackTitle(initialUserMessage);
        if (fallbackTitle != null)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[SummaryService] Using fallback session title: {fallbackTitle}");
        }

        return fallbackTitle;
    }

    private async Task<SummaryResult> GenerateAsync(
        SummaryMode mode,
        IReadOnlyList<Message> messages,
        string? previousSummary,
        CancellationToken cancellationToken)
    {
        var client = _options.ClientFactory(_options.Model);
        var modelName = ModelRef.TryParse(_options.Model, out var modelRef)
            ? modelRef.ModelId
            : _options.Model;
        var lastFinishReason = DoneReason.Complete;
        string? lastError = null;

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            try
            {
                var request = await BuildRequestAsync(
                    mode, modelName, messages, previousSummary, aggressive: attempt > 1);
                var response = await client.CompleteAsync(request, cancellationToken);
                lastFinishReason = response.FinishReason;

                if (response.FinishReason == DoneReason.MaxTokens)
                {
                    lastError = "Summary output exceeded the maximum token limit.";
                    continue;
                }

                var summary = ExtractCompleteSummary(response.GetTextContent());
                if (summary != null)
                {
                    return new SummaryResult
                    {
                        Success = true,
                        Summary = summary,
                        Mode = mode,
                        FinishReason = response.FinishReason,
                        Attempts = attempt
                    };
                }

                lastError = "Summary response was empty or did not contain a complete <summary> element.";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        return new SummaryResult
        {
            Success = false,
            Mode = mode,
            FinishReason = lastFinishReason,
            Attempts = _options.MaxAttempts,
            Error = lastError
        };
    }

    private async Task<LlmRequest> BuildRequestAsync(
        SummaryMode mode,
        string modelName,
        IReadOnlyList<Message> messages,
        string? previousSummary,
        bool aggressive)
    {
        var templateName = mode == SummaryMode.Full ? "full-summary" : "incremental-summary";
        var outputTemplate = await PromptTemplate.LoadAsync("summary-output-template");
        var prompt = await PromptTemplate.RenderAsync(templateName, new Dictionary<string, string>
        {
            ["PREVIOUS_SUMMARY"] = string.IsNullOrWhiteSpace(previousSummary) ? "(none)" : previousSummary,
            ["TARGET_TOKENS"] = _options.TargetTokens.ToString(),
            ["SUMMARY_OUTPUT_TEMPLATE"] = outputTemplate,
            ["COMPRESSION_INSTRUCTION"] = aggressive
                ? "A previous attempt was incomplete. Regenerate the entire summary from scratch, remove lower-priority details, and compress more aggressively."
                : "Keep the summary concise and within the requested budget."
        });

        var requestMessages = new List<Message>
        {
            Message.FromSystem("You are a conversation summarizer. Return a complete, concise state summary without calling tools.")
        };
        requestMessages.AddRange(messages.Where(x => x.Role != MessageRole.System));
        requestMessages.Add(Message.FromUser(prompt));

        return new LlmRequest
        {
            Model = modelName,
            Messages = requestMessages.ToArray(),
            Tools = [],
            ToolChoice = ToolChoiceMode.None,
            MaxTokens = _options.MaxTokens,
            Temperature = _options.Temperature,
            Reasoning = new ReasoningConfig { Enabled = false, Effort = ReasoningEffort.Low }
        };
    }

    private static string? ExtractCompleteSummary(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        var match = Regex.Match(
            response,
            @"<summary>\s*(.*?)\s*</summary>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)
            ? match.Groups[1].Value.Trim()
            : null;
    }

    private string? NormalizeTitle(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        var title = response
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?
            .Trim()
            .Trim('"', '\'', '`', '#', '*', '-', ' ', '。', '.', '！', '!', '？', '?', '：', ':');

        if (string.IsNullOrWhiteSpace(title))
            return null;

        return title.Length <= _options.TitleMaxCharacters
            ? title
            : title[.._options.TitleMaxCharacters].TrimEnd();
    }

    private string? CreateFallbackTitle(string initialUserMessage)
    {
        var firstLine = initialUserMessage
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return null;

        var title = Regex.Replace(firstLine, @"^\s*(?:#{1,6}|[-*+>])\s*", "");
        title = Regex.Replace(title, @"\s+", " ")
            .Trim()
            .Trim('"', '\'', '`', '#', '*', '-', ' ', '。', '.', '！', '!', '？', '?', '：', ':');
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var runes = title.EnumerateRunes().ToArray();
        if (runes.Length <= _options.TitleFallbackMaxCharacters)
            return title;

        if (_options.TitleFallbackMaxCharacters == 1)
            return "…";

        return string.Concat(runes
            .Take(_options.TitleFallbackMaxCharacters - 1)
            .Select(rune => rune.ToString())) + "…";
    }
}
