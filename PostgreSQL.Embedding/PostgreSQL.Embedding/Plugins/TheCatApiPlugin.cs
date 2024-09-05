using DocumentFormat.OpenXml.Math;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.Plugin;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "一个随机返回猫咪图片的插件")]
    public class TheCatApiPlugin : BasePlugin
    {
        [PluginParameter(Description = "API KEY", Required = true)]
        public string API_KEY { get; set; }

        private const string X_API_KEY = "x-api-key";
        private readonly IHttpClientFactory _httpClientFactory;
        public TheCatApiPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// 按品种和分类搜索猫咪图片
        /// </summary>
        /// <param name="limit"></param>
        /// <param name="page"></param>
        /// <param name="breedIds"></param>
        /// <param name="categoryIds"></param>
        /// <returns></returns>
        [KernelFunction]
        [Description("按品种和分类搜索猫咪图片")]
        public Task<string> SearchCatsAsync(
            [Description("分页大小")] int limit = 10,
            [Description("当前页数")] int page = 0,
            [Description("一个或多个品种Id，使用英文逗号隔开，例如：beng,acur")] string breedIds = "",
            [Description("一个或多个分类Id，使用英文逗号隔开，例如：1,14")] string categoryIds = ""
            )
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add(X_API_KEY, API_KEY);
            return httpClient.GetStringAsync($"https://api.thecatapi.com/v1/images/search?limit={limit}&page={page}&breed_ids={breedIds}&category_ids={categoryIds}");
        }

        [KernelFunction]
        [Description("获取分类信息")]
        public Task<string> GetCategoriesAsync()
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add(X_API_KEY, API_KEY);
            return httpClient.GetStringAsync($"https://api.thecatapi.com/v1/categories");
        }

        [KernelFunction]
        [Description("获取品种信息")]
        public Task<string> GetBreedsAsync()
        {
            if (!Validate(out var errorMessages)) throw new Exception(string.Join("", errorMessages));

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add(X_API_KEY, API_KEY);
            return httpClient.GetStringAsync($"https://api.thecatapi.com/v1/breeds");
        }
    }
}
