using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "AlgoBook API")]
    public class AlgoBookPlugin : BasePlugin
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public AlgoBookPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider) 
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("通过 ISBN 查询图书信息")]
        public async Task<string> SearchBooksByISBN([Description("ISBN")] string isbn)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetStringAsync($"https://api.algobook.info/v1/ebooks/isbn/{isbn}");
        }

        [KernelFunction]
        [Description("通过作者查询图书信息")]
        public async Task<string> SearchBooksByAuthor([Description("作者")] string author)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetStringAsync($"https://api.algobook.info/v1/ebooks/author/{author}");
        }

        [KernelFunction]
        [Description("通过书名查询图书信息")]
        public async Task<string> SearchBooksByTitle([Description("书名")] string title)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            return await httpClient.GetStringAsync($"https://api.algobook.info/v1/ebooks/title/{title}");
        }

        [KernelFunction]
        [Description("创建二维码，返回 Markdown 格式的图片")]
        public string CreateQRCode([Description("内容")] string content)
        {
            return $"![RQCode](https://api.algobook.info/v1/qr/create?data={content}&format=png)";
        }
    }
}
