using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Moq;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.Planners;
using PostgreSQL.Embedding.Plugins;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            var searchResult = await _braveSearchPlugin.SearchV2Async("blog.yuanpei.me", 15);
            this.ShouldSatisfyAllConditions(
                () => searchResult.Entries.Count.ShouldBeGreaterThan(15)
            );
        }
    }
}
