using System.ComponentModel;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.Plugins.Abstration;

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
    public async Task<RunCodeResponse> RunPython(
        [Description("要执行的 Python 代码")] string code,
        [Description("Python 依赖包列表，使用英文逗号分隔，如：pandas,numpy")] string dependencies = ""
    )
    {
        return await RunCodeAsync("python3", code, dependencies.Split(','));
    }

    [KernelFunction()]
    [Description("在沙箱中执行 JavaScript 代码并返回执行结果")]
    public async Task<RunCodeResponse> RunJavaScript(
        [Description("要执行的 JavaScript 代码")] string code,
        [Description("NPM 依赖包列表，使用英文逗号分隔，如：axios,lodash")] string dependencies = ""
    )
    {
        return await RunCodeAsync("javascript", code, dependencies.Split(',', StringSplitOptions.TrimEntries));
    }

    [KernelFunction()]
    [Description("在沙箱中执行 C# 代码并返回执行结果。支持三种后端：csharp（顶级语句）、csharp-mono、csharp-sfa（标准 F# 语法）")]
    public async Task<RunCodeResponse> RunCSharp(
        [Description("要执行的 C# 代码")] string code,
        [Description("NuGet 包依赖列表，使用英文逗号分隔，如：Newtonsoft.Json")] string dependencies = "",
        [Description("C# 运行时后端，可选值：csharp、csharp-mono、csharp-sfa，默认为 csharp-sfa")] string language = "csharp-sfa")
    {
        return await RunCodeAsync("csharp", code, dependencies.Split(',', StringSplitOptions.TrimEntries));
    }

    [KernelFunction()]
    [Description("在沙箱中执行 Java 代码并返回执行结果")]
    public async Task<RunCodeResponse> RunJava(
    [Description("要执行的 Java 代码")] string code,
    [Description("Maven 依赖列表，使用英文逗号分隔，如：com.fasterxml.jackson.core:jackson-databind")]
    string dependencies = "")
    {
        return await RunCodeAsync("java", code, dependencies.Split(',', StringSplitOptions.TrimEntries));
    }

    [KernelFunction()]
    [Description("通过 Jupyter Notebook 环境执行代码，支持 Python、C# 和 R 语言，能更好地渲染图表和富文本输出")]
    public async Task<RunCodeResponse> RunJupyter(
        [Description("要执行的代码脚本")] string code,
        [Description("依赖包列表，如有多个使用英文逗号分隔")] string dependencies = "",
        [Description("编程语言，可选值：python、csharp、r，默认为 python")] string language = "python"
    )
    {
        return await RunJupyterAsync(language, code, dependencies.Split(',', StringSplitOptions.TrimEntries));
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
}

public record RunCodeResponse
{
    [JsonProperty("result")]
    public RunCodeResult Result { get; set; }

    [JsonProperty("runtime")]
    public RunCodeRuntime Runtime { get; set; }

    [JsonProperty("project")]
    public RunCodeProject Project { get; set; }
}

public record RunCodeResult
{
    [JsonProperty("output")]
    public string Output { get; set; }

    [JsonProperty("content_type")]
    public string ContentType { get; set; }

    [JsonProperty("duration")]
    public decimal Duration { get; set; }

    [JsonProperty("artifacts")]
    public List<RunCodeArtifact> Artifacts { get; set; }

    [JsonProperty("execution_id")]
    public string ExecutionId { get; set; }
}

public record RunCodeRuntime
{
    [JsonProperty("language")]
    public string Language { get; set; }

    [JsonProperty("environment")]
    public string Environment { get; set; }

    [JsonProperty("version")]
    public string Version { get; set; }

    [JsonProperty("kernel")]
    public string Kernel { get; set; }
}

public record RunCodeProject
{
    [JsonProperty("project_id")]
    public string ProjectId { get; set; }

    [JsonProperty("project_name")]
    public string ProjectName { get; set; }
}

public record RunCodeArtifact
{
    [JsonProperty("filename")]
    public string Filename { get; set; }

    [JsonProperty("mimetype")]
    public string MimeType { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }
}
