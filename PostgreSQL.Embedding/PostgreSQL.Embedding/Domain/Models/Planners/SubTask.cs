using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Streaming;

namespace PostgreSQL.Embedding.Domain.Models.Planners
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

        [JsonProperty("state")]
        public TaskState State { get; set; }

        [JsonProperty("execute_result")]
        public string ExecuteResult { get; set; }

        [JsonProperty("required_artifacts")]
        public List<string> RequiredArtifacts { get; set; } = new List<string>();

        [JsonProperty("output_artifacts")]
        public List<string> OutputArtifacts { get; set; } = new List<string>();

        public List<CitationItem> CitationItems { get; set; } = new List<CitationItem>();

        public StepTrace AsStepTrace(long messageId)
        {
            return StepTrace.Plan(Id.ToString(), Name, Description, ExecuteResult, State.ToString().ToLower(), messageId);
        }
    }

    public class PlanResult
    {
        [JsonProperty("tasks")]
        public List<SubTask> Tasks { get; set; } = [];

        [JsonProperty("thought")]
        public string Thought {  get; set; }

        [JsonProperty("output_format")]
        public string OutputFormat { get; set; }
    }

    public enum TaskState
    {
        Pending = 0,
        InProgress = 1,
        Completed = 2,
        Failed = 3,
    }
}
