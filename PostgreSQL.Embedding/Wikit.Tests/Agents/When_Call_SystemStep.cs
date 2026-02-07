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
            [THOUGHT] 为了回答这个问题，我需要先了解应仁之乱的历史背景和具体原因。应仁之乱是日本室町时代后期的一场大规模内战，发生在1467年至1477年之间。这场战争标志着日本战国时代的开始。为了全面总结其原因，我需要从政治、经济和社会三个方面进行分析。首先，我将搜索关于应仁之乱的历史资料，以获取详细的信息。
            """;

            var step = SystemStep.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.Thought.ShouldNotBeNullOrEmpty(),
                () => step.Thought.ShouldBe("为了回答这个问题，我需要先了解应仁之乱的历史背景和具体原因。应仁之乱是日本室町时代后期的一场大规模内战，发生在1467年至1477年之间。这场战争标志着日本战国时代的开始。为了全面总结其原因，我需要从政治、经济和社会三个方面进行分析。首先，我将搜索关于应仁之乱的历史资料，以获取详细的信息。")
            );
        }

        [Fact]
        public void It_Should_Parse_Action_With_Action_Tag_Successfully()
        {
            var input = """
            [ACTION]
            {
              "action": "BoChaAIPlugin.Search",
              "action_variables": {
                "keyword": "应仁之乱 原因 政治 经济 社会"
              }
            }
            """;

            var step = SystemStep.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.Action.ShouldBe("BoChaAIPlugin.Search"),
                () => step.ActionVariables.ShouldNotBeNull(),
                () => step.ActionVariables["keyword"].ToString().ShouldBe("应仁之乱 原因 政治 经济 社会")
            );
        }

        [Fact]
        public void It_Should_Parse_Action_Without_Action_Tag_Successfully()
        {
            var input = """
            {
              "action": "BoChaAIPlugin.Search",
              "action_variables": {
                "keyword": "应仁之乱 原因 政治 经济 社会"
              }
            }
            """;

            var step = SystemStep.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.Action.ShouldBe("BoChaAIPlugin.Search"),
                () => step.ActionVariables.ShouldNotBeNull(),
                () => step.ActionVariables["keyword"].ToString().ShouldBe("应仁之乱 原因 政治 经济 社会")
            );
        }

        [Fact]
        public void It_Should_Parse_Final_Answer_Successfully()
        {
            var input = """
            [FINAL_ANSWER] 应仁之乱可以看作是日本战国时代的开端
            """;

            var step = SystemStep.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.FinalAnswer.ShouldNotBeNullOrEmpty(),
                () => step.FinalAnswer.ShouldBe("应仁之乱可以看作是日本战国时代的开端")
            );
        }

        [Fact]
        public void It_Should_Parse_Thought_With_Number_Successfully()
        {
            var input = """
            [THOUGHT-1] 分析问题，这是第一步思考
            [ACTION-1]
            {"action":"WebSearchPlugin.Run","action_variables":{"keyword":"test"}}
            """;

            var step = SystemStep.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.Thought.ShouldBe("分析问题，这是第一步思考"),
                () => step.Action.ShouldBe("WebSearchPlugin.Run")
            );
        }

        [Fact]
        public void It_Should_Parse_Thought_Without_Number_Successfully()
        {
            var input = """
            [THOUGHT] 直接思考，没有数字标签
            [ACTION]
            {"action":"WriterPlugin.PolishText","action_variables":{"text":"hello"}}
            """;

            var step = SystemStep.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.Thought.ShouldBe("直接思考，没有数字标签"),
                () => step.Action.ShouldBe("WriterPlugin.PolishText")
            );
        }

        [Fact]
        public void It_Should_Parse_Thought_With_Large_Number_Successfully()
        {
            var input = """
            [THOUGHT-100] 第一百步思考
            [ACTION-100]
            {"action":"CodeInterpreterPlugin.RunPython","action_variables":{"code":"print(1)"}}
            """;

            var step = SystemStep.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.Thought.ShouldBe("第一百步思考"),
                () => step.Action.ShouldBe("CodeInterpreterPlugin.RunPython")
            );
        }

        [Fact]
        public void It_Should_Parse_Final_Answer_With_Underscore_Successfully()
        {
            var input = """
            [FINAL_ANSWER] 这是最终答案
            """;

            var step = SystemStep.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.FinalAnswer.ShouldNotBeNullOrEmpty(),
                () => step.FinalAnswer.ShouldBe("这是最终答案")
            );
        }

        [Fact]
        public void It_Should_Parse_Final_Answer_With_Dash_Successfully()
        {
            var input = """
            [FINAL-ANSWER] 这是最终答案带横线
            """;

            var step = SystemStep.Parse(input);
            this.ShouldSatisfyAllConditions(
                () => step.FinalAnswer.ShouldNotBeNullOrEmpty(),
                () => step.FinalAnswer.ShouldBe("这是最终答案带横线")
            );
        }
    }
}
