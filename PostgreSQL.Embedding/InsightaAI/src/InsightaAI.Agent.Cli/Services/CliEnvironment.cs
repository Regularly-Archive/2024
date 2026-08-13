using InsightaAI.Agent.Abstractions;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// Resolves Agent environment values from the process and CLI configuration.
/// Process values take precedence over values configured in CliConfig.Envs.
/// </summary>
public sealed class CliEnvironment : IEnvironmentVariableReader
{
    private static readonly string[] BootstrapVariableNames =
    [
        "INSIGHTA_LANGUAGE",
        "INSIGHTA_TELEMETRY",
        "INSIGHTA_OTLP_ENDPOINT"
    ];

    private readonly IReadOnlyDictionary<string, string> _configuredValues;

    public CliEnvironment(IReadOnlyDictionary<string, string>? configuredValues = null)
    {
        _configuredValues = configuredValues ?? new Dictionary<string, string>();
    }

    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Environment.GetEnvironmentVariable(name)
            ?? (_configuredValues.TryGetValue(name, out var value) ? value : null);
    }

    /// <summary>
    /// Applies only variables needed before the CLI host is created.
    /// Existing process values always win over values from CliConfig.Envs.
    /// </summary>
    public void ApplyBootstrapVariables()
    {
        foreach (var name in BootstrapVariableNames)
        {
            if (Environment.GetEnvironmentVariable(name) != null)
                continue;

            if (_configuredValues.TryGetValue(name, out var value))
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
