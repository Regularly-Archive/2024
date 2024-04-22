using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Common.Models.RAG
{
    public class LlmRewriteResult
    {
        [JsonProperty("input")]
        public string Input { get; set; }

        [JsonProperty("output")]
        public List<string> Output { get; set; }
    }
}
