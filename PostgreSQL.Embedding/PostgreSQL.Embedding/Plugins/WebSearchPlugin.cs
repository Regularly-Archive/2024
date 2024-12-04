using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.RAG;
using PostgreSQL.Embedding.Common.Models.Search;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "网络搜索插件，支持以下搜索引擎：必应搜索，Brave 搜索, JianAI, 博查, SerpApi")]
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
        [Description("从网络中搜索信息")]
        public async Task<string> RunAsync([Description("用户请求")] string query, Kernel kernel, [Description("搜索引擎，可选值: Bing, Brave, JianAI, BoCha, SerpApi")] string searchEngine = "Brave", [Description("是否仅搜索答案")] bool searchOnly = false)
        {
            var clonedKernel = kernel.Clone();

            using var serviceScope = _serviceProvider.CreateScope();
            var serviceEngine = GetSearchEngine(serviceScope.ServiceProvider, searchEngine);
            var searchEnginePayload = await serviceEngine.SearchAsync(query);
            var searchResult = JsonConvert.DeserializeObject<SearchResult>(searchEnginePayload);


            var citations = GetLlmCitations(searchResult);
            var jsonFormatContext = JsonConvert.SerializeObject(citations);

            if (searchOnly) return jsonFormatContext;

            var promptTemplateService = serviceScope.ServiceProvider.GetRequiredService<PromptTemplateService>();
            var promptTemplate = promptTemplateService.LoadTemplate("RAGPrompt.txt");
            promptTemplate.AddVariable("name", nameof(BingSearchPlugin));
            promptTemplate.AddVariable("context", jsonFormatContext);
            promptTemplate.AddVariable("question", query);
            promptTemplate.AddVariable("empty_answer", Common.Constants.DefaultEmptyAnswer);
            promptTemplate.AddVariable("histories", string.Empty);

            var chatResult = await promptTemplate.InvokeAsync(kernel);

            var llmResponse = chatResult.GetValue<string>();
            if (llmResponse != null && llmResponse.IndexOf(Common.Constants.DefaultEmptyAnswer) != -1)
            {
                llmResponse = Common.Constants.DefaultEmptyAnswer;
                return llmResponse;
            }
            else
            {
                // 匹配引用信息，对引用信息的索引进行重排
                var index = 0;
                var matchedCitationNumbers = _regexCitations.Matches(llmResponse).Select(x => int.Parse(x.Groups[1].Value)).ToList();
                var newCitationNumbers = new List<LlmCitationMappingModel>();
                foreach (var citationNumber in matchedCitationNumbers)
                {
                    if (!newCitationNumbers.Any(x => x.OriginIndex == citationNumber))
                    {
                        index += 1;
                        newCitationNumbers.Add(new LlmCitationMappingModel() { NewIndex = index, OriginIndex = citationNumber });
                    }
                }

                // 重新生成引用信息
                var generatedCitations = citations.Where(x => matchedCitationNumbers.Contains(x.Index)).Select(x =>
                {
                    var newIndex = newCitationNumbers.FirstOrDefault(k => k.OriginIndex == x.Index).NewIndex;
                    return new LlmCitationModel() { Index = newIndex, Url = x.Url };
                })
                .OrderBy(x => x.Index)
                .Select(x => $"[{x.Index}]: {x.Url}")
                .ToList();

                var markdownFormatContext = string.Join("\r\n", generatedCitations);

                // 更新答案中的引用信息
                foreach (var ciation in newCitationNumbers)
                {
                    llmResponse = llmResponse.Replace($"[{ciation.OriginIndex}]", $"[{ciation.NewIndex}]");
                }

                var answerBuilder = new StringBuilder();
                answerBuilder.AppendLine(llmResponse);
                answerBuilder.AppendLine();
                answerBuilder.AppendLine(markdownFormatContext);

                llmResponse = answerBuilder.ToString();
            }

            return
                $"""
                The following content is sourced from an LLM response. Please keep its format for answers and citations like '<sup>[1]</sup>'.

                {llmResponse}
                """;
        }

        private ISearchEngine GetSearchEngine(IServiceProvider serviceProvider, string searchEngine = "Brave")
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
                default:
                    return serviceProvider.GetRequiredService<BraveSearchPlugin>() as ISearchEngine;
            }
        }

        private List<LlmCitationModel> GetLlmCitations(SearchResult searchResult)
        {
            return searchResult.Entries.Select((x, i) => new LlmCitationModel
            {
                Index = i + 1,
                FileName = string.Empty,
                Relevance = 1.0f,
                Text = $"[^{i + 1}]: {x.Description}",
                Url = x.Url
            }).ToList();
        }

    }
}
