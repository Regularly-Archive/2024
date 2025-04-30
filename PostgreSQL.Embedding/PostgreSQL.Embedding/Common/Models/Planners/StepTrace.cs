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
        public string Type {  get; set; }

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

        public static StepTrace Action(string actionName, Dictionary<string, object> actionVariables, double duration, bool successful)
        {
            var jsonContent = JsonConvert.SerializeObject(new { actionName = actionName, actionVariables = actionVariables });
            var markdownContent = $@"```json\r\n
            {jsonContent}
            ```";

            return new StepTrace()
            {
                Id = Guid.NewGuid().ToString("N"),
                ParentId = AgentExecutionContextExtensions.GetStepId(),
                Title = "工具调用",
                Description = $"调用工具 {actionName} 耗时 {duration} 秒",
                Content = jsonContent,
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
