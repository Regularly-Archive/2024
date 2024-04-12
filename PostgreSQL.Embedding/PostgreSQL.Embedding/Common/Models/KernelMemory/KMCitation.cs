using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Common.Models.KernelMemory
{
    public class KMCitation
    {
        [JsonProperty("sourceName")]
        public string SourceName { get; set; }
        [JsonProperty("partitions")]
        public List<KMPartition> Partitions { get; set;} = new List<KMPartition>();
    }
}
