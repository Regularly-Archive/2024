using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Common.Models.KernelMemory
{
    public class KMSearchResult
    {
        [JsonProperty("question")]
        public string Question { get; set; }

        [JsonProperty("relevantSources")]
        public List<KMCitation> RelevantSources { get; set; } = new List<KMCitation>();
    }
}
