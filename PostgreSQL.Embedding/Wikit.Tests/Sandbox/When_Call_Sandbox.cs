using PostgreSQL.Embedding.Infrastructure.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Wikit.Tests.Sandbox;

/// <summary>
/// Docker Sandbox 集成测试
/// </summary>
public class SandboxTests : IAsyncLifetime
{
    private DockerContainerManager _containerManager = null!;
    private string _testSessionId = null!;
    private string _testLocalPath = null!;

    public async Task InitializeAsync()
    {
        var options = new SandboxOptions
        {
            DockerPath = "docker",
            DefaultImage = "insighta/sandbox",
            WorkingDirectory = "/workspace",
            SessionTimeout = TimeSpan.FromMinutes(30),
            MaxLifetime = TimeSpan.FromHours(24)
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<DockerContainerManager>();

        _containerManager = new DockerContainerManager(Options.Create(options), logger);

        _testSessionId = $"test-{Guid.NewGuid():N}";
        _testLocalPath = Path.Combine(Path.GetTempPath(), _testSessionId);
        Directory.CreateDirectory(_testLocalPath);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_testLocalPath))
            {
                Directory.Delete(_testLocalPath, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task CreateContainer_ShouldReturnContainerId()
    {
        var containerId = await _containerManager.CreateContainerAsync(_testSessionId, _testLocalPath);

        this.ShouldSatisfyAllConditions(
            () => containerId.ShouldNotBeNullOrEmpty()
        );

        await _containerManager.DisposeContainerAsync(containerId);
    }

    [Fact]
    public async Task ExecuteCommand_ShouldReturnSuccess()
    {
        var containerId = await _containerManager.CreateContainerAsync(_testSessionId, _testLocalPath);
        var result = await _containerManager.ExecuteCommandAsync(containerId, "echo 'Hello World'");

        this.ShouldSatisfyAllConditions(
            () => result.ExitCode.ShouldBe(0),
            () => result.Stdout.ShouldContain("Hello World")
        );

        await _containerManager.DisposeContainerAsync(containerId);
    }

    [Fact]
    public async Task ExecutePython_ShouldWork()
    {
        var containerId = await _containerManager.CreateContainerAsync(_testSessionId, _testLocalPath);
        var result = await _containerManager.ExecuteCommandAsync(containerId, "python3 -c 'print(2 + 2)'");

        this.ShouldSatisfyAllConditions(
            () => result.ExitCode.ShouldBe(0),
            () => result.Stdout.Trim().ShouldContain("4")
        );

        await _containerManager.DisposeContainerAsync(containerId);
    }

    [Fact]
    public async Task ExecuteNode_ShouldWork()
    {
        var containerId = await _containerManager.CreateContainerAsync(_testSessionId, _testLocalPath);
        var result = await _containerManager.ExecuteCommandAsync(containerId, "node -e 'console.log(2 + 2)'");

        this.ShouldSatisfyAllConditions(
            () => result.ExitCode.ShouldBe(0),
            () => result.Stdout.Trim().ShouldContain("4")
        );

        await _containerManager.DisposeContainerAsync(containerId);
    }

    [Fact]
    public async Task ExecuteDotnet_ShouldWork()
    {
        var containerId = await _containerManager.CreateContainerAsync(_testSessionId, _testLocalPath);
        var result = await _containerManager.ExecuteCommandAsync(containerId, "dotnet --version");

        this.ShouldSatisfyAllConditions(
            () => result.ExitCode.ShouldBe(0),
            () => result.Stdout.ShouldContain("10.0")
        );

        await _containerManager.DisposeContainerAsync(containerId);
    }

    [Fact]
    public async Task WriteFile_ShouldBePersisted()
    {
        var containerId = await _containerManager.CreateContainerAsync(_testSessionId, _testLocalPath);
        var testFile = Path.Combine(_testLocalPath, "test.txt");

        await _containerManager.ExecuteCommandAsync(containerId, $"echo 'test content' > /workspace/{_testSessionId}/test.txt");

        this.ShouldSatisfyAllConditions(
            () => File.Exists(testFile).ShouldBeTrue(),
            () => File.ReadAllText(testFile).ShouldContain("test content")
        );

        await _containerManager.DisposeContainerAsync(containerId);
    }

    [Fact]
    public async Task IsContainerRunning_ShouldReturnTrue()
    {
        var containerId = await _containerManager.CreateContainerAsync(_testSessionId, _testLocalPath);
        var isRunning = await _containerManager.IsContainerRunningAsync(containerId);

        this.ShouldSatisfyAllConditions(
            () => isRunning.ShouldBeTrue()
        );

        await _containerManager.DisposeContainerAsync(containerId);
    }

    [Fact]
    public async Task DisposeContainer_ShouldRemoveContainer()
    {
        var containerId = await _containerManager.CreateContainerAsync(_testSessionId, _testLocalPath);
        await _containerManager.DisposeContainerAsync(containerId);
        var isRunning = await _containerManager.IsContainerRunningAsync(containerId);

        this.ShouldSatisfyAllConditions(
            () => isRunning.ShouldBeFalse()
        );
    }

    [Fact]
    public async Task ExecuteMultipleCommands_ShouldCreateAndRunDotnetProgram()
    {
        var containerId = await _containerManager.CreateContainerAsync(_testSessionId, _testLocalPath);
        var outputFile = Path.Combine(_testLocalPath, "output.txt");

        // Step 1: Create project (当前目录是 /workspace)
        await _containerManager.ExecuteCommandAsync(containerId,
            "dotnet new console -o . --force");

        // Step 2: Write program that outputs to a file
        var programCode = @"Console.WriteLine(""Hello from .NET!""); File.WriteAllText(""output.txt"", ""test content"");";
        await _containerManager.ExecuteCommandAsync(containerId,
            $"echo '{programCode}' > Program.cs");

        // Step 3: Run the program
        var runResult = await _containerManager.ExecuteCommandAsync(containerId,
            "dotnet run");

        // Verify
        this.ShouldSatisfyAllConditions(
            () => runResult.ExitCode.ShouldBe(0),
            () => runResult.Stdout.ShouldContain("Hello from .NET!"),
            () => File.Exists(outputFile).ShouldBeTrue(),
            () => File.ReadAllText(outputFile).ShouldBe("test content")
        );

        await _containerManager.DisposeContainerAsync(containerId);
    }
}
