using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Common.Models
{
    public class ConversationRequestModel
    {
        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = false;

        [JsonPropertyName("agenticMode")]
        public bool AgenticMode { get; set; } = true;

        [JsonPropertyName("accessInternet")]
        public bool AccessInternet { get; set; } = true;

        [JsonPropertyName("conversationId")]
        public string ConversationId { get; set; }

        [JsonPropertyName("messages")]
        public List<OpenAIMessage> Messages { get; set; }
    }
}
