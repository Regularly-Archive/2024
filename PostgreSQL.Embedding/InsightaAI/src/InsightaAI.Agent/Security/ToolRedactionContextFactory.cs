using System.Text.Json;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Security;

internal static class ToolRedactionContextFactory
{
    public static RedactionContext Create(ToolCallBlock toolCall)
    {
        var sourcePath = TryGetSourcePath(toolCall.Arguments);
        return new RedactionContext
        {
            ToolName = toolCall.Name,
            SourcePath = sourcePath,
            Format = DetectFormat(sourcePath)
        };
    }

    private static string? TryGetSourcePath(JsonElement arguments)
    {
        foreach (var propertyName in new[] { "file_path", "path" })
        {
            if (arguments.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static SecretContentFormat DetectFormat(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return SecretContentFormat.Unknown;

        var fileName = Path.GetFileName(sourcePath);
        if (fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase))
            return SecretContentFormat.DotEnv;

        return Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".json" => SecretContentFormat.Json,
            ".yaml" or ".yml" => SecretContentFormat.Yaml,
            ".xml" or ".config" => SecretContentFormat.Xml,
            ".ini" => SecretContentFormat.Ini,
            _ => SecretContentFormat.PlainText
        };
    }
}
