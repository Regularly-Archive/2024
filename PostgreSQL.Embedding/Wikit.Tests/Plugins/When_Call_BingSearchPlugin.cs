using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Plugins;
using Shouldly;

namespace Wikit.Tests.Plugins
{
    public class When_Call_BingSearchPlugin
    {
        private BingSearchPlugin _bingSearchPlugin;
        private IHttpClientFactory _httpClientFactory;
       
        public When_Call_BingSearchPlugin()
        {
            var serviceProvider = new ServiceCollection()
                .AddHttpClient()
                .AddHttpContextAccessor()
                .BuildServiceProvider();

            _httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();  
            _bingSearchPlugin = new BingSearchPlugin(serviceProvider, _httpClientFactory);
        }


        [Fact]
        public async Task It_Should_Search_By_Keywords_Successfully()
        {
            var searchResult = await _bingSearchPlugin.SearchAsync("长安的荔枝 电影 杜甫", 15);
            this.ShouldSatisfyAllConditions(
                () => searchResult.Entries.Count.ShouldBeGreaterThan(15)
            );
        }
    }
}
