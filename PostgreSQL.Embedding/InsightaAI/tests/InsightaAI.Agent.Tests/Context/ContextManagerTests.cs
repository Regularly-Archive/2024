using InsightaAI.Agent.Context;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tests.Context;

/// <summary>
/// ContextManager 单元测试
/// </summary>
public class ContextManagerTests
{
    private readonly CharTokenEstimator _estimator = new();

    [Fact]
    public void EstimateTokens_Should_CountTextMessages()
    {
        // Arrange
        var manager = CreateManager();
        var messages = new List<Message>
        {
            Message.FromSystem("You are a helpful assistant."), // ~8 tokens
            Message.FromUser("Hello World"),                    // ~3 tokens
            Message.FromAssistant("Hi there!")                  // ~3 tokens
        };
        // Each message has +4 overhead

        // Act
        var result = manager.EstimateTokens(messages);

        // Assert
        Assert.True(result > 0);
        // 3 messages * 4 overhead = 12, plus content tokens
        Assert.True(result >= 12);
    }

    [Fact]
    public void EstimateTokens_Should_CountImageAs2000Tokens()
    {
        // Arrange
        var manager = CreateManager();
        var messages = new List<Message>
        {
            new Message
            {
                Role = MessageRole.User,
                Content = [new ImageBlock
                {
                    Source = new ImageSource
                    {
                        MediaType = "image/png",
                        Data = "base64data"
                    }
                }]
            }
        };

        // Act
        var result = manager.EstimateTokens(messages);

        // Assert
        // 4 overhead + 2000 for image
        Assert.Equal(2004, result);
    }

