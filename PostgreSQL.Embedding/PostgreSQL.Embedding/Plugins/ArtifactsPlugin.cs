using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using SharpCompress.Common;
using System.ComponentModel;
using System.IO.Compression;
using System.Text;

namespace PostgreSQL.Embedding.Plugins;

[KernelPlugin(Description = "用于将智能体生成的内容发布为可访问 Artifact 的插件")]
public class ArtifactsPlugin : BasePlugin
{
    private readonly string _rootPath;
    public ArtifactsPlugin(IServiceProvider serviceProvider, IWebHostEnvironment env) : base(serviceProvider)
    {
        _rootPath = Path.Combine(env.ContentRootPath, "runs");
    }

    [KernelFunction]
    [Description("Create a new text-based Artifact with the specified type, content, file name, and content type. Returns a CreateArtifactResponse representing the created Artifact.")]
    public async Task<CreateArtifactResponse> CreateArtifactAsync(
        [Description("The type of the Artifact. Optional. Defaults to 'file'. For text-based Artifacts, keep as 'file' or specify 'text'.")] string type,
        [Description("The textual content of the Artifact. Must be a UTF-8 encoded string.")] string content,
        [Description("The file name of the Artifact, used to identify the file in storage.")] string fileName,
        [Description("The MIME type or content type of the Artifact, e.g., 'text/plain' for plain text content.")] string contentType,
        Kernel kernel
    )
    {
        var agentExecutionContext = kernel.Services.GetService<AgentExecutionContext>();
        var runId = agentExecutionContext.GetRunId();
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
    [Description("Create a new Artifact by compressing the contents of a folder into a zip file. Returns a CreateArtifactResponse representing the created Artifact.")]
    public CreateArtifactResponse CreateCompressedArtifactAsync(
        [Description("The absolute path to the folder whose contents will be compressed. Only the contents of the folder will be included, not the folder itself.")] string folderPath,
        [Description("The desired file name of the resulting zip Artifact, e.g., 'archive.zip'.")] string fileName,
        [Description("The type of the Artifact. Optional. Defaults to 'zip'.")] string type,
        [Description("The MIME type or content type of the Artifact. Optional. For zip files, use 'application/zip'.")] string contentType,
        Kernel kernel
    )
    {
        if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

        var agentExecutionContext = kernel.Services.GetService<AgentExecutionContext>();
        var runId = agentExecutionContext.GetRunId();
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
    [Description("读取一个 Artifact")]
    public IEnumerable<CreateArtifactResponse> GetArtifact([Description("ArtifactId")] string artifactId, [Description("artifactName")] string artifactName, Kernel kernel)
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
