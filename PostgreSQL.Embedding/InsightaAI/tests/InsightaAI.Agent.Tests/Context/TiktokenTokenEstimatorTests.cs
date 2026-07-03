using InsightaAI.Agent.Context;

namespace InsightaAI.Agent.Tests.Context;

/// <summary>
/// TiktokenTokenEstimator 单元测试
/// </summary>
public class TiktokenTokenEstimatorTests
{
    private readonly TiktokenTokenEstimator _estimator = new("gpt-4o");

    [Fact]
    public void EstimateTokens_Should_ReturnZero_ForEmptyString()
    {
        var result = _estimator.EstimateTokens("");
        Assert.Equal(0, result);
    }

    [Fact]
    public void EstimateTokens_Should_ReturnZero_ForNull()
    {
        var result = _estimator.EstimateTokens((string)null!);
        Assert.Equal(0, result);
    }

    [Fact]
    public void EstimateTokens_Should_EstimateEnglishText()
    {
        // "Hello World" → 4 tokens (tiktoken: Hello, space, World → 拆分可能不同，验证 > 0 即可)
        var result = _estimator.EstimateTokens("Hello World");
        Assert.True(result > 0 && result < 10, $"Expected reasonable count, got {result}");
    }

    [Fact]
    public void EstimateTokens_Should_EstimateChineseText()
    {
        // 中文 token 数取决于分词，验证合理范围即可
        var result = _estimator.EstimateTokens("你好世界");
        Assert.True(result >= 2 && result <= 12, $"Expected 2-12, got {result}");
    }

    [Fact]
    public void EstimateTokens_Should_EstimateMixedText()
    {
        var result = _estimator.EstimateTokens("Hello 你好 World 世界");
        Assert.True(result > 0, $"Expected positive count, got {result}");
    }

    [Fact]
    public void EstimateTokens_Should_BeMoreThanCharEstimator_ForChinese()
    {
        // Tiktoken 对中文的 token 数通常多于 CharTokenEstimator
        var charEstimator = new CharTokenEstimator();
        var text = new string('中', 50);

        var tiktokenCount = _estimator.EstimateTokens(text);
        var charCount = charEstimator.EstimateTokens(text);

        Assert.True(tiktokenCount > charCount,
            $"Tiktoken ({tiktokenCount}) should be more than Char ({charCount}) for CJK text");
    }

    [Fact]
    public void EstimateTokens_Should_SumMultipleTexts()
    {
        var texts = new[] { "Hello", "World", "你好" };
        var sum = _estimator.EstimateTokens(texts);
        var individual = texts.Sum(t => _estimator.EstimateTokens(t));

        Assert.Equal(individual, sum);
    }

    [Fact]
    public void EstimateTokens_Should_IncreaseWithTextLength()
    {
        var shortText = "Hello";
        var longText = new string('A', 1000);

        Assert.True(_estimator.EstimateTokens(longText) > _estimator.EstimateTokens(shortText));
    }

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("gpt-4")]
    [InlineData("gpt-3.5-turbo")]
    public void Constructor_Should_AcceptValidModelNames(string modelName)
    {
        var estimator = new TiktokenTokenEstimator(modelName);
        Assert.True(estimator.EstimateTokens("test") > 0);
    }
}
