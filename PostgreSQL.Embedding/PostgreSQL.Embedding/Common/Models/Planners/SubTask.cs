using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Common.Models.Planners
{
    public class SubTask

    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("desc")]
        public string Description { get; set; }

        [JsonProperty("depends_on")]
        public List<int> DependsOn { get; set; } = new List<int>();

        [JsonProperty("available_tools")]
        public List<string> AvailableTools { get; set; } = new List<string>();

        [JsonProperty("status")]
        public TaskStatus Status { get; set; }

        [JsonProperty("execute_result")]
        public string ExecuteResult { get; set; }

        public StepTrace AsStepTrace()
        {
            return StepTrace.Plan(Id.ToString(), Name, Description, ExecuteResult, Status.ToString().ToLower());
        }
    }

    public class PlanResult
    {
        [JsonProperty("tasks")]
        public List<SubTask> Tasks { get; set; }
    }

    public enum TaskStatus
    {
        Pending = 0,
        InProgress = 1,
        Success = 2,
        Failed = 3,
    }
}
