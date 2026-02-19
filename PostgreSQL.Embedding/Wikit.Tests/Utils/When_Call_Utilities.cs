using PostgreSQL.Embedding.Common;
using Shouldly;
using Xunit;

namespace Wikit.Tests.Utils
{
    /// <summary>
    /// Tests for LlmServiceProvider enum
    /// </summary>
    public class When_Call_LlmServiceProvider
    {
        [Theory]
        [InlineData(LlmServiceProvider.OpenAI, 0)]
        [InlineData(LlmServiceProvider.LLama, 1)]
        [InlineData(LlmServiceProvider.Ollama, 2)]
        [InlineData(LlmServiceProvider.HuggingFace, 3)]
        [InlineData(LlmServiceProvider.Zhipu, 4)]
        [InlineData(LlmServiceProvider.DeepSeek, 5)]
        [InlineData(LlmServiceProvider.OpenRouter, 6)]
        [InlineData(LlmServiceProvider.SiliconFlow, 7)]
        [InlineData(LlmServiceProvider.MiniMax, 8)]
        [InlineData(LlmServiceProvider.LingYi, 9)]
        [InlineData(LlmServiceProvider.Anthropic, 10)]
        [InlineData(LlmServiceProvider.Google, 11)]
        public void It_Should_Have_Correct_Values(LlmServiceProvider provider, int expectedValue)
        {
            // Assert
            ((int)provider).ShouldBe(expectedValue);
        }
    }

    /// <summary>
    /// Tests for LlmApiFormat enum
    /// </summary>
    public class When_Call_LlmApiFormat
    {
        [Theory]
        [InlineData(LlmApiFormat.OpenAI, 0)]
        [InlineData(LlmApiFormat.Anthropic, 1)]
        public void It_Should_Have_Correct_Values(LlmApiFormat format, int expectedValue)
        {
            // Assert
            ((int)format).ShouldBe(expectedValue);
        }
    }

    /// <summary>
    /// Tests for ModelType enum
    /// </summary>
    public class When_Call_ModelType
    {
        [Theory]
        [InlineData(ModelType.TextGeneration, 0)]
        [InlineData(ModelType.TextEmbedding, 1)]
        public void It_Should_Have_Correct_Values(ModelType type, int expectedValue)
        {
            // Assert
            ((int)type).ShouldBe(expectedValue);
        }
    }

    /// <summary>
    /// Tests for Constants class
    /// </summary>
    public class When_Call_Constants
    {
        [Fact]
        public void It_Should_NotBeNullOrEmpty_For_HttpRequestHeader_Provider()
        {
            // Assert
            Constants.HttpRequestHeader_Provider.ShouldNotBeNullOrEmpty();
        }

        [Fact]
        public void It_Should_Be_X_Llm_Provider_For_HttpRequestHeader_Provider()
        {
            // Assert
            Constants.HttpRequestHeader_Provider.ShouldBe("x-wikit-llm-provider");
        }
    }

    /// <summary>
    /// Tests for RetrievalType enum
    /// </summary>
    public class When_Call_RetrievalType
    {
        [Theory]
        [InlineData(RetrievalType.Vectors, 0)]
        [InlineData(RetrievalType.FullText, 1)]
        [InlineData(RetrievalType.Mixed, 2)]
        [InlineData(RetrievalType.WebSearch, 3)]
        public void It_Should_Have_Correct_Values(RetrievalType type, int expectedValue)
        {
            // Assert
            ((int)type).ShouldBe(expectedValue);
        }
    }

    /// <summary>
    /// Tests for RerankerType enum
    /// </summary>
    public class When_Call_RerankerType
    {
        [Theory]
        [InlineData(RerankerType.BGE, 0)]
        [InlineData(RerankerType.BM25, 1)]
        [InlineData(RerankerType.FlashRank, 2)]
        public void It_Should_Have_Correct_Values(RerankerType type, int expectedValue)
        {
            // Assert
            ((int)type).ShouldBe(expectedValue);
        }
    }

    /// <summary>
    /// Tests for LlmAppType enum
    /// </summary>
    public class When_Call_LlmAppType
    {
        [Theory]
        [InlineData(LlmAppType.Chat, 0)]
        [InlineData(LlmAppType.Knowledge, 1)]
        public void It_Should_Have_Correct_Values(LlmAppType type, int expectedValue)
        {
            // Assert
            ((int)type).ShouldBe(expectedValue);
        }
    }

    /// <summary>
    /// Tests for DocumentType enum
    /// </summary>
    public class When_Call_DocumentType
    {
        [Theory]
        [InlineData(DocumentType.File, 0)]
        [InlineData(DocumentType.Text, 1)]
        [InlineData(DocumentType.Url, 2)]
        public void It_Should_Have_Correct_Values(DocumentType type, int expectedValue)
        {
            // Assert
            ((int)type).ShouldBe(expectedValue);
        }
    }

    /// <summary>
    /// Tests for QueueStatus enum
    /// </summary>
    public class When_Call_QueueStatus
    {
        [Theory]
        [InlineData(QueueStatus.Uploaded, 0)]
        [InlineData(QueueStatus.Processing, 1)]
        [InlineData(QueueStatus.Complete, 2)]
        public void It_Should_Have_Correct_Values(QueueStatus status, int expectedValue)
        {
            // Assert
            ((int)status).ShouldBe(expectedValue);
        }
    }

    /// <summary>
    /// Tests for TraceType enum
    /// </summary>
    public class When_Call_TraceType
    {
        [Theory]
        [InlineData(TraceType.Thought, 0)]
        [InlineData(TraceType.Action, 1)]
        [InlineData(TraceType.Artifact, 2)]
        public void It_Should_Have_Correct_Values(TraceType type, int expectedValue)
        {
            // Assert
            ((int)type).ShouldBe(expectedValue);
        }
    }

    /// <summary>
    /// Tests for GenderType enum
    /// </summary>
    public class When_Call_GenderType
    {
        [Theory]
        [InlineData(GenderType.Male, 0)]
        [InlineData(GenderType.Female, 1)]
        public void It_Should_Have_Correct_Values(GenderType type, int expectedValue)
        {
            // Assert
            ((int)type).ShouldBe(expectedValue);
        }
    }
}
