using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Llm.Services;
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
        public async Task<string> AnalyseFromJson(
            [Description("输入的 JSON 数据，通常为数组形式")]string json, 
            [Description("当前执行的数据分析任务")] string task, 
            Kernel kernel
        )
        {
            var promptTemplate = _promptTemplateService.LoadTemplate("DataAnalysis");
            promptTemplate.AddVariable("json_input", json);
            promptTemplate.AddVariable("files_input", string.Empty);
            promptTemplate.AddVariable("task", task);

            var clonedKernel = kernel.Clone();
            var sourceCode = await promptTemplate.InvokeAsync<string>(clonedKernel);
            sourceCode = sourceCode.Replace("```python", "").Replace("```", "").Trim();

            var result = await RunCodeAsync("python", sourceCode, []);
            await SendArtifacts(sourceCode, result.Output, result.ContentType);

            return sourceCode;
        }

        private async Task<RunCodeResponse> RunCodeAsync(string language, string code, string[] dependencies)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(_codeInterpreterConfig.BaseUrl);

            var payload = new { language = language, code = code, dependencies = dependencies, format = "html" };
            var content = JsonContent.Create<dynamic>(payload);

            var response = await httpClient.PostAsync($"/api/jupyter/run", content);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<RunCodeResponse>(body);
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
