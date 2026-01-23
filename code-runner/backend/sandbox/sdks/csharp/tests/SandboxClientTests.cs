using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeRunner.SandboxSdk;
using CodeRunner.SandboxSdk.Models;
using Xunit;

namespace SandboxSdk.Tests;

/// <summary>
/// Unit tests for SandboxClient.
/// Uses a custom HttpMessageHandler for mocking.
/// </summary>
public class ModelsTests
{
    [Fact]
    public void ExecResult_Success_ReturnsTrueForExitCodeZero()
    {
        var result = new ExecResult
        {
            ExecutionId = "exec_123",
            ExitCode = 0,
            Stdout = "Hello",
            Stderr = "",
            DurationMs = 100,
            FilesChanged = new List<string>()
        };

        Assert.True(result.Success);
    }

    [Fact]
    public void ExecResult_Success_ReturnsFalseForNonZeroExitCode()
    {
        var result = new ExecResult
        {
            ExecutionId = "exec_123",
            ExitCode = 1,
            Stdout = "",
            Stderr = "Error",
            DurationMs = 100,
            FilesChanged = new List<string>()
        };

        Assert.False(result.Success);
    }
}

/// <summary>
/// A simple mock handler for testing HTTP calls.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responseFactory;

    public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_responseFactory(request, cancellationToken));
    }
}

public class SandboxClientTests : IDisposable
{
    private HttpClient? _httpClient;
    private readonly SandboxClient _client;

    public SandboxClientTests()
    {
        _client = new SandboxClient("http://localhost:8002");
    }

    private void SetMockHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
    {
        var handler = new MockHttpMessageHandler(responseFactory);
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8002")
        };

        var field = typeof(SandboxClient).GetField("_httpClient",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(_client, _httpClient);
    }

