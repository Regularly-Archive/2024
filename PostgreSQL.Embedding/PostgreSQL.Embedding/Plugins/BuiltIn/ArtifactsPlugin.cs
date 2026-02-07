using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;
using System.IO.Compression;
using System.Text;

namespace PostgreSQL.Embedding.Plugins.BuiltIn;

[KernelPlugin(Description = "将智能体生成的文件内容保存为可访问的 Artifact（静态资源），返回可在浏览器中访问的 URL。Artifact 有效期为 3 天。", Version = "1.1")]
public class ArtifactsPlugin : BasePlugin
{
    private readonly string _rootPath;
    public ArtifactsPlugin(IServiceProvider serviceProvider, IWebHostEnvironment env) : base(serviceProvider)
    {
        _rootPath = Path.Combine(env.ContentRootPath, "runs");
    }

    [KernelFunction]
    [Description("创建文本类型的 Artifact（文件或纯文本），返回包含访问 URL 的响应对象。可用于保存生成的代码、报告等内容。")]
    public async Task<CreateArtifactResponse> CreateArtifactAsync(
        [Description("Artifact 类型，可选值：file、text，默认为 file")] string type,
        [Description("要保存的文本内容，UTF-8 编码")] string content,
        [Description("文件名称，用于标识存储的文件，建议包含扩展名，如：report.html")] string fileName,
        [Description("MIME 类型，如：text/plain、text/html、application/json")] string contentType,
        Kernel kernel
    )
    {
        var agentExecutionContext = kernel.Services.GetService<AgentExecutionContext>();
        var runId = agentExecutionContext.GetRunId() ?? Guid.NewGuid().ToString("N");
        agentExecutionContext.SetRunId(runId);
        var artifactId = Guid.NewGuid().ToString();

         
        var filePath = Path.Combine(_rootPath, runId, "artifacts", artifactId, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);


        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
        return new CreateArtifactResponse
        {
            ArtifactId = artifactId,
            ArtifactName = fileName,
            AccessURL = $"/api/statics/runs/{runId}/artifacts/{artifactId}/{fileName}",
            ExpiresAt = DateTime.UtcNow.AddDays(3)
        };
    }

    [KernelFunction]
    [Description("将指定文件夹的内容压缩为 ZIP 文件并创建为 Artifact。返回 ZIP 文件的访问 URL。")]
    public CreateArtifactResponse CreateCompressedArtifactAsync(
        [Description("要压缩的文件夹绝对路径，仅文件夹内容会被包含（不含文件夹本身）")] string folderPath,
        [Description("生成的 ZIP 文件名称，如：archive.zip")] string fileName,
        [Description("Artifact 类型，默认为 zip")] string type,
        [Description("MIME 类型，ZIP 文件使用 application/zip")] string contentType,
        Kernel kernel
    )
    {
        if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        var agentExecutionContext = kernel.Services.GetService<AgentExecutionContext>();
        var runId = agentExecutionContext.GetRunId() ?? Guid.NewGuid().ToString("N");
        agentExecutionContext.SetRunId(runId);
        var artifactId = Guid.NewGuid().ToString();

        var zipPath = Path.Combine(_rootPath, runId, "artifacts", artifactId, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
       
        ZipFile.CreateFromDirectory(folderPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        return new CreateArtifactResponse
        {
            ArtifactId = artifactId,
            ArtifactName = fileName,
            AccessURL = $"/api/statics/runs/{runId}/artifacts/{artifactId}/{fileName}",
            ExpiresAt = DateTime.UtcNow.AddDays(3)
        };
    }

    [KernelFunction]
    [Description("查询已创建的 Artifact，返回访问 URL 列表。可用于获取之前保存的文件访问地址。")]
    public IEnumerable<CreateArtifactResponse> GetArtifact(
        [Description("要查询的 Artifact ID")] string artifactId,
        [Description("Artifact 内的文件名")] string artifactName,
        Kernel kernel
    )
    {
        var agentExecutionContext = kernel.Services.GetService<AgentExecutionContext>();
        var runId = agentExecutionContext.GetRunId();

        var artifactsPath = Path.Combine(_rootPath, runId, "artifacts", artifactId);
        if (!Directory.Exists(artifactsPath)) return Enumerable.Empty<CreateArtifactResponse>();

        var artifacts = new List<CreateArtifactResponse>();
        foreach (var file in Directory.EnumerateFiles(artifactsPath, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(fileName, artifactName, StringComparison.OrdinalIgnoreCase))
            {
                artifacts.Add(new CreateArtifactResponse
                {
                    ArtifactId = artifactId,
                    ArtifactName = fileName,
                    AccessURL = $"/statics/runs/{runId}/artifacts/{artifactId}/{fileName}",
                    ExpiresAt = DateTime.UtcNow.AddDays(3)
                });
            }
        }

        return artifacts;
    }
}

public class CreateArtifactResponse
{
    public string ArtifactId { get; set; }
    public string ArtifactName { get; set; }
    public string AccessURL { get; set; }
    public DateTime ExpiresAt { get; set; }
}
