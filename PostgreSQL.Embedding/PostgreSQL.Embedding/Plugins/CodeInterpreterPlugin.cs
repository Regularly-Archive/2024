using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "一个可以运行 C#、Python、JavaScript 代码的插件")]
    public class CodeInterpreterPlugin : BasePlugin
    {
        private ILogger<CodeInterpreterPlugin> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CodeInterpreterConfig _codeInterpreterConfig;

        private const string NO_RETURN_VALUE = "There is no return value for the current action, please proceed.";

        public CodeInterpreterPlugin(IServiceProvider serviceProvider, IOptions<CodeInterpreterConfig> options, IHttpClientFactory httpClientFactory)
            : base(serviceProvider)
        {
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            _logger = loggerFactory.CreateLogger<CodeInterpreterPlugin>();

            _codeInterpreterConfig = options.Value;
            _httpClientFactory = httpClientFactory;
        }

        [KernelFunction]
        [Description("运行 Python 代码并输出结果")]
        public async Task<string> RunPython([Description("脚本内容")] string code)
        {
            var response = await RunCodeAsync("python3", code);
            var output = JObject.Parse(response)["output"]?.Value<string>();
            await SendArtifacts(code, output, "python");
            return output;
        }

        [KernelFunction()]
        [Description("运行 JavaScript 代码并输出结果")]
        public async Task<string> RunJavaScript([Description("脚本内容")] string code)
        {
            var response = await RunCodeAsync("javascript", code);
            var output = JObject.Parse(response)["output"]?.Value<string>();
            await SendArtifacts(code, output, "javascript");
            return output;
        }

        [KernelFunction()]
        [Description("运行 C# 代码并输出结果, 你可以使用 csharp 和 csharp-mono 两种后端，对于前者，请使用顶级语句；对于后者，请使用常规语法")]
        public async Task<string> RunCSharp([Description("脚本内容")] string code, [Description("后端支持")] string backend = "csharp")
        {
            var response = await RunCodeAsync("csharp", code);
            var output = JObject.Parse(response)["output"]?.Value<string>();
            await SendArtifacts(code, output, "csharp");
            return output;
        }

        private async Task<string> RunCodeAsync(string language, string code)
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.BaseAddress = new Uri(_codeInterpreterConfig.BaseUrl);

            var payload = new { language = language, code = code, notebook = false };
            var content = JsonContent.Create<dynamic>(payload);

            var response = await httpClient.PostAsync("/api/run", content);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            return body;
        }

        private async Task SendArtifacts(string code, string output, string language)
        {
            var payload = new { code = code, output = output, language = language };
            var artifacts = new LlmArtifactResponseModel("代码解释器", ArtifactType.Code);
            artifacts.SetData(payload);
            await EmitArtifactsAsync(artifacts);
        }
    }
}
