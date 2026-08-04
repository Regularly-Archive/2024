using InsightaAI.Agent.Memory;
using InsightaAI.Agent.Abstractions;
using Microsoft.Data.Sqlite;

namespace InsightaAI.Agent.Tests.Memory;

public sealed class SqliteMemoryProviderTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"insighta-memory-{Guid.NewGuid():N}.db");
    private SqliteMemoryProvider _provider = null!;

    public Task InitializeAsync()
    {
        _provider = new SqliteMemoryProvider(_databasePath);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(_databasePath)!, $"{Path.GetFileName(_databasePath)}*"))
            File.Delete(path);
    }

    [Fact]
    public async Task SaveAndGetMemoryAsync_PersistsAllFields()
    {
        var memory = CreateMemory("agent-error", "AgentErrorEvent lifecycle", "LLM errors become AgentErrorEvent.");
        memory.Tags = ["agent", "hook"];

        await _provider.SaveMemoryAsync(memory);

        var actual = await _provider.GetMemoryAsync(memory.Id);

        Assert.NotNull(actual);
        Assert.Equal(memory.Content, actual.Content);
        Assert.Equal(memory.Tags, actual.Tags);
        Assert.Equal(MemoryType.Project, actual.Type);
    }

    [Fact]
    public async Task SearchMemoriesAsync_UsesFtsAndScopeFilters()
    {
        await _provider.SaveMemoryAsync(CreateMemory(
            "agent-error", "AgentErrorEvent lifecycle", "LLM errors become AgentErrorEvent and end the turn."));
        await _provider.SaveMemoryAsync(new MemoryEntry
        {
            Id = "other-user",
            UserId = "other",
            Name = "AgentErrorEvent lifecycle",
            Description = "other user",
            Content = "This memory must not be visible.",
            Type = MemoryType.Project,
            Scope = MemoryScope.Private
        });
        await _provider.SaveMemoryAsync(new MemoryEntry
        {
            Id = "team-memory",
            UserId = "owner",
            Project = "insighta",
            Name = "SQLite memory design",
            Description = "Team decision",
            Content = "SQLite FTS5 is the selected memory index.",
            Type = MemoryType.Project,
            Scope = MemoryScope.Team
        });

        var privateResults = await _provider.SearchMemoriesAsync("yuanpei", "AgentErrorEvent lifecycle");
        var teamResults = await _provider.SearchMemoriesAsync("yuanpei", "SQLite FTS5", new MemorySearchOptions
        {
            ProjectId = "insighta"
        });

        Assert.Collection(privateResults, item => Assert.Equal("agent-error", item.Id));
        Assert.Collection(teamResults, item => Assert.Equal("team-memory", item.Id));
        Assert.True(teamResults[0].RelevanceScore > 0);
        Assert.InRange(teamResults[0].RelevanceScore!.Value, 0, 1);
    }

    [Fact]
    public async Task SearchMemoriesAsync_UsesSubstringFallbackForShortTerms()
    {
        var memory = CreateMemory("sqlite", "SQLite memory", "Remember the local SQLite database.");
        memory.Tags = ["ci"];
        await _provider.SaveMemoryAsync(memory);

        var results = await _provider.SearchMemoriesAsync("yuanpei", "ci");

        Assert.Collection(results, item => Assert.Equal(memory.Id, item.Id));
    }

    [Fact]
    public async Task SearchMemoriesAsync_FallsBackToRelevantTermsWhenFtsHasNoExactPhrase()
    {
        var memory = CreateMemory("user-profile", "用户画像", "用户是秦元培，偏好简洁直接的回答。");
        memory.Type = MemoryType.User;
        await _provider.SaveMemoryAsync(memory);

        var results = await _provider.SearchMemoriesAsync("yuanpei", "用户身份 信息 偏好", new MemorySearchOptions
        {
            Type = MemoryType.User
        });

        Assert.Collection(results, item => Assert.Equal(memory.Id, item.Id));
    }

    [Fact]
    public async Task SearchMemoriesAsync_ReturnsRecentRequestedTypeWhenNoTextMatches()
    {
        var memory = CreateMemory("user-profile", "用户画像", "用户是秦元培，偏好简洁直接的回答。");
        memory.Type = MemoryType.User;
        await _provider.SaveMemoryAsync(memory);

        var results = await _provider.SearchMemoriesAsync("yuanpei", "用户身份 信息", new MemorySearchOptions
        {
            Type = MemoryType.User
        });

        Assert.Collection(results, item => Assert.Equal(memory.Id, item.Id));
    }

    [Fact]
    public async Task CreateActiveMemorySnapshotAsync_UsesUserTypeForIdentityQuestions()
    {
        var memory = CreateMemory("user-profile", "User profile", "The user is Yuanpei.");
        memory.Type = MemoryType.User;
        await _provider.SaveMemoryAsync(memory);
        var manager = new MemoryManager(_provider);

        var snapshot = await manager.CreateActiveMemorySnapshotAsync(
            "yuanpei", "Who am I?", "turn-identity");

        Assert.Collection(snapshot.Entries, item => Assert.Equal(memory.Id, item.Id));
        var stored = await _provider.GetMemoryAsync(memory.Id);
        Assert.NotNull(stored);
        Assert.Equal(1, stored.AccessCount);
    }

    [Fact]
    public async Task CreateActiveMemorySnapshotAsync_IncludesCoreWithoutTouchingIt()
    {
        var core = CreateMemory("core-style", "Response style", "Use concise Chinese responses.");
        core.Type = MemoryType.Feedback;
        core.Activation = MemoryActivation.Core;
        await _provider.SaveMemoryAsync(core);
        var manager = new MemoryManager(_provider);

        var snapshot = await manager.CreateActiveMemorySnapshotAsync(
            "yuanpei", "unrelated task", "turn-core");

        Assert.Collection(snapshot.CoreEntries, item => Assert.Equal(core.Id, item.Id));
        Assert.Empty(snapshot.ActiveEntries);
        var stored = await _provider.GetMemoryAsync(core.Id);
        Assert.NotNull(stored);
        Assert.Equal(MemoryActivation.Core, stored.Activation);
        Assert.Equal(0, stored.AccessCount);
    }

    [Fact]
    public async Task SearchMemoryTool_Should_TouchReturnedMemories()
    {
        var memory = CreateMemory("tool-access", "Search tool memory", "A memory returned by the search tool.");
        await _provider.SaveMemoryAsync(memory);
        var manager = new MemoryManager(_provider);
        var registry = new ToolRegistry();
        MemoryTools.RegisterAll(registry, manager, "yuanpei");
        var tool = registry.GetExecutor("search_memory");

        Assert.NotNull(tool);
        var result = await tool.ExecuteAsync(
            new Dictionary<string, object> { ["query"] = "search tool memory" },
            new ToolExecutionContext { AgentId = "test", ToolCallId = "call-1" });

        Assert.False(result.IsError);
        var stored = await _provider.GetMemoryAsync(memory.Id);
        Assert.NotNull(stored);
        Assert.Equal(1, stored.AccessCount);
    }

    [Fact]
    public async Task TouchMemoryAsync_UpdatesAccessDataWithoutChangingContent()
    {
        var memory = CreateMemory("sqlite", "SQLite memory", "Remember the local SQLite database.");
        await _provider.SaveMemoryAsync(memory);

        await _provider.TouchMemoryAsync(memory.Id);
        var actual = await _provider.GetMemoryAsync(memory.Id);

        Assert.NotNull(actual);
        Assert.Equal(1, actual.AccessCount);
        Assert.NotNull(actual.LastAccessedAt);
        Assert.Equal(memory.Content, actual.Content);
    }

    [Fact]
    public async Task GetMemoryIndexAsync_ReturnsLightweightCounts()
    {
        await _provider.SaveMemoryAsync(CreateMemory("private", "Private memory", "A private entry."));
        await _provider.SaveMemoryAsync(new MemoryEntry
        {
            Id = "team",
            UserId = "owner",
            Project = "insighta",
            Name = "Team memory",
            Description = "A team entry.",
            Content = "The shared project decision.",
            Type = MemoryType.Project,
            Scope = MemoryScope.Team
        });

        var index = await _provider.GetMemoryIndexAsync("yuanpei", "insighta");

        Assert.Equal(
            "You have 1 private memories and 1 team memories available. Use search_memory to retrieve information relevant to the current task.",
            index);
    }

    private static MemoryEntry CreateMemory(string id, string name, string content) => new()
    {
        Id = id,
        UserId = "yuanpei",
        Name = name,
        Description = name,
        Content = content,
        Type = MemoryType.Project,
        Scope = MemoryScope.Private
    };
}
