using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Vision;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;
using System.Net;

namespace InsightaAI.Agent.Tests.Vision;

public sealed class VisionServiceTests
{
    private static readonly byte[] TinyPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    #region Test doubles

    private sealed class FakeFileSystem : IFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public void AddFile(string path, byte[] bytes) => _files[path] = bytes;

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(_files.ContainsKey(path));

        public Task<byte[]> ReadFileBytesAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(_files[path]);

        public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FileContent> ReadFileLinesAsync(string path, int? offset = null, int? limit = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task WriteFileAsync(string path, string content, System.Text.Encoding encoding,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AppendFileAsync(string path, string content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<string[]> ListDirectoryAsync(string path, bool recursive = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string[]> GlobAsync(string pattern, string? basePath = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<GrepResult> GrepAsync(string pattern, string path, GrepOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public System.Text.Encoding DetectEncoding(string filePath) => throw new NotSupportedException();
    }

    private sealed class FakeVisionClient : ILlmClient
    {
        private readonly LlmResponse _response;

        public FakeVisionClient(LlmResponse response) => _response = response;

        public LlmRequest? LastRequest { get; private set; }

        public string AdapterName => "fake";

        public bool SupportsReasoning => false;

        public LlmStream Streaming(LlmRequest request) => throw new NotSupportedException();

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }

        public void Dispose() { }
    }

    private sealed class StaticBytesHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        private readonly string _contentType;
        private readonly HttpStatusCode _status;

        public StaticBytesHandler(byte[] bytes, string contentType, HttpStatusCode status = HttpStatusCode.OK)
        {
            _bytes = bytes;
            _contentType = contentType;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_status)
            {
                Content = new ByteArrayContent(_bytes)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_contentType);
            return Task.FromResult(response);
        }
    }

    #endregion

    private static VisionOptions CreateOptions(FakeVisionClient client, HttpClient? http = null) => new()
    {
        Model = "zhipu/glm-4.5v",
        ClientFactory = _ => client,
        HttpClientOverride = http
    };

    private static VisionService CreateService(
        FakeFileSystem fileSystem,
        FakeVisionClient client) =>
        new(CreateOptions(client), fileSystem);

    private static FakeVisionClient CreateClient() => new(new LlmResponse
    {
        Model = "glm-4.5v",
        Content = [new TextBlock { Text = "a red apple on a wooden table" }],
        FinishReason = DoneReason.Complete
    });

    // ---- 构造守卫 ----

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new VisionService(null!, new FakeFileSystem()));
    }

    [Fact]
    public void Constructor_NullFileSystem_Throws()
    {
        var options = new VisionOptions { Model = "m", ClientFactory = _ => CreateClient() };
        Assert.Throws<ArgumentNullException>(() => new VisionService(options, null!));
    }

    [Fact]
    public void Constructor_BlankModel_Throws()
    {
        var options = new VisionOptions { Model = " ", ClientFactory = _ => CreateClient() };
        Assert.Throws<ArgumentException>(() => new VisionService(options, new FakeFileSystem()));
    }

    [Fact]
    public void Constructor_NullClientFactory_Throws()
    {
        var options = new VisionOptions { Model = "m", ClientFactory = null! };
        Assert.Throws<ArgumentNullException>(() => new VisionService(options, new FakeFileSystem()));
    }

    // ---- 本地文件分支 ----

