using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "中国大百科全书数据库", Enabled = false)]
    public class BKZXPlugin : BasePlugin
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public BKZXPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("从中国大百科全书数据库中检索信息")]
        public async Task<string> Query([Description("")] string keyword)
        {
            var pattern = @"\s+";
            var entities = Regex.Split(keyword, pattern);
            var stringBuilder = new StringBuilder();

            using var httpClient = _httpClientFactory.CreateClient();
            foreach (var entity in entities)
            {
                var queryResult = await httpClient.GetStringAsync($"https://r.jina.ai/https://h.bkzx.cn/search?query={entity}&sublibId=");
                stringBuilder.AppendLine(queryResult);
                stringBuilder.AppendLine();
                await Task.Delay(3000);
            }

            return stringBuilder.ToString();
        }
    }
}
