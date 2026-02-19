using PostgreSQL.Embedding.Domain.Models.RAG;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Llm.Services;
using Shouldly;
using Xunit;

namespace Wikit.Tests.LlmServices
{
    /// <summary>
    /// Tests for CitationService.ReorderReferences
    /// </summary>
    public class When_Call_CitationService
    {
        private readonly CitationService _citationService;

        public When_Call_CitationService()
        {
            _citationService = new CitationService(new PromptTemplateService());
        }

        private List<LlmCitationModel> CreateCitations(params (int Index, string Text, string Url)[] items)
        {
            return items.Select(x => new LlmCitationModel
            {
                Index = x.Index,
                Text = x.Text,
                Url = x.Url,
                Type = "document"
            }).ToList();
        }

        [Fact]
        public void It_Should_Return_Empty_When_No_Citations_In_Answer()
        {
            // Arrange
            var citations = CreateCitations((1, "Content A", "http://a.com"));
            var answer = "This is a plain answer without any citations.";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBe(answer);
            result.CitationItems.ShouldBeEmpty();
        }

        [Fact]
        public void It_Should_Renumber_Single_Citation_Correctly()
        {
            // Arrange
            var citations = CreateCitations((3, "Content A", "http://a.com"));
            var answer = "Answer with [^3] citation.";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBe("Answer with [^1] citation.");
            result.CitationItems.ShouldHaveSingleItem();
            result.CitationItems[0].Index.ShouldBe(3);
        }

        [Fact]
        public void It_Should_Renumber_Multiple_Citations_Sequentially()
        {
            // Arrange
            var citations = CreateCitations(
                (1, "Content A", "http://a.com"),
                (2, "Content B", "http://b.com"),
                (3, "Content C", "http://c.com")
            );
            var answer = "Answer with [^3], [^1], and [^2] citations.";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBe("Answer with [^1], [^2], and [^3] citations.");
            result.CitationItems.Count.ShouldBe(3);
            result.CitationItems[0].Index.ShouldBe(3);
            result.CitationItems[1].Index.ShouldBe(1);
            result.CitationItems[2].Index.ShouldBe(2);
        }

        [Fact]
        public void It_Should_Only_Include_Cited_Sources_In_CitationItems()
        {
            // Arrange
            var citations = CreateCitations(
                (1, "Content A", "http://a.com"),
                (2, "Content B", "http://b.com"),
                (3, "Content C", "http://c.com"),
                (4, "Content D", "http://d.com"),
                (5, "Content E", "http://e.com")
            );
            var answer = "LLM chose [^3] and [^1].";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert - CitationItems should only include cited sources (not all citations)
            result.FormattedAnswer.ShouldBe("LLM chose [^1] and [^2].");
            result.CitationItems.Count.ShouldBe(2);
            result.CitationItems[0].Index.ShouldBe(3); // Original [^3] -> [^1]
            result.CitationItems[1].Index.ShouldBe(1); // Original [^1] -> [^2]
        }

        [Fact]
        public void It_Should_Handle_Duplicate_Citations_Correctly()
        {
            // Arrange
            var citations = CreateCitations((1, "Content A", "http://a.com"));
            var answer = "First [^1] and second [^1] both refer to same source.";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBe("First [^1] and second [^1] both refer to same source.");
            result.CitationItems.ShouldHaveSingleItem();
            result.CitationItems[0].Index.ShouldBe(1);
        }

        [Fact]
        public void It_Should_Handle_Multiple_Same_Citations_With_Others()
        {
            // Arrange
            var citations = CreateCitations(
                (1, "Content A", "http://a.com"),
                (2, "Content B", "http://b.com"),
                (3, "Content C", "http://c.com")
            );
            var answer = "[^2] appears first, then [^1], then [^2] again, and finally [^3].";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBe("[^1] appears first, then [^2], then [^1] again, and finally [^3].");
            result.CitationItems.Count.ShouldBe(3);
            result.CitationItems[0].Index.ShouldBe(2);
            result.CitationItems[1].Index.ShouldBe(1);
            result.CitationItems[2].Index.ShouldBe(3);
        }

