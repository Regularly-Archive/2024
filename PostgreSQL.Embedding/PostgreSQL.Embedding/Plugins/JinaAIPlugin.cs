using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "一个基于 JinaAI 的插件，支持信息检索及信息提取等功能")]
    public sealed class JinaAIPlugin
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public JinaAIPlugin(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("一个由 JinaAI 驱动的搜索接口，返回内容格式为 Markdown。")]
        public async Task<string> SearchAsync([Description("查询关键词")] string query)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetStringAsync($"https://s.jina.ai/{query}");
        }

        [KernelFunction]
        [Description("一个由 JinaAI 驱动的信息提取接口，可以返回指定网页的内容，返回格式为 Markdown。")]
        public async Task<string> ExtractAsync([Description("网址")] string url)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetStringAsync($"https://r.jina.ai/{url}");
        }


    }
}
