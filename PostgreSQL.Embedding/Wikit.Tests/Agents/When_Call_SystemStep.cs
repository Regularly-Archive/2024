using PostgreSQL.Embedding.Llm.Planners;
using Shouldly;

namespace Wikit.Tests.Agents
{
    public class When_Call_SystemStep
    {
        [Fact]
        public void It_Should_Parse_Thought_Successfully()
        {
            var input = """
            <Thought Step="1">为了回答这个问题，我需要先了解应仁之乱的历史背景和具体原因。应仁之乱是日本室町时代后期的一场大规模内战，发生在1467年至1477年之间。这场战争标志着日本战国时代的开始。为了全面总结其原因，我需要从政治、经济和社会三个方面进行分析。首先，我将搜索关于应仁之乱的历史资料，以获取详细的信息。</Thought>
            """;

            var step = ReasoningStepParser.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.Thought.ShouldNotBeNullOrEmpty(),
                () => step.Thought.ShouldBe("为了回答这个问题，我需要先了解应仁之乱的历史背景和具体原因。应仁之乱是日本室町时代后期的一场大规模内战，发生在1467年至1477年之间。这场战争标志着日本战国时代的开始。为了全面总结其原因，我需要从政治、经济和社会三个方面进行分析。首先，我将搜索关于应仁之乱的历史资料，以获取详细的信息。")
            );
        }

        [Fact]
        public void It_Should_Parse_Action_With_CDATA_Successfully()
        {
            var input = """
            <Action Step="1" Tool="BoChaAIPlugin.Search"><![CDATA[{
              "keyword": "应仁之乱 原因 政治 经济 社会"
            }]]></Action>
            """;

            var step = ReasoningStepParser.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.Action.ShouldBe("BoChaAIPlugin.Search"),
                () => step.ActionVariables.ShouldNotBeNull(),
                () => step.ActionVariables["keyword"].ToString().ShouldBe("应仁之乱 原因 政治 经济 社会")
            );
        }

        [Fact]
        public void It_Should_Parse_Action_Without_CDATA_Successfully()
        {
            var input = """
            <Action Step="1" Tool="BoChaAIPlugin.Search">{
              "keyword": "应仁之乱 原因 政治 经济 社会"
            }</Action>
            """;

            var step = ReasoningStepParser.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.Action.ShouldBe("BoChaAIPlugin.Search"),
                () => step.ActionVariables.ShouldNotBeNull(),
                () => step.ActionVariables["keyword"].ToString().ShouldBe("应仁之乱 原因 政治 经济 社会")
            );
        }

        [Fact]
        public void It_Should_Parse_FinalAnswer_Successfully()
        {
            var input = """
            <FinalAnswer Step="1">
                <Content>应仁之乱可以看作是日本战国时代的开端</Content>
                <Confidence Level="High">Verified via historical records</Confidence>
            </FinalAnswer>
            """;

            var step = ReasoningStepParser.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.StructuredFinalAnswer.ShouldNotBeNull(),
                () => step.StructuredFinalAnswer.Content.ShouldBe("应仁之乱可以看作是日本战国时代的开端"),
                () => step.StructuredFinalAnswer.Level.ShouldBe("High"),
                () => step.StructuredFinalAnswer.Reason.ShouldBe("Verified via historical records")
            );
        }

        [Fact]
        public void It_Should_Parse_Complete_Step_Successfully()
        {
            var input = """
            <Thought Step="1">分析问题，这是第一步思考</Thought>
            <Action Step="1" Tool="WebSearchPlugin.Run"><![CDATA[{"keyword":"test"}]]></Action>
            """;

            var step = ReasoningStepParser.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.Thought.ShouldBe("分析问题，这是第一步思考"),
                () => step.Action.ShouldBe("WebSearchPlugin.Run"),
                () => step.ActionVariables["keyword"].ToString().ShouldBe("test")
            );
        }

        [Fact]
        public void It_Should_Parse_FinalAnswer_Without_Confidence_Successfully()
        {
            var input = """
            <FinalAnswer Step="1">
                <Content>这是最终答案</Content>
            </FinalAnswer>
            """;

            var step = ReasoningStepParser.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.StructuredFinalAnswer.ShouldNotBeNull(),
                () => step.StructuredFinalAnswer.Content.ShouldBe("这是最终答案"),
                () => step.StructuredFinalAnswer.Level.ShouldBe("Medium"),
                () => step.StructuredFinalAnswer.Reason.ShouldBeEmpty()
            );
        }

        [Fact]
        public void It_Should_Format_Thought_Correctly()
        {
            var step = new ReasoningStep
            {
                Index = 1,
                Thought = "这是一个思考"
            };

            var formatted = step.FormatThought();
            formatted.ShouldBe("<Thought Step=\"1\">这是一个思考</Thought>");
        }

        [Fact]
        public void It_Should_Format_Action_Correctly()
        {
            var step = new ReasoningStep
            {
                Index = 1,
                Action = "WebSearchPlugin.Run",
                ActionVariables = new Dictionary<string, object>
                {
                    { "keyword", "test query" }
                }
            };

            var formatted = step.FormatAction();
            formatted.ShouldContain("WebSearchPlugin.Run");
            formatted.ShouldContain("<![CDATA[");
            formatted.ShouldContain("]]>");
        }

        [Fact]
        public void It_Should_Format_Observation_Correctly()
        {
            var step = new ReasoningStep
            {
                Index = 1,
                Observation = "这是一个观察结果"
            };

            var formatted = step.FormatObservation();
            formatted.ShouldBe("<Observation Step=\"1\">这是一个观察结果</Observation>");
        }
    }
}
