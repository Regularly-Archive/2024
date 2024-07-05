using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace PostgreSQL.Embedding.Common.Json
{
    public static class JsonSerializerExtensions
    {
        public static string Serialize<T>(T value, JsonSerializerOptions options = null)
        {
            if (options == null)
            {
                options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                };
            }

            return JsonSerializer.Serialize(value, options);
        }
    }
}