    [Fact]
    public async Task AnalyzeAsync_MissingLocalFile_ThrowsFileNotFound()
    {
        var service = CreateService(new FakeFileSystem(), CreateClient());
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.AnalyzeAsync("missing.png", "describe"));
    }

    [Fact]
    public async Task AnalyzeAsync_UnsupportedExtension_Throws()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("photo.xyz", TinyPng);
        var service = CreateService(fs, CreateClient());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AnalyzeAsync("photo.xyz", "describe"));
        Assert.Contains("Unsupported image extension", ex.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_OversizedFile_Throws()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("huge.png", new byte[20 * 1024 * 1024 + 1]); // 上限 20 MB，超 1 字节即拒
        var service = CreateService(fs, CreateClient());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AnalyzeAsync("huge.png", "describe"));
    }

    [Fact]
    public async Task AnalyzeAsync_BuildsMultimodalRequestAndReturnsDescription()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("photo.jpg", TinyPng);
        var client = CreateClient();
        var service = CreateService(fs, client);

        var description = await service.AnalyzeAsync("photo.jpg", "What is in this photo?");

        Assert.Equal("a red apple on a wooden table", description);

        var request = client.LastRequest!;
        Assert.Equal("glm-4.5v", request.Model); // ModelRef 解析：provider 前缀剥离
        var message = Assert.Single(request.Messages);
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Contains(message.Content, b => b is TextBlock t && t.Text == "What is in this photo?");

        var image = Assert.IsType<ImageBlock>(
            Assert.Single(message.Content, b => b is ImageBlock));
        Assert.Equal("image/jpeg", image.Source.MediaType);
        Assert.Equal(Convert.ToBase64String(TinyPng), image.Source.Data);
    }

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".webp", "image/webp")]
    [InlineData(".gif", "image/gif")]
    [InlineData(".bmp", "image/bmp")]
    public async Task AnalyzeAsync_ExtensionMapsToMediaType(string extension, string expectedMediaType)
    {
        var fs = new FakeFileSystem();
        fs.AddFile($"img{extension}", TinyPng);
        var client = CreateClient();
        var service = CreateService(fs, client);

        await service.AnalyzeAsync($"img{extension}", "describe");

        var image = Assert.IsType<ImageBlock>(
            Assert.Single(client.LastRequest!.Messages[0].Content, b => b is ImageBlock));
        Assert.Equal(expectedMediaType, image.Source.MediaType);
    }

    // ---- URL 分支 ----

    [Fact]
    public async Task AnalyzeAsync_UrlPrefersImageContentTypeOverExtension()
    {
        var fs = new FakeFileSystem();
        var client = CreateClient();
        using var http = new HttpClient(new StaticBytesHandler(TinyPng, "image/jpeg"));
        var service = new VisionService(CreateOptions(client, http), fs);

        await service.AnalyzeAsync("https://example.test/remote/pic.png", "describe");

        var image = Assert.IsType<ImageBlock>(
            Assert.Single(client.LastRequest!.Messages[0].Content, b => b is ImageBlock));
        Assert.Equal("image/jpeg", image.Source.MediaType);
    }

    [Fact]
    public async Task AnalyzeAsync_UrlFallsBackToExtensionWhenContentTypeNotImage()
    {
        var fs = new FakeFileSystem();
        var client = CreateClient();
        using var http = new HttpClient(new StaticBytesHandler(TinyPng, "text/html"));
        var service = new VisionService(CreateOptions(client, http), fs);

        await service.AnalyzeAsync("https://example.test/remote/pic.png", "describe");

        var image = Assert.IsType<ImageBlock>(
            Assert.Single(client.LastRequest!.Messages[0].Content, b => b is ImageBlock));
        Assert.Equal("image/png", image.Source.MediaType);
    }

    [Fact]
    public async Task AnalyzeAsync_UrlWithNonImageHeaderAndNoExtension_Throws()
    {
        var fs = new FakeFileSystem();
        using var http = new HttpClient(new StaticBytesHandler(TinyPng, "text/html"));
        var service = new VisionService(CreateOptions(CreateClient(), http), fs);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AnalyzeAsync("https://example.test/remote/pic", "describe"));
        Assert.Contains("does not point to a supported image", ex.Message);
    }

    [Fact]
    public async Task AnalyzeAsync_UrlHttpError_Throws()
    {
        var fs = new FakeFileSystem();
        using var http = new HttpClient(new StaticBytesHandler([], "image/png", HttpStatusCode.NotFound));
        var service = new VisionService(CreateOptions(CreateClient(), http), fs);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.AnalyzeAsync("https://example.test/missing.png", "describe"));
    }

    // ---- 客户端工厂 ----

    [Fact]
    public async Task AnalyzeAsync_ClientFactoryReceivesModelRef()
    {
        var fs = new FakeFileSystem();
        fs.AddFile("photo.png", TinyPng);
        string? receivedModelRef = null;
        var options = new VisionOptions
        {
            Model = "zhipu/glm-4.5v",
            ClientFactory = modelRef =>
            {
                receivedModelRef = modelRef;
                return CreateClient();
            }
        };
        var service = new VisionService(options, fs);

        await service.AnalyzeAsync("photo.png", "describe");

        Assert.Equal("zhipu/glm-4.5v", receivedModelRef);
    }
}
