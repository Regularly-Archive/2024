using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace InsightaAI.Agent.Security;

internal static class SecretRedactionRules
{
    public const string Replacement = "[REDACTED]";

    public static bool ContainsRedactionPlaceholder(string value) =>
        value.Contains(Replacement, StringComparison.Ordinal);

    public static bool IsSensitiveKey(string key)
    {
        var normalized = Regex.Replace(key, "[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();
        return normalized is "password" or "pwd" or "secret" or "secretkey" or "token" or "key" or "apikey" or
            "accesstoken" or "clientsecret" or "privatekey" or "connectionstring" ||
            normalized.EndsWith("password", StringComparison.Ordinal) ||
            normalized.EndsWith("secret", StringComparison.Ordinal) ||
            normalized.EndsWith("token", StringComparison.Ordinal) ||
            normalized.EndsWith("apikey", StringComparison.Ordinal);
    }

    public static bool IsConnectionStringKey(string key) =>
        Regex.Replace(key, "[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant()
            .Contains("connectionstring", StringComparison.Ordinal);

    public static RedactionResult NoChange(string content) => new() { Content = content };

    public static RedactionResult Changed(string content, string category, string? location, int count = 1) => new()
    {
        Content = content,
        WasRedacted = true,
        RedactionCount = count,
        Findings = [new RedactionFinding(category, location)]
    };
}

internal sealed class JsonSecretRedactor : ISecretRedactor
{
    public RedactionResult Redact(string content, RedactionContext context)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            return SecretRedactionRules.NoChange(content);
        }

        if (node == null)
            return SecretRedactionRules.NoChange(content);

        var findings = new List<RedactionFinding>();
        RedactNode(node, "", findings);
        return findings.Count == 0
            ? SecretRedactionRules.NoChange(content)
            : new RedactionResult
            {
                Content = node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                WasRedacted = true,
                RedactionCount = findings.Count,
                Findings = findings
            };
    }

    private static void RedactNode(JsonNode node, string path, List<RedactionFinding> findings)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                var propertyPath = string.IsNullOrEmpty(path) ? property.Key : $"{path}:{property.Key}";
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text) &&
                    SecretRedactionRules.IsSensitiveKey(property.Key))
                {
                    obj[property.Key] = SecretRedactionRules.IsConnectionStringKey(property.Key)
                        ? ConnectionStringSecretRedactor.RedactConnectionString(text, findings, propertyPath)
                        : SecretRedactionRules.Replacement;
                    if (!SecretRedactionRules.IsConnectionStringKey(property.Key))
                        findings.Add(new RedactionFinding("sensitive_key", propertyPath));
                    continue;
                }

                if (property.Value != null)
                    RedactNode(property.Value, propertyPath, findings);
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] != null)
                    RedactNode(array[index]!, $"{path}[{index}]", findings);
            }
        }
    }
}

internal sealed class XmlSecretRedactor : ISecretRedactor
{
    private static readonly Regex SensitiveElement = new(
        "(?<prefix><(?<key>[A-Za-z_][A-Za-z0-9_.:-]*)\\b[^>]*>)(?<value>[^<\\r\\n]*)(?<suffix></\\k<key>[ \\t]*>)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex KeyValueElement = new(
        "<(?<element>[A-Za-z_][A-Za-z0-9_.:-]*)(?=[^>]*\\b(?:key|name)[ \\t]*=[ \\t]*\\\"(?<key>[^\\\"]+)\\\")(?=[^>]*\\bvalue[ \\t]*=[ \\t]*\\\"(?<value>[^\\\"]*)\\\")[^>]*>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public RedactionResult Redact(string content, RedactionContext context)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return RedactFragments(content);
        }

