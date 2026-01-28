using Microsoft.Extensions.DependencyInjection;
using Moq;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Connectors.HuggingFace;
using PostgreSQL.Embedding.Llm.Connectors.LLama;
using PostgreSQL.Embedding.Llm.Connectors.Ollama;
using PostgreSQL.Embedding.Llm.Core;
using Shouldly;
using Xunit;

namespace Wikit.Tests.LlmServices
{
    /// <summary>
    /// Tests for LlmServiceFactory
    /// </summary>
    public class When_Call_LlmServiceFactory
    {
        private readonly Mock<IServiceProvider> _mockServiceProvider;

        public When_Call_LlmServiceFactory()
        {
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockServiceProvider.Setup(x => x.GetService(typeof(LLamaService))).Returns((LLamaService)null!);
            _mockServiceProvider.Setup(x => x.GetService(typeof(HuggingFaceService))).Returns((HuggingFaceService)null!);
            _mockServiceProvider.Setup(x => x.GetService(typeof(OllamaService))).Returns((OllamaService)null!);
        }

        [Fact]
        public void It_Should_Accept_IServiceProvider()
        {
            // Arrange
            var serviceProvider = _mockServiceProvider.Object;

            // Act
            var factory = new LlmServiceFactory(serviceProvider);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => factory.ShouldNotBeNull()
            );
        }

        [Fact]
        public void It_Should_Return_Null_When_Create_With_OpenAI()
        {
            // Arrange
            var factory = new LlmServiceFactory(_mockServiceProvider.Object);

            // Act
            var result = factory.Create(LlmServiceProvider.OpenAI);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => result.ShouldBeNull()
            );
        }

        [Fact]
        public void It_Should_Call_GetService_When_Create_With_LLama()
        {
            // Arrange
            var factory = new LlmServiceFactory(_mockServiceProvider.Object);

            // Act
            var result = factory.Create(LlmServiceProvider.LLama);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => result.ShouldBeNull(),
                () => _mockServiceProvider.Verify(x => x.GetService(typeof(LLamaService)), Times.Once)
            );
        }

        [Fact]
        public void It_Should_Call_GetService_When_Create_With_HuggingFace()
        {
            // Arrange
            var factory = new LlmServiceFactory(_mockServiceProvider.Object);

            // Act
            var result = factory.Create(LlmServiceProvider.HuggingFace);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => result.ShouldBeNull(),
                () => _mockServiceProvider.Verify(x => x.GetService(typeof(HuggingFaceService)), Times.Once)
            );
        }

        [Fact]
        public void It_Should_Call_GetService_When_Create_With_Ollama()
        {
            // Arrange
            var factory = new LlmServiceFactory(_mockServiceProvider.Object);

            // Act
            var result = factory.Create(LlmServiceProvider.Ollama);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => result.ShouldBeNull(),
                () => _mockServiceProvider.Verify(x => x.GetService(typeof(OllamaService)), Times.Once)
            );
        }

        [Theory]
        [InlineData(LlmServiceProvider.OpenAI)]
        [InlineData(LlmServiceProvider.LLama)]
        [InlineData(LlmServiceProvider.HuggingFace)]
        [InlineData(LlmServiceProvider.Ollama)]
        public void It_Should_NotThrow_For_Supported_Providers(LlmServiceProvider provider)
        {
            // Arrange
            var factory = new LlmServiceFactory(_mockServiceProvider.Object);

            // Act & Assert
            Should.NotThrow(() => factory.Create(provider));
        }

        [Fact]
        public void It_Should_Throw_Exception_When_Create_With_Unhandled_Provider()
        {
            // Arrange
            var factory = new LlmServiceFactory(_mockServiceProvider.Object);

            // Act & Assert
            Should.Throw<Exception>(() => factory.Create(LlmServiceProvider.Zhipu));
        }

        [Fact]
        public void It_Should_Return_Null_For_LLama_When_Create_Twice()
        {
            // Arrange
            var factory = new LlmServiceFactory(_mockServiceProvider.Object);

            // Act
            var result1 = factory.Create(LlmServiceProvider.LLama);
            var result2 = factory.Create(LlmServiceProvider.LLama);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => result1.ShouldBeNull(),
                () => result2.ShouldBeNull()
            );
        }
    }

    /// <summary>
    /// Tests for ILlmService interface
    /// </summary>
    public class When_Call_ILlmService
    {
        [Fact]
        public void It_Should_Be_Interface()
        {
            // Arrange & Act
            var type = typeof(ILlmService);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => type.IsInterface.ShouldBeTrue()
            );
        }

        [Fact]
        public void It_Should_Be_Public()
        {
            // Arrange & Act
            var type = typeof(ILlmService);

            // Assert
            this.ShouldSatisfyAllConditions(
                () => type.IsPublic.ShouldBeTrue()
            );
        }
    }
}
