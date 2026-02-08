using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.Xml.Linq;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "arXiv 学术论文检索插件。支持通过关键词或论文 ID 搜索 arXiv 上的学术论文，返回论文标题、作者、摘要、PDF 链接等信息。搜索结果会通过 Artifacts 事件展示。", Version = "1.1")]
    public class ArxivPlugin : BasePlugin
    {
        /// <summary>
        /// 每次最多检索 5 篇论文
        /// </summary>
        private const int MAX_RETRIEVE_NUMBER = 5;

        private readonly XNamespace ATOM_NAMESPACE = "http://www.w3.org/2005/Atom";

        /// <summary>
        /// 提取英文关键词的提示词
        /// </summary>
        private const string EXTRACT_ENGLISH_KEYWORDS_PROMPT =
            """
            请整理出用户希望检索的关键词，关键词必须是英文，如果是英文以外的语言，需要转换为英文，多个关键词请使用英文空格分隔。
            例如：Albert Einstein
            请不要返回任何与英文关键词无关的内容。

            用户输入：{{$input}}
            你整理出的英文检索关键词为：
            """;

        private readonly IHttpClientFactory _httpClientFactory;
        public ArxivPlugin(IHttpClientFactory httpClientFactory, IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("通过关键词搜索 arXiv 学术论文。会自动将关键词翻译为英文，返回匹配的论文列表（标题、作者、摘要、PDF 链接）。")]
        public async Task<string> SearchPapersByKeywordsAsync(
            [Description("搜索关键词，支持中英文，自动转换为英文搜索")] string keywords,
            Kernel kernel,
            [Description("最大返回论文数量，默认为 5")] int max_results = 5)
        {
            var clonedKernel = kernel.Clone();

            var promptTemplate = new CallablePromptTemplate(EXTRACT_ENGLISH_KEYWORDS_PROMPT);
            promptTemplate.AddVariable("input", keywords);
            var functionResult = await promptTemplate.InvokeAsync(kernel);
            var extractedKeywords = functionResult.GetValue<string>();

            // Todo: 支持分页查询
            var papers = await GetPapersAsync($"https://export.arxiv.org/api/query?search_query=all:{extractedKeywords}&max_results={max_results}");
            return JsonConvert.SerializeObject(papers);
        }

        [KernelFunction]
        [Description("通过 arXiv 论文 ID 查询特定论文。返回论文的完整信息（标题、作者、摘要、PDF 链接），单篇论文会触发 PDF 阅读器 Artifact。")]
        public async Task<string> SearchPapersByIdAsync(
            [Description("arXiv 论文 ID，一个或多个，使用英文逗号分隔，如：2310.00001")] string id_list)
        {
            var papers = await GetPapersAsync($"https://export.arxiv.org/api/query?id_list={id_list}");
            return JsonConvert.SerializeObject(papers);
        }

        /// <summary>
        /// 获取论文
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private async Task<IEnumerable<ArxivPaper>> GetPapersAsync(string url)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var feed = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(feed);

            var papers = new List<ArxivPaper>();
            foreach (var entry in doc.Root.Elements(ATOM_NAMESPACE + "entry"))
            {
                var paper = new ArxivPaper
                {
                    Id = entry.Element(ATOM_NAMESPACE + "id").Value.Split('/').LastOrDefault(),
                    Title = FormatValue(entry.Element(ATOM_NAMESPACE + "title").Value),
                    Authors = entry.Elements(ATOM_NAMESPACE + "author").Select(a => a.Element(ATOM_NAMESPACE + "name").Value).ToArray(),
                    Summary = FormatValue(entry.Element(ATOM_NAMESPACE + "summary").Value),
                    Link = entry.Elements(ATOM_NAMESPACE + "link").FirstOrDefault(l => l.Attribute("type")?.Value == "text/html")?.Attribute("href")?.Value,
                    PdfLink = entry.Elements(ATOM_NAMESPACE + "link").FirstOrDefault(l => l.Attribute("type")?.Value == "application/pdf")?.Attribute("href")?.Value
                };
                papers.Add(paper);
            }

            return papers;
        }

        /// <summary>
        /// 字符串格式化
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private string FormatValue(string value) => value.Replace("\r", "").Replace("\n", "").Trim();
    }

    internal class ArxivPaper
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string[] Authors { get; set; }
        public string Summary { get; set; }
        public string Link { get; set; }
        public string PdfLink { get; set; }
    }
}
