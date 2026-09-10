using InsightaAI.Agent.Storage;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests.Storage;

public sealed class JsonlMessageStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonlMessageStorage _storage;

    public JsonlMessageStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"insighta-storage-test-{Guid.NewGuid():N}");
        _storage = new JsonlMessageStorage(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* Test cleanup must not hide assertion failures. */ }
        }
    }

    [Fact]
    public async Task GetSessionsAsync_Should_ApplyOffsetAndLimit()
    {
        var sessions = new List<SessionRecord>();
        for (var index = 0; index < 5; index++)
        {
            var session = await _storage.CreateSessionAsync("model", "provider");
            session.UpdatedAt = DateTime.UtcNow.AddMinutes(index);
            await _storage.UpdateSessionAsync(session);
            sessions.Add(session);
        }

        var page = await _storage.GetSessionsAsync(offset: 1, limit: 2);

        Assert.Equal(2, page.Count);
        Assert.Equal(sessions[3].Id, page[0].Id);
        Assert.Equal(sessions[2].Id, page[1].Id);
    }

    [Fact]
    public async Task GetSessionsAsync_Should_ReturnEmpty_WhenOffsetExceedsCount()
    {
        await _storage.CreateSessionAsync("model", "provider");

        var page = await _storage.GetSessionsAsync(offset: 1, limit: 10);

        Assert.Empty(page);
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldPersistParentInvocationMetadata()
    {
        var session = await _storage.CreateSessionAsync(
            "model", "provider", parentSessionId: "parent-session", parentInvocationId: "parent-invocation", invocationId: "child-invocation");

        var loaded = await _storage.GetSessionAsync(session.Id);

        Assert.NotNull(loaded);
        Assert.Equal("parent-session", loaded!.ParentSessionId);
        Assert.Equal("parent-invocation", loaded.ParentInvocationId);
        Assert.Equal("child-invocation", loaded.InvocationId);
    }

    [Fact]
    public async Task UserVisibleSessionQueries_ShouldExcludeSubagentSessions()
    {
        var main = await _storage.CreateSessionAsync("model", "provider", workDir: "project");
        main.UpdatedAt = DateTime.UtcNow.AddMinutes(-1);
        await _storage.UpdateSessionAsync(main);

        var child = await _storage.CreateSessionAsync(
            "model", "provider", workDir: "project", parentSessionId: main.Id);

        var sessions = await _storage.GetSessionsAsync();
        var last = await _storage.GetLastSessionForWorkDirAsync("project");

        Assert.Single(sessions);
        Assert.Equal(main.Id, sessions[0].Id);
        Assert.Equal(main.Id, last!.Id);
        Assert.Null(await _storage.GetMainSessionAsync(child.Id));
        Assert.NotNull(await _storage.GetSessionAsync(child.Id));
    }

    [Fact]
    public async Task DeleteSessionAsync_Should_RemoveSessionAndMessageDirectory()
    {
        var session = await _storage.CreateSessionAsync("model", "provider");
        await _storage.AddMessageAsync(session.Id, new MessageRecord
        {
            Role = "user",
            Content = [new TextContent { Text = "test" }]
        });

        await _storage.DeleteSessionAsync(session.Id);

        Assert.Null(await _storage.GetSessionAsync(session.Id));
        Assert.False(Directory.Exists(Path.Combine(_tempDir, session.Id)));
    }

    [Fact]
    public async Task GetMessagesAsync_Should_Read_Legacy_PascalCase_ToolResultState()
    {
        var session = await _storage.CreateSessionAsync("model", "provider");
        var legacyMessage = """
            {"id":"legacy-state","role":"tool","content":[],"tool_result_state":{"RetentionLevel":1,"Artifact":{"Id":"artifact-1","Path":"tool_results/artifact.txt","ContentType":"text/plain","ByteSize":42,"CreatedAt":"2026-09-10T00:00:00+00:00"},"OriginalLength":100,"CanReplay":true,"HasSideEffects":false,"MinimumLevel":2},"created_at":"2026-09-10T00:00:00Z"}
            """;
        await File.WriteAllTextAsync(Path.Combine(_tempDir, session.Id, "messages.jsonl"), legacyMessage);

        var message = Assert.Single(await _storage.GetMessagesAsync(session.Id));
        var state = Assert.IsType<ToolResultState>(message.ToolResultState);

        Assert.Equal(ToolResultRetentionLevel.Preview, state.RetentionLevel);
        Assert.Equal(100, state.OriginalLength);
        Assert.True(state.CanReplay);
        Assert.NotNull(state.Artifact);
        Assert.Equal("artifact-1", state.Artifact!.Id);
        Assert.Equal(42, state.Artifact.ByteSize);
    }

    [Fact]
    public async Task AddMessageAsync_Should_Write_CamelCase_ToolResultState()
    {
        var session = await _storage.CreateSessionAsync("model", "provider");
        await _storage.AddMessageAsync(session.Id, new MessageRecord
        {
            Role = "tool",
            ToolResultState = new ToolResultState
            {
                RetentionLevel = ToolResultRetentionLevel.Preview,
                Artifact = new ToolResultArtifactInfo
                {
                    Id = "artifact-1",
                    Path = "tool_results/artifact.txt",
                    ByteSize = 42
                },
                OriginalLength = 100,
                CanReplay = true,
                MinimumLevel = ToolResultRetentionLevel.Placeholder
            }
        });

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, session.Id, "messages.jsonl"));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var state = document.RootElement.GetProperty("tool_result_state");
        var artifact = state.GetProperty("artifact");

        Assert.True(state.TryGetProperty("retentionLevel", out _));
        Assert.True(state.TryGetProperty("originalLength", out _));
        Assert.True(state.TryGetProperty("canReplay", out _));
        Assert.True(state.TryGetProperty("hasSideEffects", out _));
        Assert.True(state.TryGetProperty("minimumLevel", out _));
        Assert.True(artifact.TryGetProperty("contentType", out _));
        Assert.True(artifact.TryGetProperty("byteSize", out _));
        Assert.False(state.TryGetProperty("RetentionLevel", out _));
        Assert.False(artifact.TryGetProperty("ByteSize", out _));
    }
}
