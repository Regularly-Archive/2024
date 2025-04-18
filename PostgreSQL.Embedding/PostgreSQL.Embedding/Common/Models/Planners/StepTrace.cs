using Newtonsoft.Json;

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

        public static StepTrace Thought(string question, string content) => new StepTrace { Title = "思考", Description = question, Content = content, Status = "success" };

        public static StepTrace Action(string actionName, Dictionary<string, object> actionVariables, double duration, bool successful)
        {
            var jsonContent = JsonConvert.SerializeObject(new { actionName = actionName, actionVariables = actionVariables });
            var markdownContent = $@"```json\r\n
            {jsonContent}
            ```";

            return new StepTrace()
            {
                Title = "工具调用",
                Description = $"调用工具 {actionName} 耗时 {duration} 秒",
                Content = jsonContent,
                Status = successful ? "success" : "failed"
            };
        }

        public static StepTrace Plan(string planId, string planName, string planDescription, string executeResult, string status)
        {
            return new StepTrace()
            {
                Id = planId,
                Title = planName,
                Description = planDescription,
                Content = executeResult,
                Status = status
            };
        }

        public static StepTrace Done() => new StepTrace() { Title = "[THINK_DONE]" };
    }
}
