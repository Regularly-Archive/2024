using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Infrastructure.Sandbox;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using PostgreSQL.Embedding.Utils;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn;

[KernelPlugin(Description = "浏览器插件。支持访问 URL、获取页面内容、验证页面、截取截图。所有操作在沙箱上下文中执行。", Version = "1.1")]
public class BrowserPlugin : BasePlugin
{
    private readonly ILogger<BrowserPlugin> _logger;
    private readonly SandboxService? _sandboxService;
    private readonly bool _dockerAvailable;

    public BrowserPlugin(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _logger = _serviceProvider.GetService<ILoggerFactory>().CreateLogger<BrowserPlugin>();

        // 尝试获取 SandboxService
        _sandboxService = _serviceProvider.GetService<SandboxService>();
        _dockerAvailable = _sandboxService != null;
    }

    /// <summary>
    /// 在沙箱中执行 agent-browser 命令
    /// </summary>
    private async Task<CommandResult> ExecuteBrowserCommandAsync(string command, Kernel kernel)
    {
        var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();

        // 构建会话 ID: conversationId
        var sessionId = Path.GetFileName(sandboxContext.SessionDir);

        // 获取卷映射
        var volumeMappings = sandboxContext.GetVolumeMappings();

        // 获取或创建会话
        var session = await _sandboxService!.GetOrCreateSessionAsync(sessionId, volumeMappings);

        _logger.LogInformation("Executing browser command in session {SessionId}: {Command}", sessionId, command);

        // 执行 agent-browser 命令
        var result = await _sandboxService.ExecuteAsync(sessionId, command);

        return result;
    }

    /// <summary>
    /// 导航到指定 URL
    /// </summary>
    [KernelFunction]
    [Description("导航到指定 URL，浏览器会打开并等待加载完成。返回页面标题和 URL。")]
    public async Task<NavigationResult> Navigate(string url, Kernel kernel)
    {
        // 使用 agent-browser 打开 URL 并等待网络空闲
        var command = $"agent-browser open {url} --wait-load networkidle --json";
        var result = await ExecuteBrowserCommandAsync(command, kernel);

        if (result.ExitCode != 0)
            throw new Exception(result.Stderr);

        // 获取页面标题
        var titleCommand = "agent-browser get title";
        var titleResult = await ExecuteBrowserCommandAsync(titleCommand, kernel);
        var title = titleResult.Stdout.Trim();

        return new NavigationResult
        {
            Url = url,
            Title = title
        };
    }