        [Fact]
        public void It_Should_Preserve_Text_Outside_Citations()
        {
            // Arrange
            var citations = CreateCitations((1, "Content A", "http://a.com"));
            var answer = "Before text [^1] middle text after text.";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBe("Before text [^1] middle text after text.");
        }

        [Fact]
        public void It_Should_Handle_Empty_Citations_List()
        {
            // Arrange
            var citations = new List<LlmCitationModel>();
            var answer = "No citations here [^1].";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBe("No citations here [^1].");
            result.CitationItems.ShouldBeEmpty();
        }

        [Fact]
        public void It_Should_Handle_Empty_Answer()
        {
            // Arrange
            var citations = CreateCitations((1, "Content A", "http://a.com"));
            var answer = "";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBeEmpty();
            result.CitationItems.ShouldBeEmpty();
        }

        [Fact]
        public void It_Should_Maintain_Citation_Order_By_First_Appearance()
        {
            // Arrange
            var citations = CreateCitations(
                (1, "Content A", "http://a.com"),
                (2, "Content B", "http://b.com"),
                (3, "Content C", "http://c.com")
            );
            var answer = "[^3] was first, then [^1], then [^2].";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBe("[^1] was first, then [^2], then [^3].");
            result.CitationItems[0].Index.ShouldBe(3);
            result.CitationItems[1].Index.ShouldBe(1);
            result.CitationItems[2].Index.ShouldBe(2);
        }

        [Fact]
        public void It_Should_Handle_Answer_Starting_With_Citation()
        {
            // Arrange
            var citations = CreateCitations((5, "Content A", "http://a.com"));
            var answer = "[^5] is at the start of the answer.";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBe("[^1] is at the start of the answer.");
        }

        [Fact]
        public void It_Should_Handle_Answer_Ending_With_Citation()
        {
            // Arrange
            var citations = CreateCitations((2, "Content A", "http://a.com"));
            var answer = "Ends with [^2] citation.";

            // Act
            var result = _citationService.ReorderCitations(citations, answer);

            // Assert
            result.FormattedAnswer.ShouldBe("Ends with [^1] citation.");
        }
    }

    /// <summary>
    /// Tests for CitationService.RemoveCitations
    /// </summary>
    public class When_Call_CitationService_RemoveCitations
    {
        private readonly CitationService _citationService;

        public When_Call_CitationService_RemoveCitations()
        {
            _citationService = new CitationService(new PromptTemplateService());
        }

        [Fact]
        public void It_Should_Remove_All_Citation_Markers()
        {
            // Arrange
            var text = "Answer with [^1], [^2], and [^3] citations.";

            // Act
            var result = _citationService.RemoveCitations(text);

            // Assert
            result.ShouldBe("Answer with , , and  citations.");
        }

        [Fact]
        public void It_Should_Return_Original_Text_When_No_Citations()
        {
            // Arrange
            var text = "Plain text without any citations.";

            // Act
            var result = _citationService.RemoveCitations(text);

            // Assert
            result.ShouldBe(text);
        }

        [Fact]
        public void It_Should_Handle_Empty_Text()
        {
            // Arrange
            var text = "";

            // Act
            var result = _citationService.RemoveCitations(text);

            // Assert
            result.ShouldBeEmpty();
        }

        [Fact]
        public void It_Should_Remove_Single_Citation()
        {
            // Arrange
            var text = "Single [^42] citation.";

            // Act
            var result = _citationService.RemoveCitations(text);

            // Assert
            result.ShouldBe("Single  citation.");
        }

        [Fact]
        public void It_Should_Remove_Citations_At_Beginning_And_End()
        {
            // Arrange
            var text = "[^1] Text in the middle [^2].";

            // Act
            var result = _citationService.RemoveCitations(text);

            // Assert
            result.ShouldBe(" Text in the middle .");
        }

        [Fact]
        public void It_Should_Remove_Adjacent_Citations()
        {
            // Arrange
            var text = "[^1][^2][^3]";

            // Act
            var result = _citationService.RemoveCitations(text);

            // Assert
            result.ShouldBeEmpty();
        }
    }
}