    private static HttpResponseMessage CreateJsonResponse(object content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = JsonContent.Create(content, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })
        };
    }

    [Fact]
    public async Task ListTemplatesAsync_ReturnsTemplates()
    {
        SetMockHandler((_, _) => CreateJsonResponse(new
        {
            templates = new object[]
            {
                new { id = "python-basic", description = "Python runtime", capabilities = new[] { "bash", "python" }, defaults = new Dictionary<string, string>(), constraints = new Dictionary<string, object>() }
            }
        }));

        var templates = await _client.ListTemplatesAsync();

        Assert.Single(templates);
        Assert.Equal("python-basic", templates[0].Id);
    }

    [Fact]
    public async Task CreateSandboxAsync_CreatesAndReturnsSandbox()
    {
        SetMockHandler((_, _) => CreateJsonResponse(new
        {
            sandbox_id = "sbx_new123",
            status = "running",
            paths = new { workspace = "/workspace" },
            runtime = new { image = "python:3.11", resolved_from = "template:python-basic" },
            created_at = "2024-01-01T00:00:00"
        }));

        var sandbox = await _client.CreateSandboxAsync("python-basic");

        Assert.Equal("sbx_new123", sandbox.Id);
        Assert.Equal("running", sandbox.Status);
    }

    [Fact]
    public async Task ExecAsync_ExecutesCommand()
    {
        SetMockHandler((_, _) => CreateJsonResponse(new
        {
            execution_id = "exec_123",
            exit_code = 0,
            stdout = "Hello",
            stderr = "",
            duration_ms = 100,
            files_changed = Array.Empty<string>()
        }));

        var result = await _client.ExecAsync("sbx_test", "echo hello");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Hello", result.Stdout);
    }

    [Fact]
    public async Task ExecAsync_WithTimeout_SendsTimeout()
    {
        bool timeoutSent = false;
        SetMockHandler((req, _) =>
        {
            if (req.Content != null)
            {
                var content = req.Content.ReadAsStringAsync().Result;
                timeoutSent = content.Contains("timeout");
            }
            return CreateJsonResponse(new
            {
                execution_id = "exec_123",
                exit_code = 0,
                stdout = "Done",
                stderr = "",
                duration_ms = 1000,
                files_changed = Array.Empty<string>()
            });
        });

        await _client.ExecAsync("sbx_test", "sleep 30", timeout: 60);

        Assert.True(timeoutSent);
    }

    [Fact]
    public async Task ExecAndCheckAsync_ThrowsOnFailure()
    {
        SetMockHandler((_, _) => CreateJsonResponse(new
        {
            execution_id = "exec_123",
            exit_code = 1,
            stdout = "",
            stderr = "Command failed",
            duration_ms = 100,
            files_changed = Array.Empty<string>()
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _client.ExecAndCheckAsync("sbx_test", "failing_command")
        );
    }

    [Fact]
    public async Task WriteFileAsync_SendsFileContent()
    {
        bool hasFileContent = false;
        SetMockHandler((req, _) =>
        {
            if (req.Content != null)
            {
                var content = req.Content.ReadAsStringAsync().Result;
                hasFileContent = content.Contains("test.py") && content.Contains("print");
            }
            return CreateJsonResponse(new { status = "ok" });
        });

        await _client.WriteFileAsync("sbx_test", "test.py", "print('hello')");

        Assert.True(hasFileContent);
    }

    [Fact]
    public async Task ListFilesAsync_ReturnsFileList()
    {
        SetMockHandler((_, _) => CreateJsonResponse(new
        {
            items = new object[]
            {
                new { name = "file.txt", path = "/workspace/file.txt", is_dir = false, size = 1024 },
                new { name = "folder", path = "/workspace/folder", is_dir = true }
            }
        }));

        var files = await _client.ListFilesAsync("sbx_test", "/workspace");

        Assert.Equal(2, files.Count);
        Assert.Equal("file.txt", files[0].Name);
        Assert.False(files[0].IsDir);
        Assert.True(files[1].IsDir);
    }

    [Fact]
    public async Task GetEnvironmentAsync_ReturnsEnvironment()
    {
        SetMockHandler((_, _) => CreateJsonResponse(new
        {
            os = "linux",
            arch = "amd64",
            capabilities = new[] { "bash", "python@3.11" },
            paths = new { workspace = "/workspace" }
        }));

        var env = await _client.GetEnvironmentAsync("sbx_test");

        Assert.Equal("linux", env.Os);
        Assert.Equal("amd64", env.Arch);
        Assert.Contains("python@3.11", env.Capabilities);
    }

    [Fact]
    public async Task ExportAsync_ReturnsExportResult()
    {
        SetMockHandler((_, _) => CreateJsonResponse(new
        {
            artifact_id = "art_123",
            path = ".",
            size = 1024,
            download_url = "/api/sandbox/artifacts/sbx_test/art_123.zip"
        }));

        var result = await _client.ExportAsync("sbx_test", ".");

        Assert.Equal("art_123", result.ArtifactId);
        Assert.Contains("art_123", result.DownloadUrl);
    }

    [Fact]
    public async Task DestroyAsync_SendsDeleteRequest()
    {
        string method = "";
        SetMockHandler((req, _) =>
        {
            method = req.Method.ToString();
            return CreateJsonResponse(new { status = "ok" });
        });

        await _client.DestroyAsync("sbx_test");

        Assert.Equal("DELETE", method);
    }

    [Fact]
    public async Task DestroyAsync_WithExport_SendsExportParam()
    {
        string? query = null;
        SetMockHandler((req, _) =>
        {
            query = req.RequestUri?.Query;
            return CreateJsonResponse(new { status = "ok" });
        });

        await _client.DestroyAsync("sbx_test", "output");

        Assert.NotNull(query);
        Assert.Contains("export=output", query);
    }

    [Fact]
    public async Task SyncFilesAsync_ReturnsSyncedCount()
    {
        SetMockHandler((_, _) => CreateJsonResponse(new { synced = 3 }));

        var count = await _client.SyncFilesAsync("sbx_test", new Dictionary<string, string>
        {
            ["file1.py"] = "content1",
            ["file2.py"] = "content2"
        });

        Assert.Equal(3, count);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
