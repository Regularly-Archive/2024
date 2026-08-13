using InsightaAI.Agent.Cli.Localization;
using InsightaAI.Agent.Cli.Models;
using InsightaAI.Agent.Cli.Services;

namespace InsightaAI.Agent.Tests;

public sealed class CliBootstrapTests
{
    private static readonly object EnvironmentLock = new();
    private static readonly string[] BootstrapVariables =
    [
        "INSIGHTA_LANGUAGE",
        "INSIGHTA_TELEMETRY",
        "INSIGHTA_OTLP_ENDPOINT"
    ];

    [Fact]
    public void Initialize_Should_Apply_Configured_Values_Before_Consumers_Read_Them()
    {
        lock (EnvironmentLock)
        {
            var originalValues = CaptureEnvironment();
            try
            {
                ClearBootstrapVariables();
                var config = new CliConfig
                {
                    Language = CliCulture.English,
                    Envs = new Dictionary<string, string>
                    {
                        ["INSIGHTA_LANGUAGE"] = CliCulture.Chinese,
                        ["INSIGHTA_TELEMETRY"] = "1",
                        ["INSIGHTA_OTLP_ENDPOINT"] = "http://collector.example:4318"
                    }
                };

                var bootstrap = CliBootstrap.Initialize(config);

                Assert.Equal(CliCulture.Chinese, bootstrap.Language);
                Assert.True(bootstrap.TelemetryEnabled);
                Assert.Equal("http://collector.example:4318", bootstrap.OtlpEndpoint);
                Assert.Equal(CliCulture.Chinese, Environment.GetEnvironmentVariable("INSIGHTA_LANGUAGE"));
                Assert.Equal("1", Environment.GetEnvironmentVariable("INSIGHTA_TELEMETRY"));
                Assert.Equal("http://collector.example:4318", Environment.GetEnvironmentVariable("INSIGHTA_OTLP_ENDPOINT"));
            }
            finally
            {
                RestoreEnvironment(originalValues);
            }
        }
    }

    [Fact]
    public void Initialize_Should_Preserve_Process_Values_Over_Configured_Values()
    {
        lock (EnvironmentLock)
        {
            var originalValues = CaptureEnvironment();
            try
            {
                Environment.SetEnvironmentVariable("INSIGHTA_LANGUAGE", CliCulture.English);
                Environment.SetEnvironmentVariable("INSIGHTA_TELEMETRY", "0");
                Environment.SetEnvironmentVariable("INSIGHTA_OTLP_ENDPOINT", "http://process.example:4317");

                var bootstrap = CliBootstrap.Initialize(new CliConfig
                {
                    Envs = new Dictionary<string, string>
                    {
                        ["INSIGHTA_LANGUAGE"] = CliCulture.Chinese,
                        ["INSIGHTA_TELEMETRY"] = "1",
                        ["INSIGHTA_OTLP_ENDPOINT"] = "http://config.example:4318"
                    }
                });

                Assert.Equal(CliCulture.English, bootstrap.Language);
                Assert.False(bootstrap.TelemetryEnabled);
                Assert.Equal("http://process.example:4317", bootstrap.OtlpEndpoint);
            }
            finally
            {
                RestoreEnvironment(originalValues);
            }
        }
    }

    [Fact]
    public void Initialize_Should_Use_Defaults_When_Bootstrap_Values_Are_Absent()
    {
        lock (EnvironmentLock)
        {
            var originalValues = CaptureEnvironment();
            try
            {
                ClearBootstrapVariables();

                var bootstrap = CliBootstrap.Initialize(new CliConfig { Language = CliCulture.Chinese });

                Assert.Equal(CliCulture.Chinese, bootstrap.Language);
                Assert.False(bootstrap.TelemetryEnabled);
                Assert.Equal(CliBootstrap.DefaultOtlpEndpoint, bootstrap.OtlpEndpoint);
            }
            finally
            {
                RestoreEnvironment(originalValues);
            }
        }
    }

    private static Dictionary<string, string?> CaptureEnvironment() =>
        BootstrapVariables.ToDictionary(name => name, Environment.GetEnvironmentVariable);

    private static void ClearBootstrapVariables()
    {
        foreach (var name in BootstrapVariables)
            Environment.SetEnvironmentVariable(name, null);
    }

    private static void RestoreEnvironment(IReadOnlyDictionary<string, string?> values)
    {
        foreach (var (name, value) in values)
            Environment.SetEnvironmentVariable(name, value);
    }
}
