using InsightaAI.Agent.Context;

namespace InsightaAI.Agent.Tests.Context;

/// <summary>
/// CharTokenEstimator 单元测试
/// </summary>
public class CharTokenEstimatorTests
{
    private readonly CharTokenEstimator _estimator = new();

    [Fact]
    public void EstimateTokens_Should_ReturnZero_ForEmptyString()
    {
        // Act
        var result = _estimator.EstimateTokens("");

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void EstimateTokens_Should_ReturnZero_ForNull()
    {
        // Act
        var result = _estimator.EstimateTokens((string)null!);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void EstimateTokens_Should_EstimateEnglishText()
    {
        // Arrange - 4 chars/token for English
        var text = "Hello World"; // 11 chars (including space)

        // Act
        var result = _estimator.EstimateTokens(text);

        // Assert - ceil(11 / 4.0) = 3
        Assert.Equal(3, result);
    }

    [Fact]
    public void EstimateTokens_Should_EstimateChineseText()
    {
        // Arrange - 1.5 chars/token for CJK
        var text = "你好世界"; // 4 CJK chars

        // Act
        var result = _estimator.EstimateTokens(text);

        // Assert - ceil(4 / 1.5) = ceil(2.67) = 3
        Assert.Equal(3, result);
    }

    [Fact]
    public void EstimateTokens_Should_EstimateMixedText()
    {
        // Arrange - Mixed CJK and Latin
        var text = "Hello 你好 World 世界";
        // Latin: "Hello ", " World " = 12 chars -> 12/4 = 3
        // CJK: "你好", "世界" = 4 chars -> 4/1.5 = 2.67
        // Total: ceil(3 + 2.67) = ceil(5.67) = 6

        // Act
        var result = _estimator.EstimateTokens(text);

        // Assert
        Assert.Equal(6, result);
    }

    [Fact]
    public void EstimateTokens_Should_HandleLongEnglishText()
    {
        // Arrange
        var text = new string('A', 100); // 100 Latin chars

        // Act
        var result = _estimator.EstimateTokens(text);

        // Assert - ceil(100 / 4.0) = 25
        Assert.Equal(25, result);
    }

    [Fact]
    public void EstimateTokens_Should_HandleLongChineseText()
    {
        // Arrange
        var text = new string('中', 100); // 100 CJK chars

        // Act
        var result = _estimator.EstimateTokens(text);

        // Assert - ceil(100 / 1.5) = ceil(66.67) = 67
        Assert.Equal(67, result);
    }

    [Fact]
    public void EstimateTokens_Should_CountCJKExtensionA()
    {
        // Arrange - CJK Extension A: U+3400 - U+4DBF
        var text = "\u3400\u3401\u3402"; // 3 CJK Extension A chars

        // Act
        var result = _estimator.EstimateTokens(text);

        // Assert - ceil(3 / 1.5) = 2
        Assert.Equal(2, result);
    }

    [Fact]
    public void EstimateTokens_Should_CountCJKCompatibility()
    {
        // Arrange - CJK Compatibility: U+F900 - U+FAFF
        var text = "\uF900\uF901"; // 2 CJK Compatibility chars

        // Act
        var result = _estimator.EstimateTokens(text);

        // Assert - ceil(2 / 1.5) = ceil(1.33) = 2
        Assert.Equal(2, result);
    }

    [Fact]
    public void EstimateTokens_Multiple_Should_SumTokens()
    {
        // Arrange
        var texts = new[] { "Hello", "你好", "World" };
        // "Hello" = 5 chars -> ceil(5/4) = 2
        // "你好" = 2 CJK -> ceil(2/1.5) = 2
        // "World" = 5 chars -> ceil(5/4) = 2
        // Total = 6

        // Act
        var result = _estimator.EstimateTokens(texts);

        // Assert
        Assert.Equal(6, result);
    }

    [Fact]
    public void EstimateTokens_Multiple_Should_HandleEmptyArray()
    {
        // Act
        var result = _estimator.EstimateTokens(Array.Empty<string>());

        // Assert
        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData("a", 1)]      // 1 Latin char -> ceil(1/4) = 1
    [InlineData("ab", 1)]     // 2 Latin chars -> ceil(2/4) = 1
    [InlineData("abcd", 1)]   // 4 Latin chars -> ceil(4/4) = 1
    [InlineData("abcde", 2)]  // 5 Latin chars -> ceil(5/4) = 2
    [InlineData("你", 1)]     // 1 CJK char -> ceil(1/1.5) = 1
    [InlineData("你好", 2)]   // 2 CJK chars -> ceil(2/1.5) = 2
    [InlineData("你好世", 2)] // 3 CJK chars -> ceil(3/1.5) = 2
    [InlineData("你好世界", 3)] // 4 CJK chars -> ceil(4/1.5) = 3
    public void EstimateTokens_Should_MatchExpectedValues(string text, int expectedTokens)
    {
        // Act
        var result = _estimator.EstimateTokens(text);

        // Assert
        Assert.Equal(expectedTokens, result);
    }
}
