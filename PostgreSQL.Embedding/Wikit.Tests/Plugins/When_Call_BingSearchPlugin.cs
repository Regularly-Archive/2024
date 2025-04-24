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
            var searchResult = await _bingSearchPlugin.SearchV2Async("元视角", 15);
            this.ShouldSatisfyAllConditions(
                () => searchResult.Entries.Count.ShouldBeGreaterThan(15)
            );
        }
    }
}
