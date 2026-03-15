using Masuit.Tools;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Utils;

namespace PostgreSQL.Embedding.Domain.Models.Planners
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

        public static StepTrace ToolCall(string actionName, Dictionary<string, object> actionVariables, string result, double duration, bool successful, string stepId, long messageId)
        {
            return new StepTrace()
            {
                Id = Guid.NewGuid().ToString("N"),
                ParentId = stepId,
                Title = $"ToolCall|{actionName}",
                Description = $"使用工具 {actionName}, 耗时 {duration} 秒",
                Content = System.Text.Json.JsonSerializer.Serialize(new { input = actionVariables, output = result }),
                Status = successful ? "success" : "failed",
                MessageId = messageId,
                Type = "Action"
            };
        }

        public static StepTrace ToolUse(string actionName, Dictionary<string, object> actionVariables, string stepId, long messageId)
        {
            return new StepTrace()
            {
                Id = Guid.NewGuid().ToString("N"),
                ParentId = stepId,
                Title = $"ToolCall|{actionName}",
                Description = $"使用工具 {actionName}",
                Content = System.Text.Json.JsonSerializer.Serialize(new { input = actionVariables, output = string.Empty }),
                Status = "pending",
                MessageId = messageId,
                Type = "ToolUse"
            };
        }

        public static StepTrace ToolResult(StepTrace stepTrace, string actionName, Dictionary<string, object> actionVariables, string result, double duration, bool successful)
        {
            return new StepTrace()
            {
                Id = stepTrace.Id,
                ParentId = stepTrace.ParentId,
                Title = $"ToolCall|{actionName}",
                Description = $"使用工具 {actionName}, 耗时 {duration} 秒",
                Content = System.Text.Json.JsonSerializer.Serialize(new { input = actionVariables, output = result }),
                Status = successful ? "success" : "failed",
                MessageId = stepTrace.MessageId,
                Type = "ToolResult"
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

        public static StepTrace StepDone(long messageId) =>
            new StepTrace()
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "[STEP_DONE]",
                Content = "[STEP_DONE]",
                Status = "success",
                MessageId = messageId,
                Type = "MessageStatus"
            };

        public static StepTrace ThinkDone(long messageId, double duration) =>
            new StepTrace()
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "[THINK_DONE]",
                Content = duration.Round(2).ToString(),
                Status = "success",
                MessageId = messageId,
                Type = "MessageStatus"
            };

        public static StepTrace PlanningDone(long messageId) =>
            new StepTrace()
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "[PLANNING_DONE]",
                Content = "[PLANNING_DONE]",
                Status = "success",
                MessageId = messageId,
                Type = "MessageStatus"
            };

        public IEnumerable<StepTrace> AsStreamingThought() => this.Type == "Thought"
            ? this.Content.SplitString(1, 4).Select(x => StepTrace.Thought(this.Description, x, this.ParentId, this.MessageId))
            : new List<StepTrace> { this };

        public static IEnumerable<StepTrace> AsStreamingThought(string question, string content, string stepId, long messageId) =>
            content.SplitString(1, 5).Select(x => StepTrace.Thought(question, x, stepId, messageId));
    }
}
