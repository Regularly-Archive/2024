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
    }
}
