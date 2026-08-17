namespace InsightaAI.Agent.Security;

/// <summary>Runs format-specific and generic redactors in a deterministic order.</summary>
public sealed class SecretRedactionPipeline : ISecretRedactor
{
    private readonly IReadOnlyList<ISecretRedactor> _redactors;

    public SecretRedactionPipeline(IEnumerable<ISecretRedactor> redactors)
    {
        ArgumentNullException.ThrowIfNull(redactors);
        _redactors = redactors.ToArray();
    }

    public static SecretRedactionPipeline CreateDefault() => new([
        new JsonSecretRedactor(),
        new XmlSecretRedactor(),
        new KeyValueSecretRedactor(),
        new ConnectionStringSecretRedactor(),
        new GenericSecretRedactor()
    ]);

    public RedactionResult Redact(string content, RedactionContext context)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(context);

        var currentContent = content;
        var redactionCount = 0;
        var findings = new List<RedactionFinding>();

        foreach (var redactor in _redactors)
        {
            var result = redactor.Redact(currentContent, context);
            currentContent = result.Content;
            redactionCount += result.RedactionCount;
            findings.AddRange(result.Findings);
        }

        return new RedactionResult
        {
            Content = currentContent,
            WasRedacted = redactionCount > 0,
            RedactionCount = redactionCount,
            Findings = findings
        };
    }
}