        var findings = new List<RedactionFinding>();
        foreach (var element in document.Root?.DescendantsAndSelf() ?? [])
        {
            if (SecretRedactionRules.IsSensitiveKey(element.Name.LocalName))
            {
                element.Value = SecretRedactionRules.Replacement;
                findings.Add(new RedactionFinding("sensitive_key", element.Name.LocalName));
                continue;
            }

            var key = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("key", StringComparison.OrdinalIgnoreCase) ||
                attribute.Name.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))?.Value;
            var value = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.LocalName.Equals("value", StringComparison.OrdinalIgnoreCase));
            if (key != null && value != null && SecretRedactionRules.IsSensitiveKey(key))
            {
                value.Value = SecretRedactionRules.IsConnectionStringKey(key)
                    ? ConnectionStringSecretRedactor.RedactConnectionString(value.Value, findings, key)
                    : SecretRedactionRules.Replacement;
                if (!SecretRedactionRules.IsConnectionStringKey(key))
                    findings.Add(new RedactionFinding("sensitive_key", key));
            }
        }

        return findings.Count == 0
            ? SecretRedactionRules.NoChange(content)
            : new RedactionResult
            {
                Content = document.ToString(SaveOptions.DisableFormatting),
                WasRedacted = true,
                RedactionCount = findings.Count,
                Findings = findings
            };
    }

    private static RedactionResult RedactFragments(string content)
    {
        var findings = new List<RedactionFinding>();
        var redacted = SensitiveElement.Replace(content, match =>
        {
            var key = match.Groups["key"].Value;
            if (!SecretRedactionRules.IsSensitiveKey(key) || SecretRedactionRules.IsConnectionStringKey(key))
                return match.Value;

            findings.Add(new RedactionFinding("sensitive_key", key));
            return match.Groups["prefix"].Value + SecretRedactionRules.Replacement + match.Groups["suffix"].Value;
        });

        redacted = KeyValueElement.Replace(redacted, match =>
        {
            var key = match.Groups["key"].Value;
            if (!SecretRedactionRules.IsSensitiveKey(key) || SecretRedactionRules.IsConnectionStringKey(key))
                return match.Value;

            findings.Add(new RedactionFinding("sensitive_key", key));
            return Regex.Replace(match.Value, "(?<=\\bvalue[ \\t]*=[ \\t]*\\\")[^\\\"]*", SecretRedactionRules.Replacement,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        });

        return findings.Count == 0
            ? SecretRedactionRules.NoChange(content)
            : new RedactionResult
            {
                Content = redacted,
                WasRedacted = true,
                RedactionCount = findings.Count,
                Findings = findings
            };
    }
}

internal sealed class KeyValueSecretRedactor : ISecretRedactor
{
    private static readonly Regex SensitiveLine = new(
        "(?m)^(?<prefix>[ \\t]*(?:\\d+[ \\t]+)?(?:-[ \\t]*)?(?:\\\"(?<quotedKey>[A-Za-z_][A-Za-z0-9_.-]*)\\\"|(?<key>[A-Za-z_][A-Za-z0-9_.-]*))[ \\t]*[:=][ \\t]*)(?<value>[^\\r\\n]*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex QuotedJsonProperty = new(
        "\\\"(?<key>[A-Za-z_][A-Za-z0-9_.-]*)\\\"[ \\t]*:[ \\t]*\\\"(?<value>(?:\\\\.|[^\\\"\\\\])*)\\\"",
        RegexOptions.CultureInvariant);

    private static readonly Regex JsonStringValue = new(
        "^\\\"(?:\\\\.|[^\\\"\\\\])*\\\"(?<suffix>.*)$",
        RegexOptions.CultureInvariant);

