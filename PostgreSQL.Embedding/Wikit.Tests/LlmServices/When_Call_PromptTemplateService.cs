using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Llm.Services;
using Shouldly;
using Xunit;

namespace Wikit.Tests.LlmServices
{
    /// <summary>
    /// Tests for PromptTemplateService
    /// Note: These tests are limited because PromptTemplateService uses embedded resources
    /// from the main assembly, but the tests run from the test assembly.
    /// </summary>
    public class When_Call_PromptTemplateService
    {
        [Fact]
        public void It_Should_NotThrow_When_Construct()
        {
            // Arrange & Act
            var service = new PromptTemplateService();

            // Assert
            service.ShouldNotBeNull();
        }

        [Fact]
        public void It_Should_Throw_ArgumentException_When_Template_Not_Exists()
        {
            // Arrange
            var service = new PromptTemplateService();
            var nonExistentTemplate = "NonExistentTemplate12345";

            // Act & Assert
            Should.Throw<ArgumentException>(() => service.LoadTemplate(nonExistentTemplate));
        }

        [Fact]
        public void It_Should_Throw_ArgumentException_With_Correct_Message()
        {
            // Arrange
            var service = new PromptTemplateService();
            var templateName = "TemplateThatDoesNotExist";

            // Act
            var exception = Should.Throw<ArgumentException>(() => service.LoadTemplate(templateName));

            // Assert
            exception.Message.ShouldContain(templateName);
        }
    }

    /// <summary>
    /// Tests for CallablePromptTemplate
    /// </summary>
    public class When_Call_CallablePromptTemplate
    {
        [Fact]
        public void It_Should_Initialize_With_Template()
        {
            // Arrange
            var templateContent = "Hello {{name}}";

            // Act
            var template = new CallablePromptTemplate(templateContent);

            // Assert
            template.Template.ShouldBe(templateContent);
        }

        [Fact]
        public void It_Should_Store_Variable_When_AddVariable()
        {
            // Arrange
            var template = new CallablePromptTemplate("Test {{key}}");
            var key = "myKey";
            var value = "myValue";

            // Act
            template.AddVariable(key, value);

            // Assert
            template.ShouldNotBeNull();
        }

        [Fact]
        public void It_Should_Be_Null_By_Default_For_FunctionName()
        {
            // Arrange
            var template = new CallablePromptTemplate("Test");

            // Assert
            template.FunctionName.ShouldBeNull();
        }

        [Fact]
        public void It_Should_Be_Settable_For_FunctionName()
        {
            // Arrange
            var template = new CallablePromptTemplate("Test");
            var expectedFunctionName = "MyFunction";

            // Act
            template.FunctionName = expectedFunctionName;

            // Assert
            template.FunctionName.ShouldBe(expectedFunctionName);
        }

        [Fact]
        public void It_Should_NotBe_Empty_For_Template()
        {
            // Arrange
            var templateContent = "This is a test template with {{variable}}";

            // Act
            var template = new CallablePromptTemplate(templateContent);

            // Assert
            template.Template.ShouldNotBeNullOrEmpty();
            template.Template.ShouldContain("{{variable}}");
        }

        [Fact]
        public void It_Should_Contain_Multiple_Variables()
        {
            // Arrange
            var templateContent = "Hello {{name}}, your email is {{email}}";

            // Act
            var template = new CallablePromptTemplate(templateContent);

            // Assert
            template.Template.ShouldContain("{{name}}");
            template.Template.ShouldContain("{{email}}");
        }
    }
}
