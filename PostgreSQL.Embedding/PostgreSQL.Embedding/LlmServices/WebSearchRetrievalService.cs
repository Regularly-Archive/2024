using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.Common.Models.RAG;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.Plugins;

namespace PostgreSQL.Embedding.LlmServices
{
    public class WebSearchRetrievalService
    {
        public RetrievalType RetrievalType => RetrievalType.WebSearch;

        private BingSearchPlugin _bingSearchPlugin;
        public WebSearchRetrievalService(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
        {
            _bingSearchPlugin = new BingSearchPlugin(serviceProvider, httpClientFactory);
        }

        public async Task<List<LlmCitationModel>> SearchAsync(string question, int limit = 5)
        {
            var searchResult = await _bingSearchPlugin.SearchAsync(question, limit);

            return searchResult.Entries.Select((x, i) => 
                LlmCitationModel.FromSearchEngine(i + 1, x.Url, x.Title, x.Snippet)
            ).ToList();
        }
    }
}
