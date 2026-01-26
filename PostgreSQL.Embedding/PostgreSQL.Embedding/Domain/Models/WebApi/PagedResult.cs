using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Domain.Models.WebApi
{
    public class PagedResult<T>
    {
        [JsonProperty("totalCount")]
        public int TotalCount { get; set; }

        [JsonProperty("rows")]
        public List<T> Rows { get; set; }
    }
}
