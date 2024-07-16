
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "XLORE - 中英文跨语言知识图谱插件")]
    public class XLOREPlugin
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public XLOREPlugin(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("关键字查询")]
        public async Task<string> SearchAsync([Description("关键字")] string text, [Description("语言")] string lang = "zh")
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var formData = new Dictionary<string, string> { { "text", text }, { "lang", lang } };
            var httpContent = GetFormUrlEncodedContent(formData);

            var response = await httpClient.PostAsync("https://api.xlore.cn/search", httpContent);
            response.EnsureSuccessStatusCode();

            return (await response.Content.ReadAsStringAsync());
        }


        [KernelFunction]
        [Description("获取实例信息")]
        public async Task<string> GetInstanceAsync([Description("实例URL")] string url, [Description("语言")] string lang = "zh")
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var formData = new Dictionary<string, string> { { "url", $"http://xlore.cn/instance/{url}" }, { "lang", lang } };
            var httpContent = GetFormUrlEncodedContent(formData);

            var response = await httpClient.PostAsync("https://api.xlore.cn/instance", httpContent);
            response.EnsureSuccessStatusCode();

            return (await response.Content.ReadAsStringAsync());
        }

        [KernelFunction]
        [Description("获取概念信息")]
        public async Task<string> GetConceptAsync([Description("实例URL")] string url, [Description("语言")] string lang = "zh")
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var formData = new Dictionary<string, string> { { "url", $"http://xlore.cn/concept/{url}" }, { "lang", lang } };
            var httpContent = GetFormUrlEncodedContent(formData);

            var response = await httpClient.PostAsync("https://api.xlore.cn/concept", httpContent);
            response.EnsureSuccessStatusCode();

            return (await response.Content.ReadAsStringAsync());
        }

        [KernelFunction]
        [Description("获取属性信息")]
        public async Task<string> GetPropertyAsync([Description("实例URL")] string url, [Description("语言")] string lang = "zh")
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var formData = new Dictionary<string, string> { { "url", $"http://xlore.cn/property/{url}" }, { "lang", lang } };
            var httpContent = GetFormUrlEncodedContent(formData);

            var response = await httpClient.PostAsync("https://api.xlore.cn/property", httpContent);
            response.EnsureSuccessStatusCode();

            return (await response.Content.ReadAsStringAsync());
        }

        private FormUrlEncodedContent GetFormUrlEncodedContent(Dictionary<string, string> formData) => new FormUrlEncodedContent(formData);
    }
}
