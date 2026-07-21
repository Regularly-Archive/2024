using System.Text.Json;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Compaction;
using InsightaAI.Agent.Memory;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests.Context;

/// <summary>
/// SessionMemoryCompactStrategy 单元测试
/// </summary>
public class SessionMemoryCompactStrategyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SessionMemoryHook _sessionMemoryHook;
    private readonly SessionMemoryCompactStrategy _strategy;
    private readonly CharTokenEstimator _estimator = new();
    private readonly ContextBudget _budget = new()
    {
        MaxContextTokens = 100_000,
        MicroCompactThreshold = 0.60,
        SessionCompactThreshold = 0.65,
        KeepRecentRounds = 2
    };

    public SessionMemoryCompactStrategyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"insightai_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // 创建 SessionMemoryHook 使用临时目录
        _sessionMemoryHook = new SessionMemoryHook("test-session", "test-user");
        _strategy = new SessionMemoryCompactStrategy(_sessionMemoryHook);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    #region Basic Properties

    [Fact]
    public void Name_Should_BeSessionMemoryCompact()
    {
        Assert.Equal("SessionMemoryCompact", _strategy.Name);
    }

    [Fact]
    public void Priority_Should_Be2()
    {
        Assert.Equal(2, _strategy.Priority);
    }

    #endregion

    #region ShouldCompact

    [Fact]
    public void ShouldCompact_Should_ReturnFalse_WhenBelowThreshold()
    {
        // Arrange
        var messages = new List<Message>
        {
            Message.FromSystem("You are a helpful assistant."),
            Message.FromUser("Hello")
        };
        var estimatedTokens = 100; // 远低于阈值

        // Act
        var result = _strategy.ShouldCompact(messages, estimatedTokens, _budget);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldCompact_Should_ReturnFalse_WhenNoSessionMemory()
    {
        // Arrange - 没有会话记忆文件
        var messages = new List<Message>
        {
            Message.FromSystem("You are a helpful assistant."),
            Message.FromUser("Hello")
        };
        var estimatedTokens = (_budget.SessionCompactTriggerTokens +
            _budget.TraditionalCompactTriggerTokens) / 2; // 位于 Session 压缩区间，但没有会话记忆

        // Act
        var result = _strategy.ShouldCompact(messages, estimatedTokens, _budget);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldCompact_Should_ReturnTrue_WhenAboveThresholdAndHasMemory()
    {
        // Arrange - 先写入会话记忆
        var sessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta", "memories", "sessions", "test-session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "MEMORY.md"), "## Round 1\n- User asked about API");

        var messages = new List<Message>
        {
            Message.FromSystem("You are a helpful assistant."),
            Message.FromUser("Hello")
        };
        var estimatedTokens = (_budget.SessionCompactTriggerTokens +
            _budget.TraditionalCompactTriggerTokens) / 2; // 位于 Session 压缩区间

        try
        {
            // Act
            var result = _strategy.ShouldCompact(messages, estimatedTokens, _budget);

            // Assert
            Assert.True(result);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(sessionDir))
                Directory.Delete(sessionDir, true);
        }
    }

    #endregion

    #region CompactAsync

    [Fact]
    public async Task CompactAsync_Should_UseSessionMemoryAsSummary()
    {
        // Arrange - 先写入会话记忆
        var sessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta", "memories", "sessions", "test-session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "MEMORY.md"),
            "## Round 1\n- User asked about REST API\n- Decided to use FastAPI");

        var messages = CreateMessagesWithRounds(15);
        var preCompactTokens = EstimateMessagesTokens(messages);

        try
        {
            // Act
            var result = await _strategy.CompactAsync(messages, _budget, _estimator, preCompactTokens);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("SessionMemoryCompact", result.StrategyName);
            Assert.True(result.PostCompactTokens < result.PreCompactTokens,
                $"Expected post-compact tokens ({result.PostCompactTokens}) < pre-compact ({result.PreCompactTokens})");

            // 验证包含会话记忆内容（边界标记作为 Assistant 消息添加）
            var boundaryMessage = messages.First(m => m.Role == MessageRole.Assistant && m.GetTextContent().Contains("FastAPI"));
            Assert.NotNull(boundaryMessage);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(sessionDir))
                Directory.Delete(sessionDir, true);
        }
    }

    [Fact]
    public async Task CompactAsync_Should_KeepRecentMessages()
    {
        // Arrange - 先写入会话记忆
        var sessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta", "memories", "sessions", "test-session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "MEMORY.md"), "## Summary\nPrevious context");

        var messages = CreateMessagesWithRounds(5); // 5 轮对话
        var preCompactTokens = EstimateMessagesTokens(messages);

        try
        {
            // Act
            var result = await _strategy.CompactAsync(messages, _budget, _estimator, preCompactTokens);

            // Assert - 保留最近 2 轮（4 条消息：2 user + 2 assistant）+ 系统消息 + 边界标记
            Assert.True(messages.Count <= 7, $"Expected <= 7 messages, got {messages.Count}");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(sessionDir))
                Directory.Delete(sessionDir, true);
        }
    }

    [Fact]
    public async Task CompactAsync_Should_ContainCompactedMarker()
    {
        // Arrange - 先写入会话记忆
        var sessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta", "memories", "sessions", "test-session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "MEMORY.md"), "## Summary\nTest memory");

        var messages = CreateMessagesWithRounds(3);
        var preCompactTokens = EstimateMessagesTokens(messages);

        try
        {
            // Act
            var result = await _strategy.CompactAsync(messages, _budget, _estimator, preCompactTokens);

            // Assert - 验证边界标记（边界标记作为 Assistant 消息添加）
            var boundaryMessage = messages.FirstOrDefault(m =>
                m.Role == MessageRole.Assistant &&
                m.GetTextContent().Contains("SessionMemoryCompact"));
            Assert.NotNull(boundaryMessage);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(sessionDir))
                Directory.Delete(sessionDir, true);
        }
    }

    #endregion

    #region Error Handling

    [Fact]
    public async Task CompactAsync_Should_ReturnNoCompaction_WhenMemoryIsEmpty()
    {
        // Arrange - 创建空的会话记忆文件
        var sessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta", "memories", "sessions", "test-session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "MEMORY.md"), "");

        var messages = CreateMessagesWithRounds(3);
        var preCompactTokens = EstimateMessagesTokens(messages);
        var originalMessageCount = messages.Count;

        try
        {
            // Act
            var result = await _strategy.CompactAsync(messages, _budget, _estimator, preCompactTokens);

            // Assert - 应该返回不压缩的结果
            Assert.NotNull(result);
            Assert.Equal(preCompactTokens, result.PostCompactTokens);
            Assert.Equal(originalMessageCount, result.PostCompactMessages);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(sessionDir))
                Directory.Delete(sessionDir, true);
        }
    }

    [Fact]
    public async Task CompactAsync_Should_ReturnNoCompaction_WhenFileReadFails()
    {
        // Arrange - 创建会话记忆文件后删除目录（模拟文件读取失败）
        var sessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta", "memories", "sessions", "test-session");
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "MEMORY.md"), "Some memory");

        // 先触发 ShouldCompact 返回 true
        var messages = CreateMessagesWithRounds(3);
        var preCompactTokens = EstimateMessagesTokens(messages);
        var originalMessageCount = messages.Count;

        // 删除目录以模拟文件读取失败
        Directory.Delete(sessionDir, true);

        // Act
        var result = await _strategy.CompactAsync(messages, _budget, _estimator, preCompactTokens);

        // Assert - 应该返回不压缩的结果（优雅降级）
        Assert.NotNull(result);
        Assert.Equal(preCompactTokens, result.PostCompactTokens);
        Assert.Equal(originalMessageCount, result.PostCompactMessages);
    }

    #endregion

    #region Helpers

    private int EstimateMessagesTokens(List<Message> messages)
    {
        int total = 0;
        foreach (var message in messages)
        {
            total += 4; // 消息开销
            foreach (var block in message.Content)
            {
                if (block is TextBlock textBlock)
                    total += _estimator.EstimateTokens(textBlock.Text);
                else if (block is ThinkingBlock thinkingBlock)
                    total += _estimator.EstimateTokens(thinkingBlock.Thinking);
                else if (block is ImageBlock)
                    total += 2000;
                else if (block is ToolCallBlock toolCall)
                {
                    total += _estimator.EstimateTokens(toolCall.Name);
                    total += _estimator.EstimateTokens(toolCall.Arguments.GetRawText());
                }
            }
        }
        return total;
    }

    private static List<Message> CreateMessagesWithRounds(int roundCount)
    {
        var messages = new List<Message>
        {
            Message.FromSystem("You are a helpful assistant.")
        };

        for (int i = 0; i < roundCount; i++)
        {
            messages.Add(Message.FromUser($"User message {i}: This is a longer message to simulate real conversation with more tokens."));
            messages.Add(Message.FromAssistant($"Assistant response {i}: Here is a detailed response with code examples and explanations."));
        }

        return messages;
    }

    #endregion
}
