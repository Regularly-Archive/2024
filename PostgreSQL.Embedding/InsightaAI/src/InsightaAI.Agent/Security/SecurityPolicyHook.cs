using System.Text.Json;
using System.Text.RegularExpressions;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Hooks;

namespace InsightaAI.Agent.Security;

/// <summary>
/// Enforces user-defined deny rules before a tool can execute.
/// </summary>
public sealed class SecurityPolicyHook : IToolHook
{
    private readonly IReadOnlyList<DenyRule> _denyRules;

    public IReadOnlyList<string> TargetTools { get; } = ["bash"];

    public SecurityPolicyHook(IReadOnlyList<DenyRule>? denyRules)
    {
        _denyRules = denyRules ?? [];
    }

    public bool EvaluateWhenToolAlwaysAllowed => true;

    public Task<ToolHookResult> OnBeforeExecutionAsync(
        string toolName,
        string arguments,
        ToolExecutionContext context)
    {
        var subject = GetMatchSubject(arguments);
        return Task.FromResult(_denyRules.Any(rule => IsMatch(subject, rule))
            ? ToolHookResult.DenyByPolicy
            : ToolHookResult.Allow);
    }

    private static string GetMatchSubject(string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (document.RootElement.TryGetProperty("command", out var command) &&
                command.ValueKind == JsonValueKind.String)
            {
                return Normalize(command.GetString() ?? string.Empty);
            }
        }
        catch (JsonException)
        {
            // Malformed arguments are handled by the tool; evaluate the original value below.
        }

        return Normalize(arguments);
    }

    private static bool IsMatch(string subject, DenyRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return false;

        var pattern = Normalize(rule.Pattern);
        return rule.Mode switch
        {
            DenyMatchMode.Exact => string.Equals(subject, pattern, StringComparison.Ordinal),
            DenyMatchMode.Glob => Regex.IsMatch(subject, GlobToRegex(pattern), RegexOptions.CultureInvariant),
            DenyMatchMode.Regex => Regex.IsMatch(subject, rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => false
        };
    }

    private static string Normalize(string value) => Regex.Replace(value.Trim(), "\\s+", " ").ToLowerInvariant();

    private static string GlobToRegex(string pattern) => "\\A" +
        Regex.Escape(pattern).Replace("\\*", ".*") + "\\z";
}
