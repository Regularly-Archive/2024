using System.Text.Json;
using InsightaAI.Agent.Context;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests.Context;

/// <summary>
/// MicroCompactStrategy 单元测试
/// </summary>
public class MicroCompactStrategyTests
{
    private readonly MicroCompactStrategy _strategy = new();
    private readonly CharTokenEstimator _estimator = new();
    private readonly ContextBudget _budget = new()
    {
        MaxContextTokens = 100_000,
        MicroCompactThreshold = 0.60,
        KeepRecentToolResults = 2
    };

    [Fact]
    public void Name_Should_BeMicroCompact()
    {
        Assert.Equal("MicroCompact", _strategy.Name);
    }

    [Fact]
    public void Priority_Should_Be1()
    {
        Assert.Equal(1, _strategy.Priority);
    }

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
    public void ShouldCompact_Should_ReturnFalse_WhenNoToolResults()
    {
        // Arrange
        var messages = new List<Message>
        {
            Message.FromSystem("System prompt"),
            Message.FromUser("User message"),
            Message.FromAssistant("Assistant response")
        };
        var estimatedTokens = _budget.MicroCompactTriggerTokens + 1000;

        // Act
        var result = _strategy.ShouldCompact(messages, estimatedTokens, _budget);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldCompact_Should_ReturnTrue_WhenHasOldToolResults()
    {
        // Arrange
        var messages = CreateMessagesWithToolResults(5); // 5 个工具结果，保留 2 个
        var estimatedTokens = _budget.MicroCompactTriggerTokens + 1000;

        // Act
        var result = _strategy.ShouldCompact(messages, estimatedTokens, _budget);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldCompact_Should_ReturnFalse_WhenToolResultsWithinKeepRecent()
    {
        // Arrange - 只有 2 个工具结果，刚好等于 KeepRecentToolResults
        var messages = CreateMessagesWithToolResults(2);
        var estimatedTokens = _budget.MicroCompactTriggerTokens + 1000;

        // Act
        var result = _strategy.ShouldCompact(messages, estimatedTokens, _budget);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CompactAsync_Should_TruncateOldToolResults()
    {
        // Arrange
        var messages = CreateMessagesWithToolResults(4); // 4 个工具结果
        // 使用与策略相同的 token 估算方式（包含消息开销）
        var preCompactTokens = EstimateMessagesTokens(messages);

        // Act
        var result = await _strategy.CompactAsync(messages, _budget, _estimator, preCompactTokens);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("MicroCompact", result.StrategyName);
        Assert.True(result.PostCompactTokens < result.PreCompactTokens,
            $"Expected post-compact tokens ({result.PostCompactTokens}) < pre-compact ({result.PreCompactTokens})");
    }

    [Fact]
    public async Task CompactAsync_Should_KeepRecentToolResults()
    {
        // Arrange
        var messages = CreateMessagesWithToolResults(4);
        var recentToolResultContent = "Recent tool result content";

        // 更新最后 2 个工具结果的内容（这些应该被保留）
        // 由于 Message.Content 是 init-only，我们需要替换整个 Message
        var toolResultIndices = messages
            .Select((m, i) => new { Message = m, Index = i })
            .Where(x => x.Message.Role == MessageRole.ToolResult)
            .Select(x => x.Index)
            .ToList();

        if (toolResultIndices.Count >= 2)
        {
            var lastIdx = toolResultIndices[^1];
            var originalMessage = messages[lastIdx];
            messages[lastIdx] = new Message
            {
                Role = originalMessage.Role,
                ToolCallId = originalMessage.ToolCallId,
                ToolName = originalMessage.ToolName,
                Content = [new TextBlock { Text = recentToolResultContent }]
            };
        }

        var preCompactTokens = 10000;

        // Act
        var result = await _strategy.CompactAsync(messages, _budget, _estimator, preCompactTokens);

        // Assert - 最近的工具结果应该保持不变
        var lastToolResult = messages
            .Where(m => m.Role == MessageRole.ToolResult)
            .LastOrDefault();

        if (lastToolResult != null)
        {
            var content = lastToolResult.Content.OfType<TextBlock>().FirstOrDefault()?.Text;
            Assert.Equal(recentToolResultContent, content);
        }
    }

    [Fact]
    public async Task CompactAsync_Should_PreserveMessageCount()
    {
        // Arrange
        var messages = CreateMessagesWithToolResults(4);
        var originalCount = messages.Count;
        var preCompactTokens = 10000;

        // Act
        var result = await _strategy.CompactAsync(messages, _budget, _estimator, preCompactTokens);

        // Assert - 消息数量不应改变（只是截断内容）
        Assert.Equal(originalCount, result.PostCompactMessages);
    }

    [Fact]
    public async Task CompactAsync_Should_HandleCancellation()
    {
        // Arrange
        var messages = CreateMessagesWithToolResults(4);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await _strategy.CompactAsync(messages, _budget, _estimator, 10000, cts.Token);
        });
    }

    [Fact]
    public async Task CompactAsync_Should_AddMetadataToTruncatedResults()
    {
        // Arrange
        var messages = CreateMessagesWithToolResults(4);
        var preCompactTokens = 10000;

        // Act
        await _strategy.CompactAsync(messages, _budget, _estimator, preCompactTokens);

        // Assert - 被截断的工具结果应该包含元数据
        var toolResults = messages
            .Where(m => m.Role == MessageRole.ToolResult)
            .ToList();

        // 前 2 个应该被截断（保留后 2 个）
        for (int i = 0; i < toolResults.Count - 2; i++)
        {
            var content = toolResults[i].Content.OfType<TextBlock>().FirstOrDefault()?.Text;
            Assert.NotNull(content);
            Assert.Contains("[Tool:", content);
        }
    }

    #region Truncation Strategies

    [Fact]
    public void BashTruncationStrategy_Should_TruncateLongOutput()
    {
        // Arrange
        var strategy = new BashTruncationStrategy();
        var lines = Enumerable.Range(1, 20).Select(i => $"Line {i}").ToArray();
        var content = string.Join("\n", lines);

        // Act
        var result = strategy.Truncate(content, "bash");

        // Assert
        Assert.Contains("[output truncated", result);
        Assert.Contains("Line 20", result); // 保留最后几行
        // 使用精确匹配避免 "Line 1" 被 "Line 16" 匹配
        Assert.DoesNotContain("Line 1\n", result); // 截断前面的行
    }

    [Fact]
    public void BashTruncationStrategy_Should_NotTruncateShortOutput()
    {
        // Arrange
        var strategy = new BashTruncationStrategy();
        var content = "Short output";

        // Act
        var result = strategy.Truncate(content, "bash");

        // Assert
        Assert.Equal("Short output", result);
    }

    [Fact]
    public void BashTruncationStrategy_Should_HandleEmptyOutput()
    {
        // Arrange
        var strategy = new BashTruncationStrategy();

        // Act
        var result = strategy.Truncate("", "bash");

        // Assert
        Assert.Equal("[empty output]", result);
    }

    [Fact]
    public void FileReadTruncationStrategy_Should_TruncateLargeFile()
    {
        // Arrange
        var strategy = new FileReadTruncationStrategy();
        var lines = Enumerable.Range(1, 100).Select(i => $"Line {i}").ToArray();
        var content = string.Join("\n", lines);

        // Act
        var result = strategy.Truncate(content, "read_file");

        // Assert
        Assert.Contains("[file content truncated", result);
        Assert.Contains("100 lines", result);
    }

    [Fact]
    public void GrepTruncationStrategy_Should_ShowMatchCount()
    {
        // Arrange
        var strategy = new GrepTruncationStrategy();
        var content = "match1\nmatch2\nmatch3\nmatch4\nmatch5";

        // Act
        var result = strategy.Truncate(content, "grep");

        // Assert
        Assert.Contains("[grep results truncated", result);
        Assert.Contains("5 matches", result);
    }

    [Fact]
    public void GlobTruncationStrategy_Should_ShowFileCount()
    {
        // Arrange
        var strategy = new GlobTruncationStrategy();
        var content = "file1.cs\nfile2.cs\nfile3.cs";

        // Act
        var result = strategy.Truncate(content, "glob");

        // Assert
        Assert.Contains("[glob results truncated", result);
        Assert.Contains("3 files", result);
    }

    [Fact]
    public void WebFetchTruncationStrategy_Should_ShowCharCount()
    {
        // Arrange
        var strategy = new WebFetchTruncationStrategy();
        var content = new string('A', 5000);

        // Act
        var result = strategy.Truncate(content, "web_fetch");

        // Assert
        Assert.Contains("[web content truncated", result);
        Assert.Contains("5000 chars", result);
    }

    [Fact]
    public void EditFileTruncationStrategy_Should_KeepShortContent()
    {
        // Arrange
        var strategy = new EditFileTruncationStrategy();
        var content = "File edited successfully";

        // Act
        var result = strategy.Truncate(content, "edit_file");

        // Assert
        Assert.Equal("File edited successfully", result);
    }

    [Fact]
    public void EditFileTruncationStrategy_Should_TruncateLongContent()
    {
        // Arrange
        var strategy = new EditFileTruncationStrategy();
        var content = new string('A', 200);

        // Act
        var result = strategy.Truncate(content, "edit_file");

        // Assert
        Assert.Equal("[edit completed]", result);
    }

    [Fact]
    public void WriteFileTruncationStrategy_Should_ShowByteCount()
    {
        // Arrange
        var strategy = new WriteFileTruncationStrategy();
        var content = new string('X', 1234);

        // Act
        var result = strategy.Truncate(content, "write_file");

        // Assert
        Assert.Contains("[file written", result);
        Assert.Contains("1234 bytes", result);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 估算消息列表的 token 数量（与 MicroCompactStrategy 使用相同逻辑）
    /// </summary>
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

    private static List<Message> CreateMessagesWithToolResults(int toolResultCount)
    {
        var messages = new List<Message>
        {
            Message.FromSystem("You are a helpful assistant.")
        };

        for (int i = 0; i < toolResultCount; i++)
        {
            var toolCallId = $"call_{i}";

            // Assistant message with tool call
            var assistantMsg = new Message
            {
                Role = MessageRole.Assistant,
                Content =
                [
                    new TextBlock { Text = $"I'll use tool {i}" },
                    new ToolCallBlock
                    {
                        Id = toolCallId,
                        Name = "bash",
                        Arguments = JsonSerializer.SerializeToElement(new { command = $"echo {i}" })
                    }
                ]
            };
            messages.Add(assistantMsg);

            // Tool result message - 使用 50 行内容以便截断后能显著减少 token
            var toolResultMsg = new Message
            {
                Role = MessageRole.ToolResult,
                ToolCallId = toolCallId,
                ToolName = "bash",
                Content = [new TextBlock { Text = $"Output for tool {i}\n" + string.Join("\n", Enumerable.Range(1, 50).Select(j => $"Line {j}: This is a longer line of output content for testing truncation")) }]
            };
            messages.Add(toolResultMsg);
        }

        // Final assistant message
        messages.Add(Message.FromAssistant("Here's the result."));

        return messages;
    }

    #endregion
}
