using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Harness.Local;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Models;
using System.Text.Json;

namespace InsightaAI.Agent.Tests.Tools;

public sealed class GlobAndGrepToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"insighta-tools-{Guid.NewGuid():N}");
    private readonly LocalFileSystem _fileSystem = new();

    public GlobAndGrepToolTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task GlobAsync_ReturnsOnlyRequestedPatternAsAbsoluteSortedPaths()
    {
        var csFile = WriteFile("a.cs", "class A;");
        WriteFile("notes.md", "notes");
        WriteFile("readme.txt", "readme");

        var results = await _fileSystem.GlobAsync("*.cs", _root);

        Assert.Equal([Path.GetFullPath(csFile)], results);
    }

    [Fact]
    public async Task GlobAsync_SupportsQuestionMarkSingleCharacterWildcard()
    {
        var projectFile = WriteFile("src/Test.csproj", "<Project />");
        WriteFile("src/Test.csxxroj", "not a project");

        var results = await _fileSystem.GlobAsync("**/*.cs?roj", _root);

        Assert.Equal([Path.GetFullPath(projectFile)], results);
    }

    [Fact]
    public async Task GlobAsync_ExcludesCommonBuildDirectoriesByDefault()
    {
        var sourceFile = WriteFile("src/app.txt", "source");
        WriteFile("bin/app.txt", "build output");
        WriteFile("obj/app.txt", "intermediate output");
        WriteFile("node_modules/package/app.txt", "dependency output");

        var results = await _fileSystem.GlobAsync("**/*.txt", _root);

        Assert.Equal([Path.GetFullPath(sourceFile)], results);
    }

    [Fact]
    public async Task GlobAsync_CanIncludeDefaultExcludedDirectories()
    {
        WriteFile("source.txt", "source");
        WriteFile("bin/app.txt", "build output");

        var results = await _fileSystem.GlobAsync("**/*.txt", _root, new GlobOptions
        {
            UseDefaultExcludes = false
        });

        Assert.Equal(2, results.Length);
        Assert.Contains(results, path => path.EndsWith("bin\\app.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GlobTool_AppendsCustomExcludePatterns()
    {
        WriteFile("source.txt", "source");
        WriteFile("generated.txt", "generated");
        var tool = new GlobTool(_fileSystem);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "*.txt",
            ["path"] = _root,
            ["excludes"] = new[] { "generated.txt" }
        }, CreateContext());

        var text = result.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains("source.txt", text);
        Assert.DoesNotContain("generated.txt", text);
    }

    [Fact]
    public async Task GlobTool_UsesArrayExcludeFromJsonArguments()
    {
        WriteFile("source.txt", "source");
        WriteFile("generated.txt", "generated");
        var tool = new GlobTool(_fileSystem);
        using var document = JsonDocument.Parse("[\"generated.txt\"]");

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "*.txt",
            ["path"] = _root,
            ["excludes"] = document.RootElement.Clone()
        }, CreateContext());

        var text = result.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains("source.txt", text);
        Assert.DoesNotContain("generated.txt", text);
    }

    [Fact]
    public async Task GlobTool_UsesArrayExcludesThroughToolRegistry()
    {
        WriteFile("source.txt", "source");
        WriteFile("generated.txt", "generated");
        var registry = new ToolRegistry();
        registry.Register(new GlobTool(_fileSystem));

        var result = await registry.ExecuteAsync(new ToolCallBlock
        {
            Id = "call-1",
            Name = "glob",
            Arguments = JsonSerializer.SerializeToElement(new
            {
                pattern = "*.txt",
                path = _root,
                excludes = new[] { "generated.txt" }
            })
        }, CreateContext());

        var text = result.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains("source.txt", text);
        Assert.DoesNotContain("generated.txt", text);
    }

    [Fact]
    public async Task GlobTool_RejectsNonArrayExcludes()
    {
        WriteFile("source.txt", "source");
        WriteFile("generated.txt", "generated");
        var tool = new GlobTool(_fileSystem);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "*.txt",
            ["path"] = _root,
            ["excludes"] = "generated.txt,*.tmp"
        }, CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("'excludes' must be an array of strings", result.Content.OfType<TextBlock>().Single().Text);
    }

    [Fact]
    public async Task GlobTool_RejectsLegacySingularExclude()
    {
        var tool = new GlobTool(_fileSystem);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "*.txt",
            ["path"] = _root,
            ["exclude"] = "generated.txt"
        }, CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("'exclude' is not supported", result.Content.OfType<TextBlock>().Single().Text);
    }

    [Fact]
    public async Task GlobTool_RejectsParametersMissingFromSchema()
    {
        var tool = new GlobTool(_fileSystem);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "*.txt",
            ["path"] = _root,
            ["unexpected"] = true
        }, CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("'unexpected' is not declared in the tool schema", result.Content.OfType<TextBlock>().Single().Text);
    }

    [Fact]
    public async Task GlobTool_IncludeIgnoredCanSearchBuildDirectories()
    {
        WriteFile("bin/app.txt", "build output");
        var tool = new GlobTool(_fileSystem);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "**/*.txt",
            ["path"] = _root,
            ["include_ignored"] = true
        }, CreateContext());

        Assert.Contains("bin\\app.txt", result.Content.OfType<TextBlock>().Single().Text);
    }

    [Fact]
    public async Task GlobTool_NoMatchesExplainsDefaultExcludedDirectories()
    {
        var tool = new GlobTool(_fileSystem);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "*.dll",
            ["path"] = _root
        }, CreateContext());

        var text = result.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains("Default-excluded directories: bin, obj, node_modules", text);
        Assert.Contains("include_ignored=true", text);
    }

    [Fact]
    public async Task GrepAsync_SearchesExtensionlessFiles()
    {
        var dockerfile = WriteFile("Dockerfile", "FROM dotnet:9.0");
        WriteFile("ignored.bin", "unrelated");

        var result = await _fileSystem.GrepAsync("FROM", _root, new GrepOptions { UseRegex = false });

        var match = Assert.Single(result.Matches);
        Assert.Equal(Path.GetFullPath(dockerfile), match.FilePath);
        Assert.Equal(1, match.LineNumber);
    }

    [Fact]
    public async Task GrepAsync_ExcludeTreatsRegexMetacharactersAsLiterals()
    {
        WriteFile("a+b.cs", "needle");

        var result = await _fileSystem.GrepAsync("needle", _root, new GrepOptions
        {
            UseRegex = false,
            ExcludePatterns = ["a+b.cs"]
        });

        Assert.Empty(result.Matches);
    }

    [Fact]
    public async Task GrepTool_AcceptsMultipleArrayExcludes()
    {
        WriteFile("source.txt", "needle");
        WriteFile("generated.txt", "needle");
        WriteFile("logs/app.log", "needle");
        var tool = new GrepTool(_fileSystem);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "needle",
            ["path"] = _root,
            ["use_regex"] = false,
            ["excludes"] = new[] { "generated.txt", "*.log" }
        }, CreateContext());

        var text = result.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains("source.txt", text);
        Assert.DoesNotContain("generated.txt", text);
        Assert.DoesNotContain("app.log", text);
    }

    [Fact]
    public async Task GrepTool_AcceptsArrayExcludesThroughToolRegistry()
    {
        WriteFile("source.txt", "needle");
        WriteFile("generated.txt", "needle");
        var registry = new ToolRegistry();
        registry.Register(new GrepTool(_fileSystem));

        var result = await registry.ExecuteAsync(new ToolCallBlock
        {
            Id = "call-1",
            Name = "grep",
            Arguments = JsonSerializer.SerializeToElement(new
            {
                pattern = "needle",
                path = _root,
                use_regex = false,
                excludes = new[] { "generated.txt" }
            })
        }, CreateContext());

        var text = result.Content.OfType<TextBlock>().Single().Text;
        Assert.Contains("source.txt", text);
        Assert.DoesNotContain("generated.txt", text);
    }

    [Fact]
    public async Task GrepTool_RejectsLegacySingularExclude()
    {
        var tool = new GrepTool(_fileSystem);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "needle",
            ["path"] = _root,
            ["exclude"] = "*.log"
        }, CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("'exclude' is not supported", result.Content.OfType<TextBlock>().Single().Text);
    }

    [Fact]
    public async Task GrepAsync_InvalidRegexReturnsAnErrorInsteadOfLiteralFallback()
    {
        WriteFile("source.cs", "needle");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fileSystem.GrepAsync("[", _root, new GrepOptions { UseRegex = true }));

        Assert.Contains("Grep failed", exception.Message);
    }

    [Fact]
    public async Task GrepTool_RejectsNonPositiveMaxResults()
    {
        var tool = new GrepTool(_fileSystem);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "needle",
            ["path"] = _root,
            ["max_results"] = 0
        }, CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("must be greater than zero", result.Content.OfType<TextBlock>().Single().Text);
    }

    [Theory]
    [InlineData(typeof(long))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(double))]
    public async Task GrepTool_AcceptsIntegralNumericArgumentRepresentations(Type numericType)
    {
        WriteFile("source.cs", "needle\nneedle");
        var tool = new GrepTool(_fileSystem);
        object maxResults = numericType == typeof(long) ? 1L
            : numericType == typeof(decimal) ? 1m
            : 1d;

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "needle",
            ["path"] = _root,
            ["use_regex"] = false,
            ["max_results"] = maxResults
        }, CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Found at least 1 matches", result.Content.OfType<TextBlock>().Single().Text);
    }

    [Fact]
    public async Task GrepTool_LabelsTruncatedResultsAsPartial()
    {
        WriteFile("source.cs", "needle\nneedle");
        var tool = new GrepTool(_fileSystem);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["pattern"] = "needle",
            ["path"] = _root,
            ["use_regex"] = false,
            ["max_results"] = 1
        }, CreateContext());

        Assert.Contains("Found at least 1 matches", result.Content.OfType<TextBlock>().Single().Text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string WriteFile(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static ToolExecutionContext CreateContext() => new()
    {
        AgentId = "agent-1",
        ToolCallId = "call-1"
    };
}
