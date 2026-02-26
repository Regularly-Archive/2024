using PostgreSQL.Embedding.Infrastructure.Sandbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Wikit.Tests.Sandbox;

/// <summary>
/// Docker 卷挂载同步测试
/// </summary>
public class VolumeSyncTests : IAsyncLifetime
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

        _testSessionId = $"vol-{Guid.NewGuid():N}";
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
    public async Task WriteFileInContainer_ShouldSyncToHost()
    {
        var containerId = await _containerManager.CreateContainerAsync(_testSessionId, new Dictionary<string, string>());
        var testFile = Path.Combine(_testLocalPath, "test.txt");

        // 容器内写文件 (当前目录是 /workspace)
        var result = await _containerManager.ExecuteCommandAsync(containerId,
            "echo 'hello from container' > test.txt");

        // 检查容器内文件
        var checkResult = await _containerManager.ExecuteCommandAsync(containerId,
            "cat test.txt");

        this.ShouldSatisfyAllConditions(
            () => result.ExitCode.ShouldBe(0),
            () => checkResult.ExitCode.ShouldBe(0),
            () => checkResult.Stdout.ShouldContain("hello from container"),
            () => File.Exists(testFile).ShouldBeTrue($"File should exist at {testFile}")
        );

        await _containerManager.DisposeContainerAsync(containerId);
    }
}
