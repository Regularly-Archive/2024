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

        public static StepTrace Thought(string question, string content, string stepId, long messageId) =>
            new StepTrace
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "思考",
                Description = question,
                Content = content,
                Status = "success",
                ParentId = stepId,
                MessageId = messageId,
                Type = "Thought"
            };

        public static StepTrace Action(string actionName, Dictionary<string, object> actionVariables, string result, double duration, bool successful, string stepId, long messageId)
        {
            return new StepTrace()
            {
                Id = Guid.NewGuid().ToString("N"),
                ParentId = stepId,
                Title = "使用工具",
                Description = $"使用工具 {actionName}, 耗时 {duration} 秒",
                Content = System.Text.Json.JsonSerializer.Serialize(new { input = actionVariables, output = result }),
                Status = successful ? "success" : "failed",
                MessageId = messageId,
                Type = "Action"
            };
        }

        public static StepTrace Plan(string planId, string planName, string planDescription, string executeResult, string status, long messageId)
        {
            return new StepTrace()
            {
                Id = planId,
                ParentId = messageId.ToString(),
                Title = planName,
                Description = planDescription,
                Content = executeResult,
                Status = status,
                MessageId = messageId,
                Type = "Plan"
            };
        }

        public static StepTrace Done(long messageId) =>
            new StepTrace()
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "[THINK_DONE]",
                Content = "[THINK_DONE]",
                Status = "success",
                MessageId = messageId,
                Type = "MessageStatus"
            };
    }
}
