using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.RAG;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Plugins.Abstration;
using PostgreSQL.Embedding.Plugins.Custom;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "通过网络搜索引擎获取实时信息。支持多种搜索引擎：Bing、Brave、JinaAI、博查、SerpApi、DuckDuckGo。搜索结果可通过 Artifacts 事件展示。", Version = "1.1")]
    public class WebSearchPlugin : BasePlugin
    {
        private Regex _regexCitations = new Regex(@"\[(\d+)\]");
        private const string FINAL_ANSWER_TAG = "[FINAL_ANSWER]";

        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;

        public WebSearchPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        [KernelFunction]
        [Description("使用指定搜索引擎搜索关键词，返回搜索结果摘要或直接生成答案。支持结果过滤和答案生成模式。")]
        public async Task<string> RunAsync(
            Kernel kernel,
            [Description("搜索关键词或问题")] string keyword,
            [Description("搜索引擎名称，可选值：Bing、Brave、JinaAI、BoCha、SerpApi、DuckDuckGo，默认为 Brave")] string searchEngine = "Bing",
            [Description("要排除的域名，使用英文逗号分隔，如：zhihu.com,baidu.com")] string filterDomain = "",
            [Description("是否在 Artifacts 中展示搜索结果列表，默认为 false")] bool showSearchResult = false,
            [Description("是否直接生成答案（启用 RAG 模式），默认为 false")] bool onlyReturnAnswer = false
        )
        {
            var clonedKernel = kernel?.Clone();

            using var serviceScope = _serviceProvider.CreateScope();
            var serviceEngine = GetSearchEngine(serviceScope.ServiceProvider, searchEngine);
            var searchResult = await serviceEngine.SearchAsync(keyword, filterDomain: filterDomain);

            if (showSearchResult) await SendArtifacts(searchResult);
            if (onlyReturnAnswer)
            {
                var memoryService = _serviceProvider.GetService<IMemoryService>();
                var chatHistoryService = _serviceProvider.GetService<IChatHistoriesService>();

                var ragFlowService = new RAGFlowService(clonedKernel, _serviceProvider, memoryService, chatHistoryService);
                var citations = GetCitationsFromSearchEngine(searchResult);
                return await ragFlowService.GenerateAnswerAsync(keyword, citations);
            }

            return JsonConvert.SerializeObject(searchResult);
        }

        public async Task<SearchResult> SearchAsync(string query, string searchEngine = "Bing", bool showSearchResult = false)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var serviceEngine = GetSearchEngine(serviceScope.ServiceProvider, searchEngine);
            var searchResult = await serviceEngine.SearchAsync(query);

            if (showSearchResult) await SendArtifacts(searchResult);
            return searchResult;
        }

        private ISearchEngine GetSearchEngine(IServiceProvider serviceProvider, string searchEngine = "Bing")
        {
            switch (searchEngine)
            {
                case "Bing":
                    return serviceProvider.GetRequiredService<BingSearchPlugin>() as ISearchEngine;
                case "Brave":
                    return serviceProvider.GetRequiredService<BraveSearchPlugin>() as ISearchEngine;
                case "JinaAI":
                    return serviceProvider.GetRequiredService<JinaAIPlugin>() as ISearchEngine;
                case "BoCha":
                    return serviceProvider.GetRequiredService<BoChaAIPlugin>() as ISearchEngine;
                case "SerpApi":
                    return serviceProvider.GetRequiredService<SerpApiPlugin>() as ISearchEngine;
                case "DuckDuckGo":
                    return serviceProvider.GetRequiredService<DuckDuckGoSearchPlugin>() as ISearchEngine;
                default:
                    return serviceProvider.GetRequiredService<BingSearchPlugin>() as ISearchEngine;
            }
        }

        private List<LlmCitationModel> GetCitationsFromSearchEngine(SearchResult searchResult)
        {
            return searchResult.Entries.Select((x, i) => LlmCitationModel.FromSearchEngine(i + 1, x)).ToList();
        }

        private async Task SendArtifacts(SearchResult searchResult)
        {
            if (searchResult == null || !searchResult.Entries.Any()) return;

            var artifact = new LlmArtifactResponseModel("搜索结果", ArtifactType.Search);
            var payloads = searchResult.Entries.Select(x => new
            {
                link = x.Url,
                title = x.Title,
                description = x.Snippet
            });
            artifact.SetData(payloads);
            await EmitArtifactsAsync(artifact);
        }

    }
}
