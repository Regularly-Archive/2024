using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Common.Streaming;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;

namespace PostgreSQL.Embedding.Plugins.BuiltIn;

[KernelPlugin(Description = "将智能体生成的内容保存为 Artifact（可下载的静态资源），返回可在浏览器中访问的 URL。Artifact 有效期为 3 天。", Version = "2.0")]
public class ArtifactsPlugin : BasePlugin
{
    private readonly string _rootPath;
    private readonly IHttpClientFactory _httpClientFactory;

    public ArtifactsPlugin(IServiceProvider serviceProvider, IWebHostEnvironment env, IHttpClientFactory httpClientFactory) : base(serviceProvider)
    {
        _rootPath = Path.Combine(env.ContentRootPath, "runs");
        _httpClientFactory = httpClientFactory;
    }

    #region 文本与文档

    [KernelFunction]
    [Description("创建纯文本类型的 Artifact（仅下载）。适用于保存日志、配置文件等内容。")]
    public Task<ArtifactResponse> CreateArtifactFromText(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, NewArtifactType.Text, canPreview: false, kernel);
    }

    [KernelFunction]
    [Description("创建 Markdown 类型的 Artifact（可预览）。适用于保存文档、报告等内容。")]
    public Task<ArtifactResponse> CreateArtifactFromMarkdown(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, NewArtifactType.Markdown, canPreview: true, kernel);
    }

    [KernelFunction]
    [Description("创建代码类型的 Artifact（可预览，语法高亮）。适用于保存源代码、脚本等内容。")]
    public Task<ArtifactResponse> CreateArtifactFromCode(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, NewArtifactType.Code, canPreview: true, kernel);
    }

    [KernelFunction]
    [Description("创建 HTML 类型的 Artifact（可预览）。适用于保存生成的网页、报表等内容。")]
    public Task<ArtifactResponse> CreateArtifactFromHtml(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, NewArtifactType.Html, canPreview: true, kernel);
    }

    [KernelFunction]
    [Description("创建 JSON 类型的 Artifact（可预览）。适用于保存结构化数据、API 响应等内容。")]
    public Task<ArtifactResponse> CreateArtifactFromJson(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, NewArtifactType.Json, canPreview: true, kernel);
    }

    #endregion

    #region 数据与表格

    [KernelFunction]
    [Description("创建 CSV 类型的 Artifact（可预览为表格）。适用于保存表格数据、导出内容等。")]
    public Task<ArtifactResponse> CreateArtifactFromCsv(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, NewArtifactType.Csv, canPreview: true, kernel);
    }

    [KernelFunction]
    [Description("创建 SQL 查询结果类型的 Artifact（可预览为表格）。")]
    public Task<ArtifactResponse> CreateArtifactFromSqlResult(
        string jsonData,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(jsonData, fileName, NewArtifactType.Sql_Result, canPreview: true, kernel);
    }

    #endregion

    #region 可执行与多媒体

    [KernelFunction]
    [Description("创建 Jupyter 笔记本类型的 Artifact（可预览，支持代码执行结果展示）。")]
    public Task<ArtifactResponse> CreateArtifactFromJupyter(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, NewArtifactType.Jupyter, canPreview: true, kernel);
    }

    #endregion

    #region 压缩与目录

    [KernelFunction]
    [Description("创建 ZIP 压缩包类型的 Artifact（仅下载）。将指定文件打包为 ZIP。")]
    public Task<ArtifactResponse> CreateArtifactFromZip(
        string filePath,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactFromFileAsync(filePath, fileName, NewArtifactType.Zip, canPreview: false, kernel);
    }

    [KernelFunction]
    [Description("将指定文件夹的内容压缩为 ZIP 文件并创建为 Artifact（仅下载）。")]
    public Task<ArtifactResponse> CreateArtifactFromDirectory(
        string folderPath,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactFromDirectoryInternalAsync(folderPath, fileName, kernel);
    }

    #endregion

    #region URL

    [KernelFunction]
    [Description("从 URL 下载内容并创建为 Artifact（自动推断类型）。适用于将网页、图片、PDF 等转为持久化产物。")]
    public Task<ArtifactResponse> CreateArtifactFromUrl(
        string url,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactFromUrlInternalAsync(url, fileName, kernel);
    }

    #endregion

    #region 内部实现

    private async Task<ArtifactResponse> CreateArtifactInternalAsync(
        string content,
        string fileName,
        NewArtifactType type,
        bool canPreview,
        Kernel kernel)
    {
        var runId = GetRunId(kernel);
        var artifactId = Guid.NewGuid().ToString();

        var filePath = Path.Combine(_rootPath, runId, "artifacts", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);

        var response = new ArtifactResponse
        {
            ArtifactId = artifactId,
            FileName = fileName,
            AccessUrl = $"/api/statics/runs/{runId}/artifacts/{fileName}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            Type = type,
            CanPreview = canPreview,
            CanDownload = true,
            FileSize = content.Length
        };

        await EmitArtifactAsync(response, kernel);
        return response;
    }

    private async Task<ArtifactResponse> CreateArtifactFromFileAsync(
        string sourceFilePath,
        string fileName,
        NewArtifactType type,
        bool canPreview,
        Kernel kernel)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"File not found: {sourceFilePath}");

        var runId = GetRunId(kernel);
        var artifactId = Guid.NewGuid().ToString();

        var filePath = Path.Combine(_rootPath, runId, "artifacts", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await Task.Run(() => File.Copy(sourceFilePath, filePath, overwrite: true));

        var fileInfo = new FileInfo(filePath);
        var response = new ArtifactResponse
        {
            ArtifactId = artifactId,
            FileName = fileName,
            AccessUrl = $"/api/statics/runs/{runId}/artifacts/{fileName}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            Type = type,
            CanPreview = canPreview,
            CanDownload = true,
            FileSize = fileInfo.Length
        };

        await EmitArtifactAsync(response, kernel);
        return response;
    }

    private async Task<ArtifactResponse> CreateArtifactFromDirectoryInternalAsync(
        string folderPath,
        string fileName,
        Kernel kernel)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Directory not found: {folderPath}");

        var runId = GetRunId(kernel);
        var artifactId = Guid.NewGuid().ToString();

        var zipPath = Path.Combine(_rootPath, runId, "artifacts", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        await Task.Run(() => ZipFile.CreateFromDirectory(folderPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false));

        var zipInfo = new FileInfo(zipPath);
        var response = new ArtifactResponse
        {
            ArtifactId = artifactId,
            FileName = fileName,
            AccessUrl = $"/api/statics/runs/{runId}/artifacts/{fileName}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            Type = NewArtifactType.Directory,
            CanPreview = false,
            CanDownload = true,
            FileSize = zipInfo.Length
        };

        await EmitArtifactAsync(response, kernel);
        return response;
    }

    private async Task<ArtifactResponse> CreateArtifactFromUrlInternalAsync(
        string url,
        string fileName,
        Kernel kernel)
    {
        var runId = GetRunId(kernel);
        var artifactId = Guid.NewGuid().ToString();

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ArtifactsPlugin/1.0)");

        using var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        var extension = GetExtensionFromContentType(contentType);
        var actualFileName = fileName ?? $"{artifactId}{extension}";

        var filePath = Path.Combine(_rootPath, runId, "artifacts", actualFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await File.WriteAllBytesAsync(filePath, content);

        var artifactType = GetArtifactTypeFromContentType(contentType);
        var responseData = new ArtifactResponse
        {
            ArtifactId = artifactId,
            FileName = actualFileName,
            AccessUrl = $"/api/statics/runs/{runId}/artifacts/{actualFileName}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            Type = artifactType.type,
            CanPreview = artifactType.canPreview,
            CanDownload = true,
            FileSize = content.Length
        };

        await EmitArtifactAsync(responseData, kernel);
        return responseData;
    }

    private static string GetExtensionFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "application/pdf" => ".pdf",
            "application/zip" => ".zip",
            "text/html" => ".html",
            "text/markdown" => ".md",
            "text/csv" => ".csv",
            "text/plain" => ".txt",
            "application/json" => ".json",
            "application/x-ipynb+json" => ".ipynb",
            "application/vnd.ms-excel" => ".xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            _ => ".bin"
        };
    }

    private static (NewArtifactType type, bool canPreview) GetArtifactTypeFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            var ct when ct.StartsWith("image/") => (NewArtifactType.Image, true),
            "application/pdf" => (NewArtifactType.Pdf, true),
            "application/zip" => (NewArtifactType.Zip, false),
            "text/html" => (NewArtifactType.Html, true),
            "text/markdown" => (NewArtifactType.Markdown, true),
            "text/csv" => (NewArtifactType.Csv, true),
            "text/plain" => (NewArtifactType.Text, false),
            "application/json" => (NewArtifactType.Json, true),
            "application/x-ipynb+json" => (NewArtifactType.Jupyter, true),
            var ct when ct.StartsWith("application/vnd.ms-excel") || ct.StartsWith("application/vnd.openxmlformats") => (NewArtifactType.Excel, true),
            _ => (NewArtifactType.Text, false)
        };
    }

    private string GetRunId(Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var runId = agentExecutionContext.GetRunId() ?? Guid.NewGuid().ToString("N");
        agentExecutionContext.SetRunId(runId);
        return runId;
    }

    private async Task EmitArtifactAsync(ArtifactResponse response, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        if (agentExecutionContext.HasEventBus)
        {
            var @event = new ArtifactEvent
            {
                Artifact = new ArtifactData
                {
                    Id = response.ArtifactId,
                    FileName = response.FileName,
                    AccessUrl = response.AccessUrl,
                    Type = response.Type.ToString().ToLowerInvariant(),
                    CanPreview = response.CanPreview,
                    CanDownload = response.CanDownload,
                    FileSize = response.FileSize,
                    CreatedAt = DateTime.UtcNow
                }
            };
            await agentExecutionContext.PublishEventAsync(@event);
        }
    }

    #endregion

    [KernelFunction]
    [Description("列出当前运行会话中的所有 Artifact，返回访问 URL 列表。")]
    public IEnumerable<ArtifactResponse> ListArtifacts(Kernel kernel)
    {
        var runId = GetRunId(kernel);
        var artifactsPath = Path.Combine(_rootPath, runId, "artifacts");

        if (!Directory.Exists(artifactsPath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(artifactsPath, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            var fileInfo = new FileInfo(file);
            var artifactId = Path.GetFileNameWithoutExtension(file);
            yield return new ArtifactResponse
            {
                ArtifactId = artifactId,
                FileName = fileName,
                AccessUrl = $"/api/statics/runs/{runId}/artifacts/{fileName}",
                ExpiresAt = DateTime.UtcNow.AddDays(3),
                FileSize = fileInfo.Length
            };
        }
    }

    #region 产物类型定义
    public enum NewArtifactType
    {
        Text,
        Markdown,
        Code,
        Html,
        Image,
        Pdf,
        Csv,
        Excel,
        Sql_Result,
        Json,
        Jupyter,
        Zip,
        Directory
    }

    public class ArtifactResponse
    {
        public string ArtifactId { get; set; }
        public string FileName { get; set; }
        public string AccessUrl { get; set; }
        public DateTime ExpiresAt { get; set; }
        public NewArtifactType Type { get; set; }
        public bool CanPreview { get; set; }
        public bool CanDownload { get; set; } = true;
        public long? FileSize { get; set; }
    }
    #endregion
}
