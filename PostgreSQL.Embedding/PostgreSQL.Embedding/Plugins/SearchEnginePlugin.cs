using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models.RAG;
using PostgreSQL.Embedding.Common.Models.Search;
using PostgreSQL.Embedding.LlmServices;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "搜索引擎插件，支持以下搜索引擎：必应搜索，Brave 搜索, JianAI")]
    public class SearchEnginePlugin
    {
        private Regex _regexCitations = new Regex(@"\[\^(\d+)\]");
        private const string FINAL_ANSWER_TAG = "[FINAL_ANSWER]";

        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;

        public SearchEnginePlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory)
        {
            _serviceProvider = serviceProvider;
        }

        [KernelFunction]
        [Description("基于搜索引擎的检索方案")]
        private async Task<string> RunAsync([Description("用户请求")] string query, Kernel kernel, [Description("搜索引擎，可选值: Bing, Brave, JianAI")] string searchEngine = "Brave")
        {
            var clonedKernel = kernel.Clone();

            using var serviceScope = _serviceProvider.CreateScope();
            var serviceEngineProvider = GetSearchEngineProvider(serviceScope.ServiceProvider, searchEngine);
            var searchEnginePayload = await serviceEngineProvider.SearchAsync(query);
            var searchResult = JsonConvert.DeserializeObject<SearchResult>(searchEnginePayload);

            var citations = GetLlmCitations(searchResult);
            var jsonFormatContext = JsonConvert.SerializeObject(citations);

            var promptTemplateService = serviceScope.ServiceProvider.GetRequiredService<PromptTemplateService>();
            var promptTemplate = promptTemplateService.LoadTemplate("RAGPrompt.txt");
            promptTemplate.AddVariable("name", nameof(SearchEnginePlugin));
            promptTemplate.AddVariable("context", jsonFormatContext);
            promptTemplate.AddVariable("question", query);
            promptTemplate.AddVariable("empty_answer", Common.Constants.DefaultEmptyAnswer);
            promptTemplate.AddVariable("histories", string.Empty);

            var chatResult = await promptTemplate.InvokeAsync(kernel);

            var llmResponse = chatResult.GetValue<string>();
            if (llmResponse != null && llmResponse.IndexOf(Common.Constants.DefaultEmptyAnswer) != -1)
            {
                llmResponse = Common.Constants.DefaultEmptyAnswer;
            }
            else
            {
                var citationNumbers = _regexCitations.Matches(llmResponse).Select(x => int.Parse(x.Groups[1].Value));
                var markdownFormatContext = string.Join("\r\n", citations.Where(x => citationNumbers.Contains(x.Index)).Select(x => $"[^{x.Index}]: {x.Url}"));

                var answerBuilder = new StringBuilder();
                answerBuilder.AppendLine(llmResponse);
                answerBuilder.AppendLine();
                answerBuilder.AppendLine(markdownFormatContext);

                llmResponse = answerBuilder.ToString();
            }

            return $"{FINAL_ANSWER_TAG} {llmResponse}";
        }

        private ISearchEngineProvider GetSearchEngineProvider(IServiceProvider serviceProvider, string searchEngine = "Brave")
        {
            switch (searchEngine)
            {
                case "Bing":
                    return serviceProvider.GetService<BingSearchPlugin>() as ISearchEngineProvider;
                case "Brave":
                    return serviceProvider.GetService<BraveSearchPlugin>() as ISearchEngineProvider;
                case "JinaAI":
                    return serviceProvider.GetService<JinaAIPlugin>() as ISearchEngineProvider;
                default:
                    return serviceProvider.GetService<BraveSearchPlugin>() as ISearchEngineProvider;
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