    /// <summary>
    /// 获取当前页面内容
    /// </summary>
    [KernelFunction]
    [Description("获取当前页面内容。format 指定内容格式：text 为纯文本（默认），html 为 HTML 源码。")]
    public async Task<WebPageExtractionResult> GetPageContent(Kernel kernel, string format = "text")
    {
        // 获取页面标题
        var titleCommand = "agent-browser get title --json";
        var titleResult = await ExecuteBrowserCommandAsync(titleCommand, kernel);
        var title = titleResult.Stdout.Trim();

        // 获取页面 URL
        var urlCommand = "agent-browser get url --json";
        var urlResult = await ExecuteBrowserCommandAsync(urlCommand, kernel);
        var url = urlResult.Stdout.Trim();

        // 获取页面内容
        var contentCommand = format.ToLower() == "html"
            ? "agent-browser get html body"
            : "agent-browser snapshot --compact";
        var contentResult = await ExecuteBrowserCommandAsync(contentCommand, kernel);

        return new WebPageExtractionResult
        {
            Url = url,
            Title = title,
            Content = contentResult.Stdout,
            Metadata = new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// 验证当前页面是否能正常运行
    /// </summary>
    [KernelFunction]
    [Description("验证当前页面是否能正常运行，返回 true 表示无 JavaScript 错误。")]
    public async Task<bool> ValidatePage(Kernel kernel)
    {
        var command = "agent-browser errors";
        var result = await ExecuteBrowserCommandAsync(command, kernel);

        return result.ExitCode == 0 && string.IsNullOrEmpty(result.Stderr);
    }

    /// <summary>
    /// 截取页面截图
    /// </summary>
    [KernelFunction]
    [Description("截取页面截图，保存到 artifacts 目录，返回文件路径。")]
    public async Task<string> Screenshot(Kernel kernel)
    {
        var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();

        // 创建截图文件名
        var fileName = $"screenshot_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
        var sandboxPath = $"/sandbox/artifacts/{fileName}";

        // 执行截图命令
        var command = $"agent-browser screenshot {sandboxPath}";
        var result = await ExecuteBrowserCommandAsync(command, kernel);

        if (result.ExitCode != 0)
        {
            throw new Exception($"Screenshot failed: {result.Stderr}");
        }

        return sandboxPath;
    }

    /// <summary>
    /// 点击元素
    /// </summary>
    [KernelFunction]
    [Description("点击页面上的元素。使用 CSS 选择器或 snapshot 中获取的引用(如 @e1)。")]
    public async Task<string> Click(string selector, Kernel kernel)
    {
        var command = $"agent-browser click {selector}";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? "Click succeeded" : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 输入文本到元素
    /// </summary>
    [KernelFunction]
    [Description("输入文本到输入框。使用 CSS 选择器或引用(如 @e1)。")]
    public async Task<string> Fill(string selector, string text, Kernel kernel)
    {
        var command = $"agent-browser fill {selector} \"{text}\"";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? "Fill succeeded" : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 等待元素出现
    /// </summary>
    [KernelFunction]
    [Description("等待元素出现。使用 CSS 选择器。")]
    public async Task<string> WaitForSelector(string selector, Kernel kernel)
    {
        var command = $"agent-browser wait {selector}";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? "Element found" : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 执行 JavaScript
    /// </summary>
    [KernelFunction]
    [Description("在页面上下文中执行 JavaScript 代码。")]
    public async Task<string> Evaluate(string script, Kernel kernel)
    {
        var command = $"agent-browser eval \"{script}\"";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? result.Stdout : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 获取元素文本
    /// </summary>
    [KernelFunction]
    [Description("获取元素的文本内容。使用 CSS 选择器或引用。")]
    public async Task<string> GetText(string selector, Kernel kernel)
    {
        var command = $"agent-browser get text {selector} --json";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? result.Stdout.Trim() : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 获取元素属性
    /// </summary>
    [KernelFunction]
    [Description("获取元素的属性值。使用 CSS 选择器和属性名。")]
    public async Task<string> GetAttribute(string selector, string attributeName, Kernel kernel)
    {
        var command = $"agent-browser get attr {attributeName} {selector} --json";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? result.Stdout.Trim() : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 滚动页面
    /// </summary>
    [KernelFunction]
    [Description("滚动页面。方向: up, down, left, right。")]
    public async Task<string> Scroll(Kernel kernel, string direction, int? pixels = null)
    {
        var command = pixels.HasValue
            ? $"agent-browser scroll {direction} {pixels.Value}"
            : $"agent-browser scroll {direction}";

        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? "Scroll completed" : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 后退一页
    /// </summary>
    [KernelFunction]
    [Description("在浏览器历史中后退一页。")]
    public async Task<string> GoBack(Kernel kernel)
    {
        var command = "agent-browser back";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? "Back succeeded" : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 前进一页
    /// </summary>
    [KernelFunction]
    [Description("在浏览器历史中前进一页。")]
    public async Task<string> GoForward(Kernel kernel)
    {
        var command = "agent-browser forward";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? "Forward succeeded" : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 刷新页面
    /// </summary>
    [KernelFunction]
    [Description("刷新当前页面。")]
    public async Task<string> Reload(Kernel kernel)
    {
        var command = "agent-browser reload";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? "Reload succeeded" : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 获取页面快照（用于 AI 分析）
    /// </summary>
    [KernelFunction]
    [Description("获取页面的可访问性树快照，包含元素引用，方便 AI 分析和交互。")]
    public async Task<string> Snapshot(Kernel kernel, bool interactiveOnly = false)
    {
        var command = interactiveOnly
            ? "agent-browser snapshot --interactive"
            : "agent-browser snapshot";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? result.Stdout : $"Error: {result.Stderr}";
    }

    /// <summary>
    /// 获取元素数量
    /// </summary>
    [KernelFunction]
    [Description("获取匹配选择器的元素数量。")]
    public async Task<int> GetCount(string selector, Kernel kernel)
    {
        var command = $"agent-browser get count {selector} --json";
        var result = await ExecuteBrowserCommandAsync(command, kernel);

        if (result.ExitCode == 0 && int.TryParse(result.Stdout.Trim(), out var count))
        {
            return count;
        }

        return 0;
    }

    /// <summary>
    /// 检查元素是否可见
    /// </summary>
    [KernelFunction]
    [Description("检查元素是否可见。")]
    public async Task<bool> IsVisible(string selector, Kernel kernel)
    {
        var command = $"agent-browser is visible {selector} --json";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 && result.Stdout.Trim().ToLower() == "true";
    }

    /// <summary>
    /// 关闭浏览器
    /// </summary>
    [KernelFunction]
    [Description("关闭当前浏览器会话。")]
    public async Task<string> Close(Kernel kernel)
    {
        var command = "agent-browser close";
        var result = await ExecuteBrowserCommandAsync(command, kernel);
        return result.ExitCode == 0 ? "Browser closed" : $"Error: {result.Stderr}";
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
