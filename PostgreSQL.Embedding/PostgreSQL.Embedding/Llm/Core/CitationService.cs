using LLama.Transformers;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Streaming;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.RAG;
using PostgreSQL.Embedding.Llm.Services;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Llm.Core;

/// <summary>
/// Service for managing citations and references in RAG answers.
/// </summary>
public class CitationService
{
    private readonly Regex _regexCitations = new Regex(@"\[\^(\d+)\]", RegexOptions.Compiled);
    private CallablePromptTemplate _promptTemplate;

    public CitationService(PromptTemplateService promptTemplateService)
    {
        _promptTemplate = promptTemplateService.LoadTemplate("CitationInjection.txt");
    }

    /// <summary>
    /// Extract citation positions from text with [^N] markers.
    /// Maps each citation number to its source and all positions where it appears.
    /// </summary>
    /// <param name="text">Text containing [^N] citation markers</param>
    /// <param name="citations">List of citation sources</param>
    /// <returns>List of CitationItem with position information</returns>
    public async Task<List<CitationItem>> ExtractCitations(string text, List<LlmCitationModel> citations, Kernel kernel)
    {
        var references = string.Join("\r\n", citations.Select(x => $"[^{x.Index}] {x.Text}"));

        _promptTemplate.AddVariable("answer", text);
        _promptTemplate.AddVariable("references", references);

        var functionResult = await _promptTemplate.InvokeAsync(kernel);
        var generateText = functionResult.GetValue<string>();


        var result = new List<CitationItem>();
        var citationPositions = new Dictionary<int, List<CitationPosition>>();

        // Find all citation markers and collect positions
        foreach (Match match in _regexCitations.Matches(generateText))
        {
            var number = int.Parse(match.Groups[1].Value);

            if (!citationPositions.ContainsKey(number))
            {
                citationPositions[number] = new List<CitationPosition>();
            }

            citationPositions[number].Add(new CitationPosition
            {
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length
            });
        }

        // Build CitationItems from positions and citations
        foreach (var kvp in citationPositions)
        {
            var citationNumber = kvp.Key;
            var positions = kvp.Value;
            var citation = citations.FirstOrDefault(x => x.Index == citationNumber);

            result.Add(new CitationItem
            {
                Id = citationNumber.ToString(),
                Positions = positions,
                Title = citation?.FileName,
                Url = citation?.Url,
                Text = citation?.Text,
                Relevance = citation?.Relevance ?? 0,
                SourceType = citation?.Type
            });
        }

        return result;
    }

    /// <summary>
    /// Reorder reference numbers in the answer.
    /// LLM may only reference a subset of sources, so we need to renumber from 1.
    ///
    /// Example:
    ///   Original citations: [1]ContentA, [2]ContentB, [3]ContentC, [4]ContentD, [5]ContentE
    ///   LLM generated: "xxx[^3]yyy[^1]zzz[^5]"
    ///   After reorder: "xxx[^1]yyy[^2]zzz[^3]"
    /// </summary>
    /// <param name="originCitations">Original citation list with index and content</param>
    /// <param name="generatedAnswer">LLM generated answer containing [^N] references</param>
    /// <returns>Reordered answer and cited sources</returns>
    public ReorderResult ReorderCitations(List<LlmCitationModel> originCitations, string generatedAnswer)
    {
        // Find all citation markers [^N]
        var matches = _regexCitations.Matches(generatedAnswer);

        // Collect referenced numbers in order of first appearance, deduplicated
        // e.g., if LLM references [^3], [^1], [^5], then referenceOrder = [3, 1, 5]
        var referenceOrder = new List<int>();
        foreach (Match match in matches)
        {
            var number = int.Parse(match.Groups[1].Value);
            if (!referenceOrder.Contains(number))
                referenceOrder.Add(number);
        }

        // Build mapping: original number -> new sequential number
        // {3→1, 1→2, 5→3}
        var referenceMapping = new Dictionary<int, int>();
        for (int i = 0; i < referenceOrder.Count; i++)
        {
            referenceMapping[referenceOrder[i]] = i + 1;
        }

        // Replace citation markers in the answer
        string reorderedAnswer = _regexCitations.Replace(generatedAnswer, match =>
        {
            var oldNumber = int.Parse(match.Groups[1].Value);
            var newNumber = referenceMapping[oldNumber];
            return $"[^{newNumber}]";
        });

        // Collect cited sources, ordered by new number
        var citationItems = new List<LlmCitationModel>();
        foreach (var oldNumber in referenceOrder)
        {
            var citation = originCitations.FirstOrDefault(x => x.Index == oldNumber);
            if (citation != null)
            {
                citationItems.Add(citation);
            }
        }

        return new ReorderResult()
        {
            FormattedAnswer = reorderedAnswer,
            CitationItems = citationItems
        };
    }

    /// <summary>
    /// Remove all citation markers from the text, returning plain text.
    /// </summary>
    /// <param name="text">Text containing [^N] markers</param>
    /// <returns>Plain text without citation markers</returns>
    public string RemoveCitations(string text)
    {
        return _regexCitations.Replace(text, "");
    }
}

/// <summary>
/// Result of reordering references.
/// </summary>
public class ReorderResult
{
    public string FormattedAnswer { get; set; } = "";
    public List<LlmCitationModel> CitationItems { get; set; } = new();
}
