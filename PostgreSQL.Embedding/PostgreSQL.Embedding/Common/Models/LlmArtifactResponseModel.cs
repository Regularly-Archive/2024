using Newtonsoft.Json;
using SixLabors.Fonts.Unicode;

namespace PostgreSQL.Embedding.Common.Models
{
    public class LlmArtifactResponseModel
    {
        [JsonProperty("title")]
        public string Title {  get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("data")]
        public string Data { get; private set; }

        public LlmArtifactResponseModel(string title, ArtifactType type)
        {
            Title = title;
            Type = type.ToString();
        }

        public LlmArtifactResponseModel(string title, ArtifactType type, object payload)
        {
            Title = title;
            Type = type.ToString();
            Data = JsonConvert.SerializeObject(payload);
        }

        public void SetData<TPayload>(TPayload payload)
        {
            Data = JsonConvert.SerializeObject(payload);
        }
    }
}
