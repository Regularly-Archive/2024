using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Common.Models.KernelMemory
{
    public class KMAskResult
    {
        [JsonProperty("question")]
        public string Question { get; set; }

        [JsonProperty("answer")]
        public string Answer { get; set; }

        [JsonProperty("relevantSources")]
        public List<KMCitation> RelevantSources { get; set; }
    }
}
