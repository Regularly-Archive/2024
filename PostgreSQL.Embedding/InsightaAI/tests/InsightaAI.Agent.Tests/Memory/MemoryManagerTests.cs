using InsightaAI.Agent.Memory;

namespace InsightaAI.Agent.Tests.Memory;

/// <summary>
/// MemoryManager 单元测试
/// 测试自动分类、标签提取、去重、过滤等逻辑
/// </summary>
public class MemoryManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileMemoryProvider _provider;
    private readonly MemoryManager _manager;
    private const string TestUserId = "mgr_test_user";

    public MemoryManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"insightai_mgr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _provider = new FileMemoryProvider(_tempDir);
        _manager = new MemoryManager(_provider);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { }
        }
    }

    #region Auto-Classification

    [Fact]
    public async Task SaveMemoryAsync_Should_ClassifyAsUser_WhenContainsPreference()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId, "我是全栈开发者，主要使用 C# 和 TypeScript");

        // Assert
        Assert.Equal(MemoryType.User, result.Type);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_ClassifyAsFeedback_WhenContainsDontLike()
    {
        // Act - 不要使用包含 "偏好" 的内容，因为它会先匹配 User 类型
        var result = await _manager.SaveMemoryAsync(TestUserId, "不要在代码中使用全局变量");

        // Assert
        Assert.Equal(MemoryType.Feedback, result.Type);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_ClassifyAsProject_WhenContainsProject()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId, "项目的目标是在 Q2 完成 MVP 发布");

        // Assert
        Assert.Equal(MemoryType.Project, result.Type);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_ClassifyAsReference_WhenContainsDocument()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId, "API 文档位于 https://docs.example.com");

        // Assert
        Assert.Equal(MemoryType.Reference, result.Type);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_UseProvidedType_WhenSpecified()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId,
            "这是一条自定义类型的记忆",
            type: MemoryType.Project);

        // Assert
        Assert.Equal(MemoryType.Project, result.Type);
    }

    #endregion

    #region Auto-Tag Extraction

    [Fact]
    public async Task SaveMemoryAsync_Should_ExtractLanguageTags()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId,
            "这个项目使用 Python 和 TypeScript 开发");

        // Assert
        Assert.Contains("python", result.Tags);
        Assert.Contains("typescript", result.Tags);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_ExtractTechnologyTags()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId,
            "我们使用 Docker 部署，Redis 做缓存");

        // Assert
        Assert.Contains("docker", result.Tags);
        Assert.Contains("redis", result.Tags);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_ExtractErrorTag()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId,
            "遇到了一个编译错误，需要修复");

        // Assert
        Assert.Contains("error", result.Tags);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_UseProvidedTags_WhenSpecified()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId,
            "自定义标签测试",
            tags: ["custom", "important"]);

        // Assert
        Assert.Contains("custom", result.Tags);
        Assert.Contains("important", result.Tags);
    }

    #endregion

    #region Sensitive Data Filtering

    [Fact]
    public async Task SaveMemoryAsync_Should_FilterApiKey()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId,
            "API Key: sk-1234567890abcdef");

        // Assert
        Assert.Equal("filtered", result.Source);
        Assert.True(result.Metadata.ContainsKey("filtered"));
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_FilterPassword()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId,
            "password = mysecretpassword123");

        // Assert
        Assert.Equal("filtered", result.Source);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_NotFilterNormalContent()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId,
            "用户偏好深色主题");

        // Assert
        Assert.NotEqual("filtered", result.Source);
    }

    #endregion

    #region Deduplication

    [Fact]
    public async Task SaveMemoryAsync_Should_UpdateExisting_WhenContentSimilar()
    {
        // Arrange
        await _manager.SaveMemoryAsync(TestUserId, "用户的真实姓名是张三");

        // Act - 保存语义相同的记忆
        var result = await _manager.SaveMemoryAsync(TestUserId, "用户的真实姓名是张三");

        // Assert - 应该更新而非创建新记忆
        var memories = await _provider.ListMemoriesAsync(TestUserId);
        Assert.Single(memories);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_CreateNew_WhenContentDifferent()
    {
        // Arrange
        await _manager.SaveMemoryAsync(TestUserId, "用户喜欢使用 C#");

        // Act
        await _manager.SaveMemoryAsync(TestUserId, "项目使用 PostgreSQL 数据库");

        // Assert
        var memories = await _provider.ListMemoriesAsync(TestUserId);
        Assert.Equal(2, memories.Count);
    }

    #endregion

    #region Scope Determination

    [Fact]
    public async Task SaveMemoryAsync_Should_SetPrivateScope_ForUserType()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId,
            "我是开发者",
            type: MemoryType.User);

        // Assert
        Assert.Equal(MemoryScope.Private, result.Scope);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_SetTeamScope_ForProjectWithProjectId()
    {
        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId,
            "项目决策：使用微服务架构",
            type: MemoryType.Project,
            project: "my-project");

        // Assert
        Assert.Equal(MemoryScope.Team, result.Scope);
        Assert.Equal("my-project", result.Project);
    }

    #endregion

    #region Name and Description Generation

    [Fact]
    public async Task SaveMemoryAsync_Should_GenerateNameWithTypePrefix()
    {
        // Act
        var userResult = await _manager.SaveMemoryAsync(TestUserId, "我是全栈开发者");
        var feedbackResult = await _manager.SaveMemoryAsync(TestUserId, "不要使用全局变量");
        var projectResult = await _manager.SaveMemoryAsync(TestUserId, "项目截止日期是下个月");

        // Assert
        Assert.StartsWith("User:", userResult.Name);
        Assert.StartsWith("Feedback:", feedbackResult.Name);
        Assert.StartsWith("Project:", projectResult.Name);
    }

    [Fact]
    public async Task SaveMemoryAsync_Should_TruncateLongContent_ForDescription()
    {
        // Arrange
        var longContent = new string('A', 200);

        // Act
        var result = await _manager.SaveMemoryAsync(TestUserId, longContent);

        // Assert
        Assert.True(result.Description.Length <= 110); // 100 + "..."
        Assert.EndsWith("...", result.Description);
    }

    #endregion

    #region SearchRelevantMemoriesAsync

    [Fact]
    public async Task SearchRelevantMemoriesAsync_Should_ReturnRelevantMemories()
    {
        // Arrange
        await _manager.SaveMemoryAsync(TestUserId, "用户喜欢使用 C# 开发");
        await _manager.SaveMemoryAsync(TestUserId, "项目使用 Redis 缓存");
        await _manager.SaveMemoryAsync(TestUserId, "用户偏好深色主题");

        // Act
        var results = await _manager.SearchRelevantMemoriesAsync(TestUserId, "C# 开发");

        // Assert
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Content.Contains("C#"));
    }

    [Fact]
    public async Task SearchRelevantMemoriesAsync_Should_RespectMaxResults()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            await _manager.SaveMemoryAsync(TestUserId, $"记忆条目 {i} 包含测试内容");
        }

        // Act
        var results = await _manager.SearchRelevantMemoriesAsync(TestUserId, "测试内容", maxResults: 3);

        // Assert
        Assert.True(results.Count <= 3);
    }

    #endregion

    #region User Context

    [Fact]
    public async Task GetUserContextAsync_Should_IncludeUserProfile()
    {
        // Arrange
        var profile = new UserProfile
        {
            UserId = TestUserId,
            DisplayName = "测试用户",
            Style = new CommunicationStyle { Language = "zh-CN" }
        };
        await _provider.SaveUserProfileAsync(profile);

        // Act
        var context = await _manager.GetUserContextAsync(TestUserId);

        // Assert
        Assert.Contains("测试用户", context);
        Assert.Contains("zh-CN", context);
    }

    [Fact]
    public async Task GetUserContextAsync_Should_IncludeMemoryIndex()
    {
        // Arrange
        await _manager.SaveMemoryAsync(TestUserId, "用户喜欢 C#");

        // Act
        var context = await _manager.GetUserContextAsync(TestUserId);

        // Assert
        Assert.NotEmpty(context);
    }

    #endregion

    #region User Profile

    [Fact]
    public async Task UpdateUserProfileAsync_Should_CreateProfile_WhenNotExists()
    {
        // Act
        await _manager.UpdateUserProfileAsync(TestUserId, new Dictionary<string, string>
        {
            ["language"] = "zh-CN",
            ["theme"] = "dark"
        });

        // Assert
        var profile = await _manager.GetUserProfileAsync(TestUserId);
        Assert.NotNull(profile);
        Assert.Equal("zh-CN", profile.Preferences["language"]);
        Assert.Equal("dark", profile.Preferences["theme"]);
    }

    [Fact]
    public async Task UpdateUserProfileAsync_Should_MergePreferences()
    {
        // Arrange
        await _manager.UpdateUserProfileAsync(TestUserId, new Dictionary<string, string>
        {
            ["language"] = "zh-CN"
        });

        // Act
        await _manager.UpdateUserProfileAsync(TestUserId, new Dictionary<string, string>
        {
            ["theme"] = "dark"
        });

        // Assert
        var profile = await _manager.GetUserProfileAsync(TestUserId);
        Assert.NotNull(profile);
        Assert.Equal("zh-CN", profile.Preferences["language"]);
        Assert.Equal("dark", profile.Preferences["theme"]);
    }

    #endregion
}
