using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Common.Streaming;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Infrastructure.UserIdentity;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PostgreSQL.Embedding.Plugins.BuiltIn;

[KernelPlugin(Description = "将智能体生成的内容保存为 Artifact（可下载的静态资源），返回可在浏览器中访问的 URL。Artifact 有效期为 3 天。", Version = "2.1")]
public class ArtifactsPlugin : BasePlugin
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICurrentUserService _currentUserService;
    private readonly IRepository<ChatMessageArtifact> _artifactRepository;

    public ArtifactsPlugin(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory, ICurrentUserService currentUserService)
        : base(serviceProvider)
    {
        _httpClientFactory = httpClientFactory;
        _currentUserService = currentUserService;
        _artifactRepository = serviceProvider.GetRequiredService<IRepository<ChatMessageArtifact>>();
    }

    #region 文本与文档

    [KernelFunction]
    [Description("创建纯文本类型的 Artifact（仅下载）。适用于保存日志、配置文件等内容。")]
    public Task<ArtifactResponse> CreateArtifactFromText(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, ArtifactType.Text, canPreview: false, kernel);
    }

    [KernelFunction]
    [Description("创建 Markdown 类型的 Artifact（可预览）。适用于保存文档、报告等内容。")]
    public Task<ArtifactResponse> CreateArtifactFromMarkdown(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, ArtifactType.Markdown, canPreview: true, kernel);
    }

    [KernelFunction]
    [Description("创建代码类型的 Artifact（可预览，语法高亮）。适用于保存源代码、脚本等内容。")]
    public Task<ArtifactResponse> CreateArtifactFromCode(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, ArtifactType.Code, canPreview: true, kernel);
    }

    [KernelFunction]
    [Description("创建 HTML 类型的 Artifact（可预览）。适用于保存生成的网页、报表等内容。")]
    public Task<ArtifactResponse> CreateArtifactFromHtml(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, ArtifactType.Html, canPreview: true, kernel);
    }

    [KernelFunction]
    [Description("创建 JSON 类型的 Artifact（可预览）。适用于保存结构化数据、API 响应等内容。")]
    public Task<ArtifactResponse> CreateArtifactFromJson(
        string content,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(content, fileName, ArtifactType.Json, canPreview: true, kernel);
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
        return CreateArtifactInternalAsync(content, fileName, ArtifactType.Csv, canPreview: true, kernel);
    }

    [KernelFunction]
    [Description("创建 SQL 查询结果类型的 Artifact（可预览为表格）。")]
    public Task<ArtifactResponse> CreateArtifactFromSqlResult(
        string jsonData,
        string fileName,
        Kernel kernel)
    {
        return CreateArtifactInternalAsync(jsonData, fileName, ArtifactType.Sql_Result, canPreview: true, kernel);
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
        return CreateArtifactInternalAsync(content, fileName, ArtifactType.Jupyter, canPreview: true, kernel);
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
        return CreateArtifactFromFileAsync(filePath, fileName, ArtifactType.Zip, canPreview: false, kernel);
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

    #region 图表
    [KernelFunction]
    [Description("创建 Mermaid 类型的 Artifact（可下载，可预览）。适用于简单的图表和可视化场景。")]
    public Task<ArtifactResponse> CreateArtifactFromMermaid(
        string content,
        string fileName,
        Kernel kernel)
    {
        content = content.Replace("```mermaid", "").Replace("```", "").TrimStart();
        return CreateArtifactInternalAsync(content, fileName, ArtifactType.Mermaid, true, kernel);
    }
    #endregion

    #region 类型查询

    /// <summary>
    /// 产物类型元信息
    /// </summary>
    public class ArtifactTypeInfo
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public bool CanPreview { get; set; }
        public bool CanDownload { get; set; }
        public string[] Extensions { get; set; }
    }

    [KernelFunction]
    [Description("获取系统支持的所有 Artifact 类型及其说明，用于了解可以使用哪些类型的产物。")]
    public List<ArtifactTypeInfo> GetSupportedArtifactTypes()
    {
        return new List<ArtifactTypeInfo>
        {
            new() { Type = "Text", Description = "纯文本文件", CanPreview = false, CanDownload = true, Extensions = new[] { ".txt" } },
            new() { Type = "Markdown", Description = "Markdown 文档，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".md" } },
            new() { Type = "Code", Description = "代码文件，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".cs", ".js", ".py", ".java", ".go", ".rs", ".ts", ".cpp", ".c" } },
            new() { Type = "Html", Description = "HTML 页面，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".html", ".htm" } },
            new() { Type = "Image", Description = "图片文件，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" } },
            new() { Type = "Video", Description = "视频文件，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".mp4", ".webm", ".ogg" } },
            new() { Type = "Audio", Description = "音频文件，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".mp3", ".wav", ".ogg" } },
            new() { Type = "Pdf", Description = "PDF 文档，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".pdf" } },
            new() { Type = "Csv", Description = "CSV 表格文件，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".csv" } },
            new() { Type = "Excel", Description = "Excel 电子表格，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".xls", ".xlsx" } },
            new() { Type = "Json", Description = "JSON 数据，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".json" } },
            new() { Type = "Jupyter", Description = "Jupyter Notebook，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".ipynb" } },
            new() { Type = "Sql_Result", Description = "SQL 查询结果，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".json" } },
            new() { Type = "Mermaid", Description = "Mermaid 图表，可预览", CanPreview = true, CanDownload = true, Extensions = new[] { ".mmd", ".md" } },
            new() { Type = "Zip", Description = "ZIP 压缩包", CanPreview = false, CanDownload = true, Extensions = new[] { ".zip" } },
            new() { Type = "Directory", Description = "目录", CanPreview = false, CanDownload = true, Extensions = new[] { "" } }
        };
    }

    #endregion

    #region 内部实现

    private async Task<ArtifactResponse> CreateArtifactInternalAsync(
        string content,
        string fileName,
        ArtifactType type,
        bool canPreview,
        Kernel kernel)
    {
        var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();

        var filePath = Path.Combine(sandboxContext.ArtifactsDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);

        var response = new ArtifactResponse
        {
            ArtifactId = Path.GetFileNameWithoutExtension(filePath),
            FileName = fileName,
            AccessUrl = GetArtifactAccessUrl(kernel, fileName),
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
        ArtifactType type,
        bool canPreview,
        Kernel kernel)
    {
        var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
        var resolvedPath = sandboxContext.ToLocalPath(sourceFilePath);

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"File not found: {sourceFilePath}");

        var filePath = Path.Combine(sandboxContext.ArtifactsDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await Task.Run(() => File.Copy(resolvedPath, filePath, overwrite: true));

        var fileInfo = new FileInfo(filePath);
        var response = new ArtifactResponse
        {
            ArtifactId = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            AccessUrl = GetArtifactAccessUrl(kernel, fileName),
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

        var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();

        var zipPath = Path.Combine(sandboxContext.ArtifactsDir, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        await Task.Run(() => ZipFile.CreateFromDirectory(folderPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false));

        var zipInfo = new FileInfo(zipPath);
        var response = new ArtifactResponse
        {
            ArtifactId = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            AccessUrl = GetArtifactAccessUrl(kernel, fileName),
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            Type = ArtifactType.Directory,
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
        var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ArtifactsPlugin/1.0)");

        using var response = await httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsByteArrayAsync();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        var extension = GetExtensionFromContentType(contentType);
        var actualFileName = fileName ?? $"{Guid.NewGuid().ToString("N")}{extension}";

        var filePath = Path.Combine(sandboxContext.ArtifactsDir, actualFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await File.WriteAllBytesAsync(filePath, content);

        var artifactType = GetArtifactTypeFromContentType(contentType);
        var responseData = new ArtifactResponse
        {
            ArtifactId = Path.GetFileNameWithoutExtension(fileName),
            FileName = actualFileName,
            AccessUrl = GetArtifactAccessUrl(kernel, actualFileName),
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
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/ogg" => ".ogg",
            "audio/mpeg" => ".mp3",
            "audio/wav" => ".wav",
            "audio/ogg" => ".ogg",
            "audio/webm" => ".webm",
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

    private static (ArtifactType type, bool canPreview) GetArtifactTypeFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            var ct when ct.StartsWith("image/") => (ArtifactType.Image, true),
            var ct when ct.StartsWith("video/") => (ArtifactType.Video, true),
            var ct when ct.StartsWith("audio/") => (ArtifactType.Audio, true),
            "application/pdf" => (ArtifactType.Pdf, true),
            "application/zip" => (ArtifactType.Zip, false),
            "text/html" => (ArtifactType.Html, true),
            "text/markdown" => (ArtifactType.Markdown, true),
            "text/csv" => (ArtifactType.Csv, true),
            "text/plain" => (ArtifactType.Text, false),
            "application/json" => (ArtifactType.Json, true),
            "application/x-ipynb+json" => (ArtifactType.Jupyter, true),
            var ct when ct.StartsWith("application/vnd.ms-excel") || ct.StartsWith("application/vnd.openxmlformats") => (ArtifactType.Excel, true),
            _ => (ArtifactType.Text, false)
        };
    }

    private async Task EmitArtifactAsync(ArtifactResponse response, Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();

        // 持久化产物
        await _artifactRepository.AddAsync(new ChatMessageArtifact
        {
            RunId = agentExecutionContext.GetRunId(),
            MessageId = agentExecutionContext.GetMessageId(),
            ConversationId = agentExecutionContext.GetConversationId(),
            ArtifactId = response.ArtifactId,
            FileName = response.FileName,
            ArtifactType = (int)response.Type,
            Url = response.AccessUrl,
            CanPreview = response.CanPreview,
            CanDownload = response.CanDownload,
            FileSize = response.FileSize
        });

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

    /// <summary>
    /// 读取文本类型的 Artifact 内容
    /// </summary>
    [KernelFunction]
    [Description("通过 ArtifactId 读取文本类型 Artifact 的内容。支持的格式：文本、Markdown、代码、HTML、JSON、CSV、SQL 结果。")]
    public async Task<string> ReadArtifactAsync(
        [Description("ArtifactId")] string artifactId,
        Kernel kernel)
    {
        // 从数据库查询 Artifact
        var artifact = await _artifactRepository.FindAsync(x => x.ArtifactId == artifactId || x.FileName == artifactId);
        if (artifact == null)
            throw new ArgumentException($"The Artifact not found: {artifactId}");

        var filePath = GetArtifactFilePath(kernel, artifact.RunId, artifact.FileName);
        if (!File.Exists(filePath))
            throw new ArgumentException($"The Artifact not found: {artifactId}");

        return await File.ReadAllTextAsync(filePath);
    }

    [KernelFunction]
    [Description("列出当前会话中的所有 Artifact，返回访问 URL 列表。")]
    public async Task<IEnumerable<ArtifactResponse>> ListArtifacts(Kernel kernel)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var conversationId = agentExecutionContext.GetConversationId();

        // 从数据库查询当前会话的所有 Artifact
        var artifacts = await _artifactRepository.FindListAsync(x => x.ConversationId == conversationId);

        return artifacts.Select(artifact => new ArtifactResponse
        {
            ArtifactId = artifact.ArtifactId,
            FileName = artifact.FileName,
            AccessUrl = artifact.Url ?? "",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            Type = (ArtifactType)artifact.ArtifactType,
            CanPreview = artifact.CanPreview,
            CanDownload = artifact.CanDownload,
            FileSize = artifact.FileSize
        });
    }

    private string GetArtifactAccessUrl(Kernel kernel, string fileName)
    {
        var agentExecutionContext = kernel.GetAgentExecutionContext();
        var currentUser = _currentUserService.GetCurrentIdentityAsync().GetAwaiter().GetResult();

        var appId = agentExecutionContext.GetAppId();
        var conversationId = agentExecutionContext.GetConversationId();
        var runId = agentExecutionContext.GetRunId();

        var relativeUrl = $"/api/statics/{currentUser.Id}/{appId}/conversations/{conversationId}/runs/{runId}/artifacts/{fileName}";
        var baseUrl = GetBaseUrl();
        return string.IsNullOrEmpty(baseUrl) ? relativeUrl : $"{baseUrl}{relativeUrl}";
    }

    private string GetArtifactFilePath(Kernel kernel, string runId, string fileName)
    {
        var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
        return Path.Combine(sandboxContext.SessionDir, "runs", runId, "artifacts", fileName);
    }


    #region 产物类型定义
    public enum ArtifactType
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
        Directory,
        Mermaid,
        Audio,
        Video
    }

    public class ArtifactResponse
    {
        [JsonProperty("artifact_id")]
        public string ArtifactId { get; set; }
        public string FileName { get; set; }
        public string AccessUrl { get; set; }
        public DateTime ExpiresAt { get; set; }
        public ArtifactType Type { get; set; }
        public bool CanPreview { get; set; }
        public bool CanDownload { get; set; } = true;
        public long? FileSize { get; set; }
    }
    #endregion
}
