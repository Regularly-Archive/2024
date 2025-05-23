using Newtonsoft.Json;
using PostgreSQL.Embedding.Planners;

namespace PostgreSQL.Embedding.Common.Models.Planners
{
    public class StepTrace
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("parent_id")]
        public string ParentId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("message_id")]
        public long MessageId { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        public static StepTrace Thought(string question, string content) =>
            new StepTrace
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "思考",
                Description = question,
                Content = content,
                Status = "success",
                ParentId = AgentExecutionContextExtensions.GetStepId(),
                MessageId = AgentExecutionContextExtensions.GetMessageId(),
                Type = "Thought"
            };

        public static StepTrace Action(string actionName, Dictionary<string, object> actionVariables, string result, double duration, bool successful)
        {
            return new StepTrace()
            {
                Id = Guid.NewGuid().ToString("N"),
                ParentId = AgentExecutionContextExtensions.GetStepId(),
                Title = "使用工具",
                Description = $"使用工具 {actionName}, 耗时 {duration} 秒",
                Content = System.Text.Json.JsonSerializer.Serialize(new { input = actionVariables, output = result }),
                Status = successful ? "success" : "failed",
                MessageId = AgentExecutionContextExtensions.GetMessageId(),
                Type = "Action"
            };
        }

        public static StepTrace Plan(string planId, string planName, string planDescription, string executeResult, string status)
        {
            return new StepTrace()
            {
                Id = planId,
                ParentId = AgentExecutionContextExtensions.GetMessageId().ToString(),
                Title = planName,
                Description = planDescription,
                Content = executeResult,
                Status = status,
                MessageId = AgentExecutionContextExtensions.GetMessageId(),
                Type = "Plan"
            };
        }

        public static StepTrace Done() =>
            new StepTrace()
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "[THINK_DONE]",
                Content = "[THINK_DONE]",
                Status = "success",
                MessageId = AgentExecutionContextExtensions.GetMessageId(),
                Type = "MessageStatus"
            };
    }
}
