using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Plugins;
using Shouldly;

namespace Wikit.Tests.Plugins
{
    public class When_Call_WeiXinSearchPlugin
    {
        private WeiXinSearchPlugin _weixinSearchPlugin;
        private IHttpClientFactory _httpClientFactory;

        public When_Call_WeiXinSearchPlugin()
        {
            var serviceProvider = new ServiceCollection()
                .AddHttpClient()
                .AddHttpContextAccessor()
                .BuildServiceProvider();

            _httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            _weixinSearchPlugin = new WeiXinSearchPlugin(serviceProvider, _httpClientFactory);
        }


        [Fact]
        public async Task It_Should_Search_By_Keywords_Successfully()
        {
            var searchResult = await _weixinSearchPlugin.SearchAsync("张鲁一", 15);
            this.ShouldSatisfyAllConditions(
                () => searchResult.Entries.Count.ShouldBeGreaterThan(15)
            );
        }
    }
}
