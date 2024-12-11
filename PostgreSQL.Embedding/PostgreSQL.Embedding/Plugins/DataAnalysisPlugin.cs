using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "一个用于数据分析及可视化的插件")]
    public class DataAnalysisPlugin : BasePlugin
    {
        private readonly PromptTemplateService _promptTemplateService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CodeInterpreterConfig _codeInterpreterConfig;
        public DataAnalysisPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory, IOptions<CodeInterpreterConfig> options)
            : base(serviceProvider)
        {
            _promptTemplateService = serviceProvider.GetService<PromptTemplateService>();
            _httpClientFactory = httpClientFactory;
            _codeInterpreterConfig = options.Value;
        }

        [KernelFunction]
        [Description("加载 JSON 数据并完成数据分析与可视化")]
        public async Task<string> AnalyseFromJson([Description("输入的 JSON 数据，通常为数组形式")]string json, [Description("当前执行的数据分析任务")] string task, Kernel kernel)
        {
            var promptTemplate = _promptTemplateService.LoadTemplate("DataAnalysis.txt");
            promptTemplate.AddVariable("json_input", json);
            promptTemplate.AddVariable("files_input", string.Empty);
            promptTemplate.AddVariable("task", task);

            var clonedKernel = kernel.Clone();
            var sourceCode = await promptTemplate.InvokeAsync<string>(clonedKernel);
            sourceCode = sourceCode.Replace("```python", "").Replace("```", "").Trim();

            var result = await RunCodeAsync("jupyter-python3", sourceCode);
            var previewCode = JObject.Parse(result)["output"].Value<string>();
            var previewType = JObject.Parse(result)["type"].Value<string>();
            await SendArtifacts(sourceCode, previewCode, previewType);

            return sourceCode;
        }

        private async Task<string> RunCodeAsync(string language, string code)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(_codeInterpreterConfig.BaseUrl);

            var payload = new { language = language, code = code, notebook = true };
            var content = JsonContent.Create<dynamic>(payload);

            var response = await httpClient.PostAsync("/api/run?format=html", content);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            return body;
        }

        private async Task SendArtifacts(string sourceCode, string previewCode, string previewType)
        {
            var payload = new { sourceCode = sourceCode, previewCode = previewCode, previewType = previewType };
            var artifacts = new LlmArtifactResponseModel("数据分析", ArtifactType.DataAnalysis);
            artifacts.SetData(payload);
            await EmitArtifactsAsync(artifacts);
        }
    }
}
