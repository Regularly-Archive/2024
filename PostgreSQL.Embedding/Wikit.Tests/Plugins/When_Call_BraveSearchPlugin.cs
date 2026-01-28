using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Plugins.Custom;
using Shouldly;

namespace Wikit.Tests.Plugins
{
    public class When_Call_BraveSearchPlugin
    {
        private BraveSearchPlugin _braveSearchPlugin;
        private IHttpClientFactory _httpClientFactory;

        public When_Call_BraveSearchPlugin()
        {
            var serviceProvider = new ServiceCollection()
                .AddHttpClient()
                .AddHttpContextAccessor()
                .BuildServiceProvider();

            _httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            _braveSearchPlugin = new BraveSearchPlugin(serviceProvider, _httpClientFactory);
        }


        [Fact]
        public async Task It_Should_Search_By_Keywords_Successfully()
        {
            var searchResult = await _braveSearchPlugin.SearchAsync("blog.yuanpei.me", 15);
            this.ShouldSatisfyAllConditions(
                () => searchResult.Entries.ShouldNotBeEmpty()
            );
        }
    }
}
