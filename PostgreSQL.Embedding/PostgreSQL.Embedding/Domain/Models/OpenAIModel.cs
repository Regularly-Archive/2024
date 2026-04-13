using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Domain.Models
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

    public class UserInputFile
    {
        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }

        [JsonPropertyName("name")]
        public string FileName { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("type")]
        public string Type {  get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}
