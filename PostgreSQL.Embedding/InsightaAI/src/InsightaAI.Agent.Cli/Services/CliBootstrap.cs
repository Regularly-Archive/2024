using InsightaAI.Agent.Cli.Models;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// Resolves the configuration required before the CLI host is created.
/// </summary>
public sealed record CliBootstrap(
    CliEnvironment Environment,
    string? Language,
    bool TelemetryEnabled,
    string OtlpEndpoint)
{
    public const string DefaultOtlpEndpoint = "http://localhost:4317";

    public static CliBootstrap Initialize(CliConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var environment = new CliEnvironment(config.Envs);
        environment.ApplyBootstrapVariables();

        var language = environment.Get("INSIGHTA_LANGUAGE") ?? config.Language;
        var telemetryEnabled = string.Equals(
            environment.Get("INSIGHTA_TELEMETRY"), "1", StringComparison.OrdinalIgnoreCase);
        var otlpEndpoint = environment.Get("INSIGHTA_OTLP_ENDPOINT") ?? DefaultOtlpEndpoint;

        return new CliBootstrap(environment, language, telemetryEnabled, otlpEndpoint);
    }
}
