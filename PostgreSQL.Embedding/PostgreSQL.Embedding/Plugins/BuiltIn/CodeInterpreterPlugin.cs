using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn;

[KernelPlugin(Description = "在沙箱环境中运行多种编程语言的代码（Python、JavaScript、C#、Java）。执行结果和代码会通过 Artifacts 事件返回。", Version = "1.1")]
public class CodeInterpreterPlugin : BasePlugin
{
    private ILogger<CodeInterpreterPlugin> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CodeInterpreterConfig _codeInterpreterConfig;

    public CodeInterpreterPlugin(IServiceProvider serviceProvider, IOptions<CodeInterpreterConfig> options, IHttpClientFactory httpClientFactory)
        : base(serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger<CodeInterpreterPlugin>();

        _codeInterpreterConfig = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    [KernelFunction]
    [Description("在沙箱中执行 Python 3 代码并返回执行结果")]
    public async Task<string> RunPython(
        [Description("要执行的 Python 代码")] string code,
        [Description("Python 依赖包列表，使用英文逗号分隔，如：pandas,numpy")] string dependencies = ""
    )
    {
        var response = await RunCodeAsync("python3", code, dependencies.Split(','));
        await SendArtifacts(code, response.Output, response.Language, response.ContentType);
        return response.Output;
    }

    [KernelFunction()]
    [Description("在沙箱中执行 JavaScript 代码并返回执行结果")]
    public async Task<string> RunJavaScript(
        [Description("要执行的 JavaScript 代码")] string code,
        [Description("NPM 依赖包列表，使用英文逗号分隔，如：axios,lodash")] string dependencies = ""
    )
    {
        var response = await RunCodeAsync("javascript", code, dependencies.Split(',', StringSplitOptions.TrimEntries));
        await SendArtifacts(code, response.Output, response.Language, response.ContentType);
        return response.Output;
    }

    [KernelFunction()]
    [Description("在沙箱中执行 C# 代码并返回执行结果。支持三种后端：csharp（顶级语句）、csharp-mono、csharp-sfa（标准 F# 语法）")]
    public async Task<string> RunCSharp(
        [Description("要执行的 C# 代码")] string code,
        [Description("NuGet 包依赖列表，使用英文逗号分隔，如：Newtonsoft.Json")] string dependencies = "",
        [Description("C# 运行时后端，可选值：csharp、csharp-mono、csharp-sfa，默认为 csharp-sfa")] string language = "csharp-sfa")
    {
        var response = await RunCodeAsync("csharp", code, dependencies.Split(',', StringSplitOptions.TrimEntries));
        await SendArtifacts(code, response.Output, response.Language, response.ContentType);
        return response.Output;
    }

    [KernelFunction()]
    [Description("在沙箱中执行 Java 代码并返回执行结果")]
    public async Task<string> RunJava(
    [Description("要执行的 Java 代码")] string code,
    [Description("Maven 依赖列表，使用英文逗号分隔，如：com.fasterxml.jackson.core:jackson-databind")]
    string dependencies = "")
    {
        var response = await RunCodeAsync("java", code, dependencies.Split(',', StringSplitOptions.TrimEntries));
        await SendArtifacts(code, response.Output, response.Language, response.ContentType);
        return response.Output;
    }

    [KernelFunction()]
    [Description("通过 Jupyter Notebook 环境执行代码，支持 Python、C# 和 R 语言，能更好地渲染图表和富文本输出")]
    public async Task<string> RunJupyter(
        [Description("要执行的代码脚本")] string code,
        [Description("依赖包列表，如有多个使用英文逗号分隔")] string dependencies = "",
        [Description("编程语言，可选值：python、csharp、r，默认为 python")] string language = "python"
    )
    {
        var response = await RunJupyterAsync(language, code, dependencies.Split(',', StringSplitOptions.TrimEntries));
        await SendArtifacts(code, response.Output, response.Language, response.ContentType);
        return response.Output;
    }

    /// <summary>
    /// 在沙箱中运行代码
    /// </summary>
    /// <param name="language"></param>
    /// <param name="code"></param>
    /// <param name="dependencies"></param>
    /// <returns></returns>
    private async Task<RunCodeResponse> RunCodeAsync(string language, string code, string[] dependencies)
    {
        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(_codeInterpreterConfig.BaseUrl);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        var payload = new { language = language, code = code, dependencies = dependencies  };
        var content = JsonContent.Create<dynamic>(payload);

        var response = await httpClient.PostAsync($"/api/code/run", content);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<RunCodeResponse>(body);
    }

    /// <summary>
    /// 通过 Jupyter Notebook 运行代码
    /// </summary>
    /// <param name="language"></param>
    /// <param name="code"></param>
    /// <param name="dependencies"></param>
    /// <param name="format"></param>
    /// <returns></returns>
    private async Task<RunCodeResponse> RunJupyterAsync(string language, string code, string[] dependencies, string format = "notebook")
    {
        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.BaseAddress = new Uri(_codeInterpreterConfig.BaseUrl);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        var payload = new { language = language, code = code, dependencies = dependencies, format = format };
        var content = JsonContent.Create<dynamic>(payload);

        var response = await httpClient.PostAsync($"/api/jupyter/run", content);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<RunCodeResponse>(body);
    }

    private async Task SendArtifacts(string code, string output, string language, string contentType = "text/plain")
    {
        var payload = new { code = code, output = output, contentType = contentType, language = language };
        var artifacts = new LlmArtifactResponseModel("代码解释器", ArtifactType.Code);
        artifacts.SetData(payload);
        await EmitArtifactsAsync(artifacts);
    }
}

record RunCodeResponse
{
    [JsonProperty("output")]
    public string Output { get; set; }

    [JsonProperty("contentType")]
    public string ContentType { get; set; }

    [JsonProperty("duration")]
    public decimal Duration { get; set; }

    [JsonProperty("language")]
    public string Language { get; set; }
}