    [Fact]
    public async Task CompactIfNeededAsync_Should_ReturnNull_WhenDisabled()
    {
        // Arrange
        var budget = new ContextBudget { Enabled = false };
        var manager = CreateManager(budget: budget);
        var messages = new List<Message> { Message.FromUser("Hello") };

        // Act
        var result = await manager.CompactIfNeededAsync(messages);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CompactIfNeededAsync_Should_ReturnNull_WhenNoStrategies()
    {
        // Arrange
        var manager = CreateManager(strategies: []);
        var messages = new List<Message> { Message.FromUser("Hello") };

        // Act
        var result = await manager.CompactIfNeededAsync(messages);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CompactIfNeededAsync_Should_ReturnNull_WhenBelowThreshold()
    {
        // Arrange
        var budget = new ContextBudget
        {
            MaxContextTokens = 100_000,
            MicroCompactThreshold = 0.60
        };
        var strategy = new MockCompactStrategy("TestStrategy", 1, shouldCompact: false);
        var manager = CreateManager(budget: budget, strategies: [strategy]);
        var messages = new List<Message> { Message.FromUser("Short message") };

        // Act
        var result = await manager.CompactIfNeededAsync(messages);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CompactIfNeededAsync_Should_ExecuteStrategy_WhenShouldCompact()
    {
        // Arrange
        var strategy = new MockCompactStrategy("TestStrategy", 1, shouldCompact: true);
        var manager = CreateManager(strategies: [strategy]);
        var messages = new List<Message> { Message.FromUser("Test") };

        // Act
        var result = await manager.CompactIfNeededAsync(messages);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestStrategy", result.StrategyName);
        Assert.True(strategy.CompactCalled);
    }

    [Fact]
    public async Task CompactIfNeededAsync_Should_ExecuteHigherPriorityFirst()
    {
        // Arrange
        var lowPriority = new MockCompactStrategy("Low", 2, shouldCompact: true);
        var highPriority = new MockCompactStrategy("High", 1, shouldCompact: true);
        var manager = CreateManager(strategies: [lowPriority, highPriority]);
        var messages = new List<Message> { Message.FromUser("Test") };

        // Act
        var result = await manager.CompactIfNeededAsync(messages);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("High", result.StrategyName);
        Assert.True(highPriority.CompactCalled);
        Assert.False(lowPriority.CompactCalled);
    }

    [Fact]
    public async Task ForceCompactAsync_Should_ReturnNull_WhenDisabled()
    {
        // Arrange
        var budget = new ContextBudget { Enabled = false };
        var manager = CreateManager(budget: budget);
        var messages = new List<Message> { Message.FromUser("Hello") };

        // Act
        var result = await manager.ForceCompactAsync(messages, "auto");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ForceCompactAsync_Should_ExecuteMicroStrategy()
    {
        // Arrange
        var micro = new MockCompactStrategy("MicroCompact", 1, shouldCompact: false);
        var traditional = new MockCompactStrategy("TraditionalCompact", 2, shouldCompact: false);
        var manager = CreateManager(strategies: [micro, traditional]);
        var messages = new List<Message> { Message.FromUser("Test") };

        // Act
        var result = await manager.ForceCompactAsync(messages, "micro");

        // Assert
        Assert.NotNull(result);
        Assert.True(micro.CompactCalled);
        Assert.False(traditional.CompactCalled);
    }

    [Fact]
    public async Task ForceCompactAsync_Should_ExecuteTraditionalStrategy()
    {
        // Arrange
        var micro = new MockCompactStrategy("MicroCompact", 1, shouldCompact: false);
        var traditional = new MockCompactStrategy("TraditionalCompact", 2, shouldCompact: false);
        var manager = CreateManager(strategies: [micro, traditional]);
        var messages = new List<Message> { Message.FromUser("Test") };

        // Act
        var result = await manager.ForceCompactAsync(messages, "traditional");

        // Assert
        Assert.NotNull(result);
        Assert.False(micro.CompactCalled);
        Assert.True(traditional.CompactCalled);
    }

    [Fact]
    public async Task ForceCompactAsync_Should_ExecuteAutoStrategy()
    {
        // Arrange
        var micro = new MockCompactStrategy("MicroCompact", 1, shouldCompact: true);
        var traditional = new MockCompactStrategy("TraditionalCompact", 2, shouldCompact: false);
        var manager = CreateManager(strategies: [micro, traditional]);
        var messages = new List<Message> { Message.FromUser("Test") };

        // Act
        var result = await manager.ForceCompactAsync(messages, "auto");

        // Assert
        Assert.NotNull(result);
        Assert.True(micro.CompactCalled); // shouldCompact=true, so it's selected
    }

    [Fact]
    public async Task ForceCompactAsync_Should_FallBackToLastStrategy_WhenNoneShouldCompact()
    {
        // Arrange
        var micro = new MockCompactStrategy("MicroCompact", 1, shouldCompact: false);
        var traditional = new MockCompactStrategy("TraditionalCompact", 2, shouldCompact: false);
        var manager = CreateManager(strategies: [micro, traditional]);
        var messages = new List<Message> { Message.FromUser("Test") };

        // Act
        var result = await manager.ForceCompactAsync(messages, "auto");

        // Assert
        Assert.NotNull(result);
        Assert.True(traditional.CompactCalled); // Last strategy selected
    }

    [Fact]
    public async Task ForceCompactAsync_Should_ReturnNull_ForUnknownStrategy()
    {
        // Arrange
        var manager = CreateManager();
        var messages = new List<Message> { Message.FromUser("Test") };

        // Act
        var result = await manager.ForceCompactAsync(messages, "unknown");

        // Assert
        Assert.Null(result);
    }

    #region ModelContextWindows

    [Fact]
    public void ModelContextWindows_Should_ReturnKnownModelSize()
    {
        // Act
        var size = ModelContextWindows.GetContextWindowSize("gpt-4o");

        // Assert
        Assert.Equal(128_000, size);
    }

    [Fact]
    public void ModelContextWindows_Should_MatchByPrefix()
    {
        // Act
        var size = ModelContextWindows.GetContextWindowSize("gpt-4o-2024-08-06");

        // Assert
        Assert.Equal(128_000, size); // Matches "gpt-4o" prefix
    }

    [Fact]
    public void ModelContextWindows_Should_ReturnDefault_ForUnknownModel()
    {
        // Act
        var size = ModelContextWindows.GetContextWindowSize("unknown-model");

        // Assert
        Assert.Equal(128_000, size);
    }

    [Fact]
    public void ModelContextWindows_Should_ReturnCustomDefault_ForUnknownModel()
    {
        // Act
        var size = ModelContextWindows.GetContextWindowSize("unknown-model", 64_000);

        // Assert
        Assert.Equal(64_000, size);
    }

    [Fact]
    public void ModelContextWindows_Should_ReturnDefault_ForEmptyModel()
    {
        // Act
        var size = ModelContextWindows.GetContextWindowSize("");

        // Assert
        Assert.Equal(128_000, size);
    }

    [Fact]
    public void ModelContextWindows_Should_BeCaseInsensitive()
    {
        // Act
        var size1 = ModelContextWindows.GetContextWindowSize("GPT-4O");
        var size2 = ModelContextWindows.GetContextWindowSize("gpt-4o");

        // Assert
        Assert.Equal(size1, size2);
    }

    #endregion

    #region ContextBudget

    [Fact]
    public void ContextBudget_Should_CalculateTriggerTokens()
    {
        // Arrange
        var budget = new ContextBudget
        {
            MaxContextTokens = 100_000,
            MicroCompactThreshold = 0.60,
            TraditionalCompactThreshold = 0.75
        };

        // Assert
        Assert.Equal(60_000, budget.MicroCompactTriggerTokens);
        Assert.Equal(75_000, budget.TraditionalCompactTriggerTokens);
    }

    [Fact]
    public void ContextBudget_Should_CalculateAvailableInputTokens()
    {
        // Arrange
        var budget = new ContextBudget
        {
            MaxContextTokens = 100_000,
            ReservedForOutput = 16_384
        };

        // Assert
        Assert.Equal(83_616, budget.AvailableInputTokens);
    }

    #endregion

    #region Helpers

    private static ContextManager CreateManager(
        ITokenEstimator? estimator = null,
        ContextBudget? budget = null,
        IEnumerable<ICompactStrategy>? strategies = null)
    {
        return new ContextManager(
            estimator ?? new CharTokenEstimator(),
            budget ?? new ContextBudget(),
            strategies);
    }

    /// <summary>
    /// Mock 压缩策略，用于测试 ContextManager 的策略调度逻辑
    /// </summary>
    private class MockCompactStrategy : ICompactStrategy
    {
        private readonly bool _shouldCompact;

        public string Name { get; }
        public int Priority { get; }
        public bool CompactCalled { get; private set; }

        public MockCompactStrategy(string name, int priority, bool shouldCompact)
        {
            Name = name;
            Priority = priority;
            _shouldCompact = shouldCompact;
        }

        public bool ShouldCompact(IReadOnlyList<Message> messages, int estimatedTokens, ContextBudget budget)
        {
            return _shouldCompact;
        }

        public Task<CompactionResult> CompactAsync(
            List<Message> messages,
            ContextBudget budget,
            ITokenEstimator tokenEstimator,
            int preCompactTokens,
            CancellationToken cancellationToken = default)
        {
            CompactCalled = true;
            return Task.FromResult(new CompactionResult
            {
                StrategyName = Name,
                PreCompactTokens = preCompactTokens,
                PostCompactTokens = preCompactTokens / 2,
                PreCompactMessages = messages.Count,
                PostCompactMessages = messages.Count,
                RequestMessages = messages.ToArray()
            });
        }
    }

    #endregion
}
