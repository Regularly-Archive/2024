using System.Text.Json;

namespace InsightaAI.Agent.Evals;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> Main(string[] args)
    {
        var scenarios = LoadScenarios();
        if (args.Contains("--list", StringComparer.Ordinal))
        {
            foreach (var scenario in scenarios)
                Console.WriteLine($"{scenario.Id}\t{scenario.Description}");
            return 0;
        }

        var scenarioId = ReadOption(args, "--scenario");
        var reportPath = ReadOption(args, "--report");
        var selected = scenarioId == null
            ? scenarios
            : scenarios.Where(x => string.Equals(x.Id, scenarioId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selected.Count == 0)
        {
            Console.Error.WriteLine($"Unknown scenario '{scenarioId}'. Run with --list to inspect available scenarios.");
            return 2;
        }

        var runner = new EvaluationRunner();
        var reports = new List<EvaluationReport>();
        foreach (var scenario in selected)
        {
            var report = await runner.RunAsync(scenario);
            reports.Add(report);
            Console.WriteLine($"{(report.Passed ? "PASS" : "FAIL")}\t{report.ScenarioId}\t{report.DurationMs}ms");
            foreach (var failure in report.Failures)
                Console.WriteLine($"  {failure}");
        }

        var json = JsonSerializer.Serialize(reports, JsonOptions);
        if (reportPath != null)
        {
            var fullPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllTextAsync(fullPath, json);
            Console.WriteLine($"Report: {fullPath}");
        }
        else
        {
            Console.WriteLine(json);
        }

        return reports.All(x => x.Passed) ? 0 : 1;
    }

    private static IReadOnlyList<EvaluationScenario> LoadScenarios()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Scenarios");
        if (!Directory.Exists(directory))
            throw new InvalidOperationException($"Scenario directory was not found: '{directory}'.");

        return Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Select(path => JsonSerializer.Deserialize<EvaluationScenario>(File.ReadAllText(path))
                ?? throw new InvalidOperationException($"Scenario file is empty: '{path}'."))
            .OrderBy(scenario => scenario.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.Ordinal)) continue;
            if (index == args.Count - 1)
                throw new ArgumentException($"Option '{name}' requires a value.");
            return args[index + 1];
        }

        return null;
    }
}
