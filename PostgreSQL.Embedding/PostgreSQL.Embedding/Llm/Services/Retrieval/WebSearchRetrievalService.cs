using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Models.RAG;
using PostgreSQL.Embedding.Plugins.BuiltIn;

namespace PostgreSQL.Embedding.Llm.Services.Retrieval;

public class WebSearchRetrievalService 
{ 
    public RetrievalType RetrievalType => RetrievalType.WebSearch;
    public string SearchEngine { get; set; } = "Bing";

    private WebSearchPlugin _webSearchPlugin;
    public WebSearchRetrievalService(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
    {
        _webSearchPlugin = new WebSearchPlugin(serviceProvider, httpClientFactory);
    }

    public async Task<List<LlmCitationModel>> SearchAsync(long knowledgeBaseId, string question, double minRelevance, int limit)
    {
        var searchResult = await _webSearchPlugin.SearchAsync(question, SearchEngine, showSearchResult: true);

        return searchResult.Entries.Select((x, i) =>
            LlmCitationModel.FromSearchEngine(i + 1, x)
        ).ToList();
    }
}
