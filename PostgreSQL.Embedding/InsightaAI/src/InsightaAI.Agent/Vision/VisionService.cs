using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Vision;

/// <summary>
/// 默认视觉分析服务实现：加载本地/远程图片，以一次性请求调用视觉模型。
/// 图像不进入主对话，主模型无需多模态能力。
/// </summary>
public sealed class VisionService : IVisionService
{
    /// <summary>图片大小上限：20 MB。</summary>
    internal const int MaxImageBytes = 20 * 1024 * 1024;

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(100) };

    private static readonly Dictionary<string, string> ExtensionMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
    };

    private readonly VisionOptions _options;
    private readonly IFileSystem _fileSystem;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 标准的构造注入形式；未提供 HttpClient 时使用服务内共享实例。
    /// </summary>
    public VisionService(VisionOptions options, IFileSystem fileSystem, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
        ArgumentNullException.ThrowIfNull(options.ClientFactory);

        _options = options;
        _fileSystem = fileSystem;
        _httpClient = httpClient ?? HttpClient;
    }

    /// <inheritdoc />
    public async Task<string> AnalyzeAsync(string source, string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var (bytes, mediaType) = await LoadImageAsync(source, cancellationToken);

        var modelId = ModelRef.TryParse(_options.Model, out var modelRef) ? modelRef.ModelId : _options.Model;
        var client = _options.ClientFactory(_options.Model);

        var message = new Message
        {
            Role = MessageRole.User,
            Content =
            [
                new TextBlock { Text = prompt },
                new ImageBlock { Source = new ImageSource { MediaType = mediaType, Data = Convert.ToBase64String(bytes) } }
            ]
        };

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model = modelId,
            Messages = [message],
            Tools = [],
            ToolChoice = ToolChoiceMode.None,
            MaxTokens = _options.MaxTokens,
            Temperature = _options.Temperature,
            ReasoningPreference = ReasoningPreference.Off,
            AllowReasoningFallbackToDefault = true
        }, cancellationToken);

        return response.GetTextContent();
    }

    /// <summary>
    /// 从本地路径或 URL 加载图片字节及媒体类型。
    /// </summary>
    private async Task<(byte[] Bytes, string MediaType)> LoadImageAsync(string source, CancellationToken ct)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            using var response = await _httpClient.GetAsync(uri, ct);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            EnsureSize(bytes.Length, source);

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var mediaType = contentType is not null && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? contentType
                : GuessMediaType(uri.AbsolutePath)
                    ?? throw new InvalidOperationException(
                        $"URL does not point to a supported image (content-type: '{contentType ?? "unknown"}').");

            return (bytes, mediaType);
        }

        if (!await _fileSystem.ExistsAsync(source, ct))
            throw new FileNotFoundException($"Image file not found: {source}");

        var mediaTypeFromExt = GuessMediaType(source)
            ?? throw new InvalidOperationException(
                $"Unsupported image extension. Supported: {string.Join(", ", ExtensionMediaTypes.Keys)}");

        var localBytes = await _fileSystem.ReadFileBytesAsync(source, ct);
        EnsureSize(localBytes.Length, source);
        return (localBytes, mediaTypeFromExt);
    }

    private static string? GuessMediaType(string path)
    {
        var ext = Path.GetExtension(path);
        return ExtensionMediaTypes.TryGetValue(ext, out var mediaType) ? mediaType : null;
    }

    private static void EnsureSize(int length, string source)
    {
        if (length > MaxImageBytes)
            throw new InvalidOperationException(
                $"Image exceeds the {MaxImageBytes / (1024 * 1024)} MB limit ({length / (1024 * 1024)} MB): {source}");
    }
}
