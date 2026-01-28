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

[KernelPlugin(Description = "一个可以运行 C#、Python、JavaScript 代码的插件")]
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
    [Description("运行 Python 代码并输出结果")]
    public async Task<string> RunPython(
        [Description("脚本内容")] string code, 
        [Description("一个或多个依赖项, 使用英文逗号隔开，例如：pandas,numpy")] string dependencies = ""
    )
    {
        var response = await RunCodeAsync("python3", code, dependencies.Split(','));
        await SendArtifacts(code, response.Output, response.Language, response.ContentType);
        return response.Output;
    }

    [KernelFunction()]
    [Description("运行 JavaScript 代码并输出结果")]
    public async Task<string> RunJavaScript(
        [Description("脚本内容")] string code, 
        [Description("一个或多个依赖项, 使用英文逗号隔开，例如：axios,lodash")] string dependencies = ""
    )
    {
        var response = await RunCodeAsync("javascript", code, dependencies.Split(',', StringSplitOptions.TrimEntries));
        await SendArtifacts(code, response.Output, response.Language, response.ContentType);
        return response.Output;
    }

    [KernelFunction()]
    [Description("运行 C# 代码并输出结果, 你可以使用 csharp、csharp-mono、csharp-sfa 三种后端，对于前者，请使用顶级语句；对于后者，请使用常规语法")]
    public async Task<string> RunCSharp(
        [Description("脚本内容")] string code, 
        [Description("一个或多个依赖项, 使用英文逗号隔开，例如：Newtonsoft.Json")] string dependencies = "", 
        [Description("语言，可选值为：csharp、csharp-mono、csharp-sfa")] string language = "csharp-sfa")
    {
        var response = await RunCodeAsync("csharp", code, dependencies.Split(',', StringSplitOptions.TrimEntries));
        await SendArtifacts(code, response.Output, response.Language, response.ContentType);
        return response.Output;
    }

    [KernelFunction()]
    [Description("运行 Java 代码并输出结果")]
    public async Task<string> RunJava(
    [Description("脚本内容")] string code,
    [Description("一个或多个依赖项, 使用英文逗号隔开，例如：Newtonsoft.Json")] string dependencies = "")
    {
        var response = await RunCodeAsync("java", code, dependencies.Split(',', StringSplitOptions.TrimEntries));
        await SendArtifacts(code, response.Output, response.Language, response.ContentType);
        return response.Output;
    }

    [KernelFunction()]
    [Description("使用 Jupyter Notebook 运行代码，请正确处理 Jupyter Notebook 中中文字符的显示问题")]
    public async Task<string> RunJupyter(
        [Description("脚本内容")] string code, 
        [Description("依赖项, 如有多个，使用英文逗号隔开")] string dependencies = "", 
        [Description("当前语言，可选值为：python、csharp、r")] string language = "python"
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
