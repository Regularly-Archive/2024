using JetBrains.Annotations;
using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Common.Models
{
    public class OpenAIResult
    {
        public string id { get; set; } = Guid.NewGuid().ToString();
        [JsonProperty("object")]
        public string obj { get; set; } = "chat.completion";
        public List<ChoicesModel> choices { get; set; }
        public long created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }


    public class ChoicesModel
    {
        public string finish_reason { get; set; } = "stop";
        public int index { get; set; } = 0;

        public OpenAIMessage message { get; set; }
    }

    public class OpenAICompatibleEmbeddingResult
    {
        [JsonProperty("object")]
        public string Object { get; set; } = "list";

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("usage")]
        public UsageModel Usage { get; set; } = new UsageModel();

        [JsonProperty("data")]
        public List<OpenAICompatibleEmbeddingDataModel> Data { get; set; }
    }

    public class UsageModel
    {
        public long prompt_tokens { get; set; } = 0;

        public long total_tokens { get; set; } = 0;
    }

    public class OpenAICompatibleEmbeddingDataModel
    {
        [JsonProperty("object")]
        public string Object { get; set; } = "embedding";

        [JsonProperty("index")]
        public int Index { get; set; } = 0;

        [JsonProperty("embedding")]
        public List<float> Embedding { get; set; }
    }


    public class OpenAIStreamResult
    {
        public string id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("object")]
        public string obj { get; set; } = "chat.completion";

        public List<StreamChoicesModel> choices { get; set; }

        public long created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public class StreamChoicesModel
    {
        public int index { get; set; } = 0;

        public OpenAIMessage delta { get; set; }
    }

    public class OpenAICompatibleCompletionResult
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("object")]
        public string Object { get; set; } = "text.completion";

        [JsonProperty("choices")]
        public List<OpenAICompatibleCompletionChoiceModel> Choices { get; set; }

        [JsonProperty("created")]
        public long Created { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public class OpenAICompatibleCompletionChoiceModel
    {
        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
