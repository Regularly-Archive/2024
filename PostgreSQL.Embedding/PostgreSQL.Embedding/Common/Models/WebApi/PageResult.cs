using Newtonsoft.Json;

namespace PostgreSQL.Embedding.Common.Models.WebApi
{
    public class PageResult<T>
    {
        [JsonProperty("totalCount")]
        public long TotalCount { get; set; }

        [JsonProperty("rows")]
        public List<T> Rows { get; set; }
    }
}
