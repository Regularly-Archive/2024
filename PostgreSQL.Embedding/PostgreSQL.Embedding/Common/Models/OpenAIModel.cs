using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Common.Models
{
    public class OpenAIModel
    {
        public bool stream { get; set; } = false;
        public List<OpenAIMessage> messages { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }
    }

    public class OpenAIMessage
    {
        public string role { get; set; }

        public string content { get; set; }
    }

    public class OpenAIEmbeddingModel
    {
        public string model { get; set; }
        public List<string> input { get; set; }
    }

    public class OpenAICompletionModel
    {
        public string model { get; set; }
        public string prompt { get; set; }
    }
}
