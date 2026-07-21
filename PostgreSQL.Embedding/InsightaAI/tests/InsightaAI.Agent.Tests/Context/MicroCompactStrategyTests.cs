using System.Text.Json;
using InsightaAI.Agent.Context;
using InsightaAI.Agent.Context.Compaction;
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
    public async Task CompactAsync_Should_MarkTruncatedResultsAsPreview()
    {
        // Arrange
        var messages = CreateMessagesWithToolResults(4);
        var preCompactTokens = 10000;

        // Act
        await _strategy.CompactAsync(messages, _budget, _estimator, preCompactTokens);

        // Assert - 被截断的工具结果应该进入 Preview 状态
        var toolResults = messages
            .Where(m => m.Role == MessageRole.ToolResult)
            .ToList();

        // 前 2 个应该被截断（保留后 2 个）
        for (int i = 0; i < toolResults.Count - 2; i++)
        {
            Assert.Equal(ToolResultRetentionLevel.Preview,
                toolResults[i].ToolResultState?.RetentionLevel);
        }
    }

    [Fact]
    public async Task CompactAsync_Should_AdvancePreviewToPlaceholder()
    {
        var budget = _budget with { KeepRecentToolResults = 0 };
        var messages = CreateMessagesWithToolResults(1);
        var resultIndex = messages.FindIndex(message => message.Role == MessageRole.ToolResult);
        messages[resultIndex] = messages[resultIndex] with
        {
            ToolResultState = new ToolResultState
            {
                RetentionLevel = ToolResultRetentionLevel.Preview,
                OriginalLength = 10_000,
                CanReplay = true,
                MinimumLevel = ToolResultRetentionLevel.Removed
            }
        };

        await _strategy.CompactAsync(
            messages, budget, _estimator, budget.SessionCompactTriggerTokens + 1);

        var compacted = messages.Single(message => message.Role == MessageRole.ToolResult);
        Assert.Equal(ToolResultRetentionLevel.Placeholder, compacted.ToolResultState?.RetentionLevel);
        Assert.Contains("omitted", compacted.GetTextContent(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompactAsync_Should_NotAdvanceToPreview_WhenPreviewHasNoBenefit()
    {
        var budget = _budget with { KeepRecentToolResults = 0 };
        var messages = CreateMessagesWithToolResults(1);
        var resultIndex = messages.FindIndex(message => message.Role == MessageRole.ToolResult);
        messages[resultIndex] = messages[resultIndex] with
        {
            Content = [new TextBlock { Text = "short result" }]
        };

        await _strategy.CompactAsync(
            messages, budget, _estimator, budget.MicroCompactTriggerTokens + 1);

        var result = messages.Single(message => message.Role == MessageRole.ToolResult);
        Assert.Null(result.ToolResultState);
        Assert.Equal("short result", result.GetTextContent());
    }

    [Fact]
    public async Task CompactAsync_Should_SkipNoBenefitPreviewAndUsePlaceholder_WhenAllowed()
    {
        var budget = _budget with { KeepRecentToolResults = 0 };
        var messages = CreateMessagesWithToolResults(1);
        var resultIndex = messages.FindIndex(message => message.Role == MessageRole.ToolResult);
        var mediumResult = string.Join("\n",
            Enumerable.Range(1, 20).Select(i => $"line {i}: reusable output data"));
        messages[resultIndex] = messages[resultIndex] with
        {
            Content = [new TextBlock { Text = mediumResult }]
        };

        await _strategy.CompactAsync(
            messages, budget, _estimator, budget.SessionCompactTriggerTokens + 1);

        var result = messages.Single(message => message.Role == MessageRole.ToolResult);
        Assert.Equal(ToolResultRetentionLevel.Placeholder, result.ToolResultState?.RetentionLevel);
        Assert.Contains("omitted", result.GetTextContent(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompactAsync_Should_RemoveToolCallAndResultAsPair()
    {
        var budget = _budget with { KeepRecentToolResults = 0 };
        var messages = CreateMessagesWithToolResults(1);
        var resultIndex = messages.FindIndex(message => message.Role == MessageRole.ToolResult);
        messages[resultIndex] = messages[resultIndex] with
        {
            ToolResultState = new ToolResultState
            {
                RetentionLevel = ToolResultRetentionLevel.Placeholder,
                OriginalLength = 10_000,
                CanReplay = true,
                MinimumLevel = ToolResultRetentionLevel.Removed
            }
        };

        await _strategy.CompactAsync(
            messages, budget, _estimator, budget.TraditionalCompactTriggerTokens + 1);

        Assert.DoesNotContain(messages, message => message.Role == MessageRole.ToolResult);
        Assert.DoesNotContain(messages.SelectMany(message => message.Content), block => block is ToolCallBlock);
    }

    [Fact]
    public async Task CompactAsync_Should_PreserveSiblingParallelToolCall_WhenRemovingOnePair()
    {
        var budget = _budget with { KeepRecentToolResults = 1 };
        var arguments = JsonSerializer.SerializeToElement(new { path = "test.txt" });
        var messages = new List<Message>
        {
            Message.FromSystem("You are a helpful assistant."),
            new()
            {
                Role = MessageRole.Assistant,
                Content =
                [
                    new TextBlock { Text = "Running two tools." },
                    new ToolCallBlock { Id = "call_a", Name = "read_file", Arguments = arguments },
                    new ToolCallBlock { Id = "call_b", Name = "read_file", Arguments = arguments }
                ]
            },
            new()
            {
                Role = MessageRole.ToolResult,
                ToolCallId = "call_a",
                ToolName = "read_file",
                Content = [new TextBlock { Text = "[Previous result omitted.]" }],
                ToolResultState = new ToolResultState
                {
                    RetentionLevel = ToolResultRetentionLevel.Placeholder,
                    OriginalLength = 10_000,
                    CanReplay = true,
                    MinimumLevel = ToolResultRetentionLevel.Removed
                }
            },
            new()
            {
                Role = MessageRole.ToolResult,
                ToolCallId = "call_b",
                ToolName = "read_file",
                Content = [new TextBlock { Text = "result b" }]
            }
        };

        await _strategy.CompactAsync(
            messages, budget, _estimator, budget.TraditionalCompactTriggerTokens + 1);

        var assistant = Assert.Single(messages, message => message.Role == MessageRole.Assistant);
        Assert.Equal("Running two tools.", Assert.Single(assistant.Content.OfType<TextBlock>()).Text);
        Assert.Equal("call_b", Assert.Single(assistant.Content.OfType<ToolCallBlock>()).Id);
        Assert.DoesNotContain(messages, message => message.ToolCallId == "call_a");
        Assert.Contains(messages, message => message.Role == MessageRole.ToolResult && message.ToolCallId == "call_b");
    }



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
            // Timestamp 设为 10 分钟前，确保超过 MicroCompactStrategy 的 ToolResultTTL (5 分钟)
            var toolResultMsg = new Message
            {
                Role = MessageRole.ToolResult,
                ToolCallId = toolCallId,
                ToolName = "bash",
                Content = [new TextBlock { Text = $"Output for tool {i}\n" + string.Join("\n", Enumerable.Range(1, 50).Select(j => $"Line {j}: This is a longer line of output content for testing truncation")) }],
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10)
            };
            messages.Add(toolResultMsg);
        }

        // Final assistant message
        messages.Add(Message.FromAssistant("Here's the result."));

        return messages;
    }

    #endregion
}
