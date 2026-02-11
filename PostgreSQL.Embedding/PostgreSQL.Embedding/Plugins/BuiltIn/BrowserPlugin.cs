using Microsoft.Playwright;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using PostgreSQL.Embedding.Utils;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn;

[KernelPlugin(Description = "浏览器插件。支持访问 URL、获取页面内容（执行 JavaScript）、验证页面、截取截图。所有操作在沙箱上下文中执行。")]
public class BrowserPlugin : BasePlugin, IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IPage? _page;
    private string? _pageError;

    public BrowserPlugin(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    private async Task EnsureBrowserAsync()
    {
        if (_playwright == null)
        {
            _playwright = await Playwright.CreateAsync();
        }

        if (_browser == null)
        {
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
        }

        if (_page == null)
        {
            _page = await _browser.NewPageAsync();
            _page.PageError += (_, error) => _pageError = error;
        }
    }

    [KernelFunction]
    [Description("导航到指定 URL，浏览器会打开并等待加载完成。返回页面标题和 URL。")]
    public async Task<NavigationResult> Navigate(string url)
    {
        await EnsureBrowserAsync();
        _pageError = null;
        await _page!.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 60000
        });

        return new NavigationResult
        {
            Url = _page.Url,
            Title = await _page.TitleAsync()
        };
    }

    [KernelFunction]
    [Description("无头模式获取当前页面内容。自动执行 JavaScript，返回标题、文本和元数据。适用于 SPA 和动态页面。")]
    public async Task<WebPageExtractionResult> GetPageContent()
    {
        await EnsureBrowserAsync();

        var title = await _page!.TitleAsync();
        var content = await _page.EvaluateAsync<string>("document.body.innerText");

        var metadata = new Dictionary<string, string>();
        var metaTags = await _page.QuerySelectorAllAsync("meta");

        foreach (var tag in metaTags)
        {
            var name = await tag.GetAttributeAsync("name") ?? await tag.GetAttributeAsync("property") ?? "";
            var contentAttr = await tag.GetAttributeAsync("content") ?? "";

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(contentAttr))
            {
                metadata[name] = contentAttr;
            }
        }

        return new WebPageExtractionResult
        {
            Url = _page.Url,
            Title = title,
            Content = content,
            Metadata = metadata
        };
    }

    [KernelFunction]
    [Description("验证当前页面是否能正常运行。检查 PageError 事件捕获的 JavaScript 错误。")]
    public async Task<PageValidationResult> ValidatePage()
    {
        await EnsureBrowserAsync();

        if (!string.IsNullOrEmpty(_pageError))
        {
            return new PageValidationResult
            {
                IsValid = false,
                Error = _pageError
            };
        }

        return new PageValidationResult { IsValid = true };
    }

    [KernelFunction]
    [Description("无头模式截取页面截图，保存到 artifacts 目录，返回文件路径。")]
    public async Task<string> Screenshot(Kernel kernel)
    {
        await EnsureBrowserAsync();

        var sandbox = kernel.GetAgentExecutionContext().GetSandboxContext();
        Directory.CreateDirectory(sandbox.ArtifactsDir);

        var fileName = $"screenshot_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
        var filePath = Path.Combine(sandbox.ArtifactsDir, fileName);

        await _page!.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = filePath,
            Type = ScreenshotType.Png
        });

        return fileName;
    }

    public async ValueTask DisposeAsync()
    {
        if (_page != null)
        {
            await _page.CloseAsync();
            _page = null;
        }

        if (_browser != null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }
}

public class PageValidationResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
}

public class NavigationResult
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}
