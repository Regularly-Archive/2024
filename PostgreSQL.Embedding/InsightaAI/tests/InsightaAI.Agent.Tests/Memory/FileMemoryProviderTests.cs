using InsightaAI.Agent.Memory;

namespace InsightaAI.Agent.Tests.Memory;

/// <summary>
/// FileMemoryProvider 单元测试
/// 使用临时目录隔离测试环境
/// </summary>
public class FileMemoryProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileMemoryProvider _provider;
    private const string TestUserId = "test_user_001";

    public FileMemoryProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"insightai_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _provider = new FileMemoryProvider(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* 清理失败不影响测试 */ }
        }
    }

    #region SaveMemoryAsync

    [Fact]
    public async Task SaveMemoryAsync_Should_CreateMemoryFile()
    {
        // Arrange
        var entry = CreateTestMemory("test-001", "用户偏好使用 C#");

        // Act
        await _provider.SaveMemoryAsync(entry);

        // Assert
        var filePath = Path.Combine(_tempDir, "private", TestUserId, "memories", "test-001.md");
        Assert.True(File.Exists(filePath));

        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("用户偏好使用 C#", content);
        Assert.Contains("name: Test Memory", content);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_CreateMemoryIndex()
    {
        // Arrange
        var entry = CreateTestMemory("test-idx-001", "索引测试内容");

        // Act
        await _provider.SaveMemoryAsync(entry);

        // Assert
        var indexPath = Path.Combine(_tempDir, "private", TestUserId, "MEMORY.md");
        Assert.True(File.Exists(indexPath));

        var indexContent = await File.ReadAllTextAsync(indexPath);
        Assert.Contains("test-idx-001.md", indexContent);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_UpdateExistingIndex()
    {
        // Arrange
        var entry1 = CreateTestMemory("test-idx-001", "第一条记忆");
        var entry2 = CreateTestMemory("test-idx-002", "第二条记忆");

        // Act
        await _provider.SaveMemoryAsync(entry1);
        await _provider.SaveMemoryAsync(entry2);

        // Assert
        var indexPath = Path.Combine(_tempDir, "private", TestUserId, "MEMORY.md");
        var indexContent = await File.ReadAllTextAsync(indexPath);
        Assert.Contains("test-idx-001.md", indexContent);
        Assert.Contains("test-idx-002.md", indexContent);
    }

    [Fact]
    public async Task SaveMemoryAsync_TeamScope_Should_SaveToTeamDirectory()
    {
        // Arrange
        var entry = CreateTestMemory("team-001", "团队记忆", scope: MemoryScope.Team, project: "project-abc");

        // Act
        await _provider.SaveMemoryAsync(entry);

        // Assert
        var filePath = Path.Combine(_tempDir, "team", "project-abc", "memories", "team-001.md");
        Assert.True(File.Exists(filePath));
    }

    #endregion

    #region GetMemoryAsync

    [Fact]
    public async Task GetMemoryAsync_Should_ReturnMemory_WhenExists()
    {
        // Arrange
        var entry = CreateTestMemory("get-001", "测试获取记忆");
        await _provider.SaveMemoryAsync(entry);

        // Act
        var result = await _provider.GetMemoryAsync("get-001");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("get-001", result.Id);
        Assert.Equal("测试获取记忆", result.Content);
        Assert.Equal("Test Memory", result.Name);
    }

    [Fact]
    public async Task GetMemoryAsync_Should_ReturnNull_WhenNotExists()
    {
        // Act
        var result = await _provider.GetMemoryAsync("nonexistent");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region SearchMemoriesAsync

    [Fact]
    public async Task SearchMemoriesAsync_Should_FindMatchingMemories()
    {
        // Arrange
        await _provider.SaveMemoryAsync(CreateTestMemory("s-001", "用户喜欢使用 C# 编程"));
        await _provider.SaveMemoryAsync(CreateTestMemory("s-002", "项目使用 PostgreSQL 数据库"));
        await _provider.SaveMemoryAsync(CreateTestMemory("s-003", "用户偏好 VS Code 编辑器"));

        // Act
        var results = await _provider.SearchMemoriesAsync(TestUserId, "C# 编程");

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Id == "s-001");
    }

    [Fact]
    public async Task SearchMemoriesAsync_Should_RespectMaxResults()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _provider.SaveMemoryAsync(CreateTestMemory($"bulk-{i}", $"记忆条目 {i} 包含测试关键词"));
        }

        // Act
        var results = await _provider.SearchMemoriesAsync(TestUserId, "测试关键词",
            new MemorySearchOptions { MaxResults = 3 });

        // Assert
        Assert.True(results.Count <= 3);
    }

    [Fact]
    public async Task SearchMemoriesAsync_Should_FilterByType()
    {
        // Arrange
        var userEntry = CreateTestMemory("type-001", "用户偏好信息", type: MemoryType.User);
        var projectEntry = CreateTestMemory("type-002", "项目决策信息", type: MemoryType.Project);
        await _provider.SaveMemoryAsync(userEntry);
        await _provider.SaveMemoryAsync(projectEntry);

        // Act
        var results = await _provider.SearchMemoriesAsync(TestUserId, "信息",
            new MemorySearchOptions { Type = MemoryType.User });

        // Assert
        Assert.All(results, r => Assert.Equal(MemoryType.User, r.Type));
    }

    [Fact]
    public async Task SearchMemoriesAsync_Should_NotModifySearchResults()
    {
        // Arrange - 搜索不应修改 LastAccessedAt 和 AccessCount
        await _provider.SaveMemoryAsync(CreateTestMemory("side-001", "测试副作用"));

        // Act
        var results = await _provider.SearchMemoriesAsync(TestUserId, "测试副作用");
        var memory = await _provider.GetMemoryAsync("side-001");

        // Assert - 搜索后访问信息不应改变
        Assert.NotNull(memory);
        Assert.Null(memory.LastAccessedAt);
        Assert.Equal(0, memory.AccessCount);
    }

    #endregion

    #region ListMemoriesAsync

    [Fact]
    public async Task ListMemoriesAsync_Should_ReturnAllUserMemories()
    {
        // Arrange
        await _provider.SaveMemoryAsync(CreateTestMemory("list-001", "记忆 A"));
        await _provider.SaveMemoryAsync(CreateTestMemory("list-002", "记忆 B"));
        await _provider.SaveMemoryAsync(CreateTestMemory("list-003", "记忆 C"));

        // Act
        var results = await _provider.ListMemoriesAsync(TestUserId);

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task ListMemoriesAsync_Should_SupportPagination()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _provider.SaveMemoryAsync(CreateTestMemory($"page-{i:D2}", $"记忆 {i}"));
        }

        // Act
        var page1 = await _provider.ListMemoriesAsync(TestUserId, skip: 0, take: 3);
        var page2 = await _provider.ListMemoriesAsync(TestUserId, skip: 3, take: 3);

        // Assert
        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.DoesNotContain(page1[0].Id, page2.Select(p => p.Id));
    }

    #endregion

    #region UpdateMemoryAsync

    [Fact]
    public async Task UpdateMemoryAsync_Should_UpdateContent()
    {
        // Arrange
        var entry = CreateTestMemory("upd-001", "原始内容");
        await _provider.SaveMemoryAsync(entry);

        // Act
        entry.Content = "更新后的内容";
        entry.UpdatedAt = DateTime.UtcNow;
        await _provider.UpdateMemoryAsync(entry);

        // Assert
        var updated = await _provider.GetMemoryAsync("upd-001");
        Assert.NotNull(updated);
        Assert.Equal("更新后的内容", updated.Content);
    }

    #endregion

    #region DeleteMemoryAsync

    [Fact]
    public async Task DeleteMemoryAsync_Should_RemoveMemoryFile()
    {
        // Arrange
        var entry = CreateTestMemory("del-001", "待删除的记忆");
        await _provider.SaveMemoryAsync(entry);

        // Act
        await _provider.DeleteMemoryAsync("del-001");

        // Assert
        var result = await _provider.GetMemoryAsync("del-001");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteMemoryAsync_Should_UpdateIndex()
    {
        // Arrange
        await _provider.SaveMemoryAsync(CreateTestMemory("del-idx-001", "记忆 A"));
        await _provider.SaveMemoryAsync(CreateTestMemory("del-idx-002", "记忆 B"));

        // Act
        await _provider.DeleteMemoryAsync("del-idx-001");

        // Assert
        var indexPath = Path.Combine(_tempDir, "private", TestUserId, "MEMORY.md");
        var indexContent = await File.ReadAllTextAsync(indexPath);
        Assert.DoesNotContain("del-idx-001.md", indexContent);
        Assert.Contains("del-idx-002.md", indexContent);
    }

    #endregion

    #region TouchMemoryAsync

    [Fact]
    public async Task TouchMemoryAsync_Should_UpdateAccessInfo()
    {
        // Arrange
        await _provider.SaveMemoryAsync(CreateTestMemory("touch-001", "测试访问跟踪"));

        // Act
        await _provider.TouchMemoryAsync("touch-001");

        // Assert
        var memory = await _provider.GetMemoryAsync("touch-001");
        Assert.NotNull(memory);
        Assert.NotNull(memory.LastAccessedAt);
        Assert.Equal(1, memory.AccessCount);
    }

    [Fact]
    public async Task TouchMemoryAsync_Should_IncrementAccessCount()
    {
        // Arrange
        await _provider.SaveMemoryAsync(CreateTestMemory("touch-002", "多次访问测试"));

        // Act
        await _provider.TouchMemoryAsync("touch-002");
        await _provider.TouchMemoryAsync("touch-002");
        await _provider.TouchMemoryAsync("touch-002");

        // Assert
        var memory = await _provider.GetMemoryAsync("touch-002");
        Assert.NotNull(memory);
        Assert.Equal(3, memory.AccessCount);
    }

    #endregion

    #region GetMemoryIndexAsync

    [Fact]
    public async Task GetMemoryIndexAsync_Should_ReturnIndexContent()
    {
        // Arrange
        await _provider.SaveMemoryAsync(CreateTestMemory("idx-001", "索引测试"));
        await _provider.SaveMemoryAsync(CreateTestMemory("idx-002", "索引测试 B"));

        // Act
        var index = await _provider.GetMemoryIndexAsync(TestUserId);

        // Assert
        Assert.Contains("idx-001.md", index);
        Assert.Contains("idx-002.md", index);
    }

    [Fact]
    public async Task GetMemoryIndexAsync_Should_ReturnEmpty_WhenNoMemories()
    {
        // Act
        var index = await _provider.GetMemoryIndexAsync("empty_user");

        // Assert
        Assert.Equal("", index);
    }

    #endregion

    #region UserProfile

    [Fact]
    public async Task SaveUserProfileAsync_Should_CreateProfileFile()
    {
        // Arrange
        var profile = new UserProfile
        {
            UserId = TestUserId,
            DisplayName = "测试用户",
            Style = new CommunicationStyle { Language = "zh-CN", Verbosity = "concise" }
        };

        // Act
        await _provider.SaveUserProfileAsync(profile);

        // Assert
        var result = await _provider.GetUserProfileAsync(TestUserId);
        Assert.NotNull(result);
        Assert.Equal("测试用户", result.DisplayName);
        Assert.Equal("zh-CN", result.Style.Language);
    }

    [Fact]
    public async Task GetUserProfileAsync_Should_ReturnNull_WhenNotExists()
    {
        // Act
        var result = await _provider.GetUserProfileAsync("nonexistent_user");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Helpers

    private static MemoryEntry CreateTestMemory(
        string id,
        string content,
        MemoryType type = MemoryType.User,
        MemoryScope scope = MemoryScope.Private,
        string? project = null)
    {
        return new MemoryEntry
        {
            Id = id,
            UserId = TestUserId,
            Name = "Test Memory",
            Description = "Test description",
            Content = content,
            Type = type,
            Scope = scope,
            Tags = ["test"],
            Source = "unit_test",
            Project = project
        };
    }

    #endregion
}