    public RedactionResult Redact(string content, RedactionContext context)
    {
        var findings = new List<RedactionFinding>();
        var lineEnding = ResolveOutputLineEnding(DetectLineEndingStyle(content));
        var redacted = SensitiveLine.Replace(NormalizeLineEndings(content), match =>
        {
            var key = match.Groups["quotedKey"].Success
                ? match.Groups["quotedKey"].Value
                : match.Groups["key"].Value;
            if (!SecretRedactionRules.IsSensitiveKey(key))
                return match.Value;

            if (SecretRedactionRules.IsConnectionStringKey(key))
                return match.Groups["prefix"].Value + ConnectionStringSecretRedactor.RedactConnectionString(
                    match.Groups["value"].Value, findings, key);

            findings.Add(new RedactionFinding("sensitive_key", key));
            return match.Groups["prefix"].Value + CreateReplacementValue(
                match.Groups["value"].Value,
                match.Groups["quotedKey"].Success);
        });

        redacted = QuotedJsonProperty.Replace(redacted, match =>
        {
            var key = match.Groups["key"].Value;
            if (!SecretRedactionRules.IsSensitiveKey(key) || SecretRedactionRules.IsConnectionStringKey(key))
                return match.Value;

            findings.Add(new RedactionFinding("sensitive_key", key));
            return $"\"{key}\": \"{SecretRedactionRules.Replacement}\"";
        });

        redacted = ApplyLineEnding(redacted, lineEnding);

        return findings.Count == 0
            ? SecretRedactionRules.NoChange(content)
            : new RedactionResult
            {
                Content = redacted,
                WasRedacted = true,
                RedactionCount = findings.Count,
                Findings = findings
            };
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string CreateReplacementValue(string value, bool quotedJsonKey)
    {
        if (!quotedJsonKey)
            return SecretRedactionRules.Replacement;

        var match = JsonStringValue.Match(value);
        return match.Success
            ? $"\"{SecretRedactionRules.Replacement}\"{match.Groups["suffix"].Value}"
            : SecretRedactionRules.Replacement;
    }

    private static string ApplyLineEnding(string text, string lineEnding) => text.Replace("\n", lineEnding);

    private static string ResolveOutputLineEnding(LineEndingStyle original) => original switch
    {
        LineEndingStyle.CrLf => "\r\n",
        LineEndingStyle.Lf => "\n",
        LineEndingStyle.Mixed or LineEndingStyle.Unknown => Environment.NewLine,
        _ => Environment.NewLine
    };

    private static LineEndingStyle DetectLineEndingStyle(string text)
    {
        var hasCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var hasLf = text.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n');
        if (hasCrLf && hasLf)
            return LineEndingStyle.Mixed;
        if (hasCrLf)
            return LineEndingStyle.CrLf;
        if (hasLf)
            return LineEndingStyle.Lf;
        return LineEndingStyle.Unknown;
    }

    private enum LineEndingStyle { CrLf, Lf, Mixed, Unknown }
}

internal sealed class ConnectionStringSecretRedactor : ISecretRedactor
{
    private static readonly Regex SecretSegment = new(
        "(?<prefix>\\b(?:password|pwd|access\\s*token|client\\s*secret)\\s*=\\s*)(?<value>\"[^\"]*\"|'[^']*'|[^;\"'\\r\\n]*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public RedactionResult Redact(string content, RedactionContext context)
    {
        var findings = new List<RedactionFinding>();
        var redacted = RedactConnectionString(content, findings, null);
        return findings.Count == 0
            ? SecretRedactionRules.NoChange(content)
            : new RedactionResult
            {
                Content = redacted,
                WasRedacted = true,
                RedactionCount = findings.Count,
                Findings = findings
            };
    }

    internal static string RedactConnectionString(string content, List<RedactionFinding> findings, string? location) =>
        SecretSegment.Replace(content, match =>
        {
            findings.Add(new RedactionFinding("connection_string", location));
            return match.Groups["prefix"].Value + SecretRedactionRules.Replacement;
        });
}

internal sealed class GenericSecretRedactor : ISecretRedactor
{
    private static readonly Regex PrivateKeyBlock = new(
        "-----BEGIN (?:[A-Z0-9 ]+ )?PRIVATE KEY-----(?:.|\\n|\\r)*?-----END (?:[A-Z0-9 ]+ )?PRIVATE KEY-----",
        RegexOptions.CultureInvariant);
    private static readonly Regex UriPassword = new(
        "(?<prefix>[a-z][a-z0-9+.-]*://[^\\s/:@]+:)(?<secret>[^@\\s/]+)(?<suffix>@)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public RedactionResult Redact(string content, RedactionContext context)
    {
        var findings = new List<RedactionFinding>();
        var redacted = PrivateKeyBlock.Replace(content, _ =>
        {
            findings.Add(new RedactionFinding("private_key", null));
            return SecretRedactionRules.Replacement;
        });
        redacted = UriPassword.Replace(redacted, match =>
        {
            findings.Add(new RedactionFinding("uri_password", null));
            return match.Groups["prefix"].Value + SecretRedactionRules.Replacement + match.Groups["suffix"].Value;
        });

        return findings.Count == 0
            ? SecretRedactionRules.NoChange(content)
            : new RedactionResult
            {
                Content = redacted,
                WasRedacted = true,
                RedactionCount = findings.Count,
                Findings = findings
            };
    }
}
