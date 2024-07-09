using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "中国大百科全书数据库", Enabled = false)]
    public class BKZXPlugin
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public BKZXPlugin(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("从中国大百科全书数据库中检索信息")]
        public async Task<string> Query([Description("关键词/实体")] string entity)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetStringAsync($"https://r.jina.ai/https://h.bkzx.cn/search?query={entity}&sublibId=");
        }
    }
}
