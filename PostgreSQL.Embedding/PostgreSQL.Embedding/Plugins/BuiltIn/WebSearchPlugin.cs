using Anthropic.SDK.Messaging;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Domain.Models.RAG;
using PostgreSQL.Embedding.Domain.Models.Search;
using PostgreSQL.Embedding.Llm.Abstractions;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using PostgreSQL.Embedding.Plugins.Custom;
using PostgreSQL.Embedding.Utils;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "通过网络搜索引擎获取实时信息。支持多种搜索引擎：Bing、Brave、JinaAI、博查、SerpApi、DuckDuckGo。搜索结果可通过 Artifacts 事件展示。", Version = "1.2")]
    public class WebSearchPlugin : BasePlugin
    {
        public WebSearchPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {

        }

        [KernelFunction]
        [Description("使用指定搜索引擎搜索关键词，返回搜索结果摘要或直接生成答案。支持结果过滤和答案生成模式。")]
        public async Task<string> RunAsync(
            Kernel kernel,
            [Description("搜索关键词或问题")] string keyword,
            [Description("搜索引擎名称，可选值：Bing、Brave、JinaAI、BoCha、SerpApi、DuckDuckGo, WeChat, Tavily，默认为 Bing")] string searchEngine = "Bing",
            [Description("要包含的域名，使用英文逗号分隔，如：zhihu.com,baidu.com")] string includeDomain = "",
            [Description("是否直接生成答案（启用 RAG 模式），默认为 false")] bool onlyReturnAnswer = false
        )
        {
            var clonedKernel = kernel?.Clone();

            using var serviceScope = _serviceProvider.CreateScope();
            var serviceEngine = GetSearchEngine(serviceScope.ServiceProvider, searchEngine.ToLower());

            var searchResult = await serviceEngine.SearchAsync(keyword, filterDomain: includeDomain);

            if (onlyReturnAnswer && searchEngine.ToLower() != "tavily")
            {
                var memoryService = _serviceProvider.GetService<IMemoryService>();
                var chatHistoryService = _serviceProvider.GetService<IChatHistoriesService>();
                var citationService = _serviceProvider.GetRequiredService<CitationService>();

                var ragFlowService = new RAGFlowService(clonedKernel, _serviceProvider, memoryService, chatHistoryService, citationService);
                var citations = GetCitationsFromSearchEngine(searchResult);
                var ragResult = await ragFlowService.GenerateAnswerAsync(keyword, citations);

                kernel.GetAgentExecutionContext().AddCitations(ragResult.AnswerSources);
                return ragResult.PlainAnswer;
            }
            else if (onlyReturnAnswer && searchEngine.ToLower() == "tavily")
            {
                var answer = searchResult.Entries.FirstOrDefault(x => x.Title == "AI_Summary").Snippet;
                searchResult.Entries = searchResult.Entries.Where(x => x.Title != "AI_Summary").ToList();
                var citations = GetCitationsFromSearchEngine(searchResult);

                kernel.GetAgentExecutionContext().AddCitations(citations);
                return answer;
            }

            return JsonConvert.SerializeObject(searchResult);
        }

        public async Task<SearchResult> SearchAsync(string query, string searchEngine = "Bing", bool showSearchResult = false)
        {
            using var serviceScope = _serviceProvider.CreateScope();
            var serviceEngine = GetSearchEngine(serviceScope.ServiceProvider, searchEngine);
            var searchResult = await serviceEngine.SearchAsync(query);

            return searchResult;
        }

        private ISearchEngine GetSearchEngine(IServiceProvider serviceProvider, string searchEngine = "bing")
        {
            switch (searchEngine)
            {
                case "bing":
                    return serviceProvider.GetRequiredService<BingSearchPlugin>() as ISearchEngine;
                case "brave":
                    return serviceProvider.GetRequiredService<BraveSearchPlugin>() as ISearchEngine;
                case "jinaai":
                    return serviceProvider.GetRequiredService<JinaAIPlugin>() as ISearchEngine;
                case "bocha":
                    return serviceProvider.GetRequiredService<BoChaAIPlugin>() as ISearchEngine;
                case "serpapi":
                    return serviceProvider.GetRequiredService<SerpApiPlugin>() as ISearchEngine;
                case "duckduckgo":
                    return serviceProvider.GetRequiredService<DuckDuckGoSearchPlugin>() as ISearchEngine;
                case "wechat":
                    return serviceProvider.GetRequiredService<WeiXinSearchPlugin>() as ISearchEngine;
                case "tavily":
                    return serviceProvider.GetRequiredService<TavilySearchPlugin>() as ISearchEngine;
                default:
                    return serviceProvider.GetRequiredService<BingSearchPlugin>() as ISearchEngine;
            }
        }

        private List<LlmCitationModel> GetCitationsFromSearchEngine(SearchResult searchResult)
        {
            return searchResult.Entries.Select((x, i) => LlmCitationModel.FromSearchEngine(i + 1, x)).ToList();
        }

        [KernelFunction]
        [Description("请求指定的 URL，返回包含标题、元数据和正文内容的 JSON 结构。支持 HTML 和 Markdown，HTML 使用 CSS 选择器提取正文，Markdown 直接返回内容。")]
        public async Task<WebPageExtractionResult> FetchUrlAsync(
            [Description("要请求的网页 URL，必须是有效的 HTTP/HTTPS 地址")] string url,
            [Description("CSS 选择器，用于定位主要内容区域，如：'article'、'.content'、'#main'。不指定则提取整个 body。")] string? contentSelector = null
        )
        {

            var result = await WebPageExtractor.ExtractWebPageAsync(url, contentSelector);
            return result;
        }

    }
}
