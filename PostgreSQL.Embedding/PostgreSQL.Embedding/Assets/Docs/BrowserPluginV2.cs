using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

namespace AgentBrowser
{
    /// <summary>
    /// AgentBrowser 插件 - 通过命令行执行浏览器自动化操作
    /// </summary>
    public class AgentBrowserPlugin
    {
        private readonly string _executablePath;
        private readonly Dictionary<string, string> _globalOptions;
        private string? _sessionId;

        /// <summary>
        /// 初始化 AgentBrowser 插件
        /// </summary>
        /// <param name="executablePath">agent-browser 可执行文件路径，默认为 "agent-browser"</param>
        public AgentBrowserPlugin(string executablePath = "agent-browser")
        {
            _executablePath = executablePath;
            _globalOptions = new Dictionary<string, string>();
        }

        /// <summary>
        /// 设置全局选项
        /// </summary>
        public void SetOption(string key, string value)
        {
            _globalOptions[key] = value;
        }

        /// <summary>
        /// 设置无头模式
        /// </summary>
        public void SetHeadless(bool headless = true)
        {
            _globalOptions["headed"] = headless ? "false" : "true";
        }

        /// <summary>
        /// 设置用户代理
        /// </summary>
        public void SetUserAgent(string userAgent)
        {
            _globalOptions["user-agent"] = userAgent;
        }

        /// <summary>
        /// 设置代理服务器
        /// </summary>
        public void SetProxy(string proxy)
        {
            _globalOptions["proxy"] = proxy;
        }

        /// <summary>
        /// 设置下载路径
        /// </summary>
        public void SetDownloadPath(string path)
        {
            _globalOptions["download-path"] = path;
        }

        /// <summary>
        /// 设置自定义 HTTP 头
        /// </summary>
        public void SetHeaders(Dictionary<string, string> headers)
        {
            _globalOptions["headers"] = JsonSerializer.Serialize(headers);
        }

        #region 私有方法

        private async Task<CommandResult> ExecuteCommandAsync(string subCommand, Dictionary<string, string>? options = null)
        {
            var args = new List<string>();

            // 添加全局选项
            foreach (var opt in _globalOptions)
            {
                args.Add($"--{opt.Key}");
                if (!string.IsNullOrEmpty(opt.Value))
                    args.Add(opt.Value);
            }

            // 添加会话ID
            if (!string.IsNullOrEmpty(_sessionId))
            {
                args.Add("--session");
                args.Add(_sessionId);
            }

            // 添加子命令
            args.Add(subCommand);

            // 添加子命令选项
            if (options != null)
            {
                foreach (var opt in options)
                {
                    if (!string.IsNullOrEmpty(opt.Value))
                    {
                        args.Add($"--{opt.Key}");
                        args.Add(opt.Value);
                    }
                }
            }

            return await RunProcessAsync(args);
        }

        private Task<CommandResult> RunProcessAsync(List<string> arguments)
        {
            return Task.Run(() =>
            {
                var result = new CommandResult();
                var startInfo = new ProcessStartInfo
                {
                    FileName = _executablePath,
                    Arguments = string.Join(" ", arguments),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = startInfo };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) outputBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                result.ExitCode = process.ExitCode;
                result.Output = outputBuilder.ToString();
                result.Error = errorBuilder.ToString();
                result.Success = process.ExitCode == 0;

                return result;
            });
        }

        #endregion

        #region 会话管理

        /// <summary>
        /// 连接到现有会话或创建新会话
        /// </summary>
        public async Task<CommandResult> ConnectAsync(string? sessionId = null)
        {
            var options = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(sessionId))
                options["session"] = sessionId;

            var result = await ExecuteCommandAsync("connect", options);
            if (result.Success)
            {
                // 尝试从输出中提取会话ID
                _sessionId = sessionId;
            }
            return result;
        }

        /// <summary>
        /// 关闭当前会话
        /// </summary>
        public async Task<CommandResult> CloseAsync()
        {
            return await ExecuteCommandAsync("close");
        }

        #endregion

        #region 导航操作

        /// <summary>
        /// 打开指定 URL
        /// </summary>
        /// <param name="url">要打开的网址</param>
        /// <param name="waitUntil">等待加载的事件: networkidle, load, domcontentloaded, commit</param>
        public async Task<CommandResult> OpenAsync(string url, string? waitUntil = null)
        {
            var options = new Dictionary<string, string>
            {
                { "url", url }
            };
            if (!string.IsNullOrEmpty(waitUntil))
                options["wait-until"] = waitUntil;

            return await ExecuteCommandAsync("open", options);
        }

        /// <summary>
        /// 后退一页
        /// </summary>
        public async Task<CommandResult> BackAsync()
        {
            return await ExecuteCommandAsync("back");
        }

        /// <summary>
        /// 前进一页
        /// </summary>
        public async Task<CommandResult> ForwardAsync()
        {
            return await ExecuteCommandAsync("forward");
        }

        /// <summary>
        /// 重新加载当前页
        /// </summary>
        /// <param name="ignoreCache">是否忽略缓存</param>
        public async Task<CommandResult> ReloadAsync(bool ignoreCache = false)
        {
            var options = new Dictionary<string, string>();
            if (ignoreCache)
                options["ignore-cache"] = "true";

            return await ExecuteCommandAsync("reload", options);
        }

        #endregion

        #region 元素查找

        /// <summary>
        /// 查找元素定位器
        /// </summary>
        /// <param name="locatorType">定位器类型: role, text, label, placeholder, alt, title, testid, id, css, xpath</param>
        /// <param name="value">定位器值</param>
        public async Task<CommandResult> FindAsync(string locatorType, string value)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, value }
            };
            return await ExecuteCommandAsync("find", options);
        }

        /// <summary>
        /// 按 Role 查找元素
        /// </summary>
        public async Task<CommandResult> FindByRoleAsync(string role, string? name = null)
        {
            var options = new Dictionary<string, string>
            {
                { "role", role }
            };
            if (!string.IsNullOrEmpty(name))
                options["name"] = name;

            return await ExecuteCommandAsync("find", options);
        }

        /// <summary>
        /// 按文本内容查找元素
        /// </summary>
        public async Task<CommandResult> FindByTextAsync(string text, bool exact = false)
        {
            var options = new Dictionary<string, string>
            {
                { "text", text }
            };
            if (exact)
                options["exact"] = "true";

            return await ExecuteCommandAsync("find", options);
        }

        /// <summary>
        /// 按标签查找元素
        /// </summary>
        public async Task<CommandResult> FindByLabelAsync(string label)
        {
            var options = new Dictionary<string, string>
            {
                { "label", label }
            };
            return await ExecuteCommandAsync("find", options);
        }

        /// <summary>
        /// 按占位符查找元素
        /// </summary>
        public async Task<CommandResult> FindByPlaceholderAsync(string placeholder)
        {
            var options = new Dictionary<string, string>
            {
                { "placeholder", placeholder }
            };
            return await ExecuteCommandAsync("find", options);
        }

        /// <summary>
        /// 按 ID 查找元素
        /// </summary>
        public async Task<CommandResult> FindByIdAsync(string id)
        {
            return await FindAsync("id", id);
        }

        /// <summary>
        /// 按 CSS 选择器查找元素
        /// </summary>
        public async Task<CommandResult> FindByCssAsync(string css)
        {
            return await FindAsync("css", css);
        }

        /// <summary>
        /// 按 XPath 查找元素
        /// </summary>
        public async Task<CommandResult> FindByXPathAsync(string xpath)
        {
            return await FindAsync("xpath", xpath);
        }

        #endregion

        #region 元素交互

        /// <summary>
        /// 点击元素
        /// </summary>
        /// <param name="locatorType">定位器类型</param>
        /// <param name="locatorValue">定位器值</param>
        /// <param name="button">鼠标按钮: left, right, middle</param>
        /// <param name="clickCount">点击次数</param>
        public async Task<CommandResult> ClickAsync(string locatorType, string locatorValue, 
            string button = "left", int clickCount = 1)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue },
                { "button", button },
                { "clicks", clickCount.ToString() }
            };
            return await ExecuteCommandAsync("click", options);
        }

        /// <summary>
        /// 双击元素
        /// </summary>
        public async Task<CommandResult> DoubleClickAsync(string locatorType, string locatorValue)
        {
            return await ClickAsync(locatorType, locatorValue, "left", 2);
        }

        /// <summary>
        /// 右键点击元素
        /// </summary>
        public async Task<CommandResult> RightClickAsync(string locatorType, string locatorValue)
        {
            return await ClickAsync(locatorType, locatorValue, "right", 1);
        }

        /// <summary>
        /// 输入文本到元素
        /// </summary>
        /// <param name="locatorType">定位器类型</param>
        /// <param name="locatorValue">定位器值</param>
        /// <param name="text">要输入的文本</param>
        /// <param name="delay">每个字符输入的延迟(毫秒)</param>
        public async Task<CommandResult> TypeAsync(string locatorType, string locatorValue, 
            string text, int delay = 0)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue },
                { "text", text }
            };
            if (delay > 0)
                options["delay"] = delay.ToString();

            return await ExecuteCommandAsync("type", options);
        }

        /// <summary>
        /// 填充表单（清空后输入）
        /// </summary>
        public async Task<CommandResult> FillAsync(string locatorType, string locatorValue, string value)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue },
                { "value", value }
            };
            return await ExecuteCommandAsync("fill", options);
        }

        /// <summary>
        /// 模拟按键
        /// </summary>
        /// <param name="key">按键: Enter, Escape, Backspace, Delete, Tab, ArrowUp, ArrowDown 等</param>
        public async Task<CommandResult> PressAsync(string key)
        {
            var options = new Dictionary<string, string>
            {
                { "key", key }
            };
            return await ExecuteCommandAsync("press", options);
        }

        /// <summary>
        /// 模拟键盘快捷键
        /// </summary>
        /// <param name="keys">组合键，如 "Control+c", "Control+Shift+a"</param>
        public async Task<CommandResult> KeyboardAsync(string keys)
        {
            var options = new Dictionary<string, string>
            {
                { "keys", keys }
            };
            return await ExecuteCommandAsync("keyboard", options);
        }

        /// <summary>
        /// 悬停在元素上
        /// </summary>
        public async Task<CommandResult> HoverAsync(string locatorType, string locatorValue)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue }
            };
            return await ExecuteCommandAsync("hover", options);
        }

        /// <summary>
        /// 聚焦元素
        /// </summary>
        public async Task<CommandResult> FocusAsync(string locatorType, string locatorValue)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue }
            };
            return await ExecuteCommandAsync("focus", options);
        }

        /// <summary>
        /// 勾选复选框
        /// </summary>
        public async Task<CommandResult> CheckAsync(string locatorType, string locatorValue)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue }
            };
            return await ExecuteCommandAsync("check", options);
        }

        /// <summary>
        /// 取消勾选复选框
        /// </summary>
        public async Task<CommandResult> UncheckAsync(string locatorType, string locatorValue)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue }
            };
            return await ExecuteCommandAsync("uncheck", options);
        }

        /// <summary>
        /// 选择选项（适用于 select 元素）
        /// </summary>
        /// <param name="locatorType">定位器类型</param>
        /// <param name="locatorValue">定位器值</param>
        /// <param name="option">选项值或选项文本</param>
        /// <param name="byText">是否按文本选择</param>
        public async Task<CommandResult> SelectAsync(string locatorType, string locatorValue, 
            string option, bool byText = false)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue },
                { byText ? "by-text" : "by-value", option }
            };
            return await ExecuteCommandAsync("select", options);
        }

        /// <summary>
        /// 拖拽元素
        /// </summary>
        /// <param name="sourceLocatorType">源元素定位器类型</param>
        /// <param name="sourceLocatorValue">源元素定位器值</param>
        /// <param name="targetLocatorType">目标元素定位器类型</param>
        /// <param name="targetLocatorValue">目标元素定位器值</param>
        public async Task<CommandResult> DragAsync(string sourceLocatorType, string sourceLocatorValue,
            string targetLocatorType, string targetLocatorValue)
        {
            var options = new Dictionary<string, string>
            {
                { sourceLocatorType, sourceLocatorValue },
                { $"to-{targetLocatorType}", targetLocatorValue }
            };
            return await ExecuteCommandAsync("drag", options);
        }

        /// <summary>
        /// 上传文件
        /// </summary>
        /// <param name="locatorType">文件输入框定位器类型</param>
        /// <param name="locatorValue">文件输入框定位器值</param>
        /// <param name="filePath">要上传的文件路径</param>
        public async Task<CommandResult> UploadAsync(string locatorType, string locatorValue, string filePath)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue },
                { "file", filePath }
            };
            return await ExecuteCommandAsync("upload", options);
        }

        /// <summary>
        /// 滚动页面或元素
        /// </summary>
        /// <param name="scrollType">滚动类型: top, bottom, into-view, by</param>
        /// <param name="locatorType">元素定位器类型（可选）</param>
        /// <param name="locatorValue">元素定位器值（可选）</param>
        /// <param name="x">水平滚动像素</param>
        /// <param name="y">垂直滚动像素</param>
        public async Task<CommandResult> ScrollAsync(string scrollType, 
            string? locatorType = null, string? locatorValue = null, int x = 0, int y = 0)
        {
            var options = new Dictionary<string, string>
            {
                { "scroll-type", scrollType }
            };
            if (!string.IsNullOrEmpty(locatorType))
                options[locatorType] = locatorValue ?? "";
            if (x != 0)
                options["x"] = x.ToString();
            if (y != 0)
                options["y"] = y.ToString();

            return await ExecuteCommandAsync("scroll", options);
        }

        /// <summary>
        /// 滚动到页面顶部
        /// </summary>
        public async Task<CommandResult> ScrollToTopAsync()
        {
            return await ScrollAsync("top");
        }

        /// <summary>
        /// 滚动到页面底部
        /// </summary>
        public async Task<CommandResult> ScrollToBottomAsync()
        {
            return await ScrollAsync("bottom");
        }

        /// <summary>
        /// 滚动到元素可见
        /// </summary>
        public async Task<CommandResult> ScrollIntoViewAsync(string locatorType, string locatorValue)
        {
            return await ScrollAsync("into-view", locatorType, locatorValue);
        }

        #endregion

        #region 等待操作

        /// <summary>
        /// 等待指定时间（毫秒）
        /// </summary>
        public async Task<CommandResult> WaitAsync(int milliseconds)
        {
            var options = new Dictionary<string, string>
            {
                { "timeout", milliseconds.ToString() }
            };
            return await ExecuteCommandAsync("wait", options);
        }

        /// <summary>
        /// 等待元素可见
        /// </summary>
        public async Task<CommandResult> WaitForVisibleAsync(string locatorType, string locatorValue, int timeout = 30000)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue },
                { "timeout", timeout.ToString() }
            };
            return await ExecuteCommandAsync("wait", options);
        }

        /// <summary>
        /// 等待元素可点击
        /// </summary>
        public async Task<CommandResult> WaitForClickableAsync(string locatorType, string locatorValue, int timeout = 30000)
        {
            var options = new Dictionary<string, string>
            {
                { locatorType, locatorValue },
                { "state", "visible" },
                { "timeout", timeout.ToString() }
            };
            return await ExecuteCommandAsync("wait", options);
        }

        /// <summary>
        /// 等待网络空闲
        /// </summary>
        public async Task<CommandResult> WaitForNetworkIdleAsync(int timeout = 30000)
        {
            var options = new Dictionary<string, string>
            {
                { "until", "networkidle" },
                { "timeout", timeout.ToString() }
            };
            return await ExecuteCommandAsync("wait", options);
        }

        #endregion

        #region 信息获取

        /// <summary>
        /// 获取元素或页面信息
        /// </summary>
        /// <param name="getType">获取类型: text, html, value, attr, title, url, count, box, styles</param>
        /// <param name="locatorType">定位器类型（可选）</param>
        /// <param name="locatorValue">定位器值（可选）</param>
        /// <param name="attrName">属性名（当 getType 为 attr 时需要）</param>
        public async Task<CommandResult> GetAsync(string getType, 
            string? locatorType = null, string? locatorValue = null, string? attrName = null)
        {
            var options = new Dictionary<string, string>
            {
                { getType, "true" }
            };
            if (!string.IsNullOrEmpty(locatorType))
                options[locatorType] = locatorValue ?? "";
            if (!string.IsNullOrEmpty(attrName))
                options["name"] = attrName;

            return await ExecuteCommandAsync("get", options);
        }

        /// <summary>
        /// 获取元素文本内容
        /// </summary>
        public async Task<CommandResult> GetTextAsync(string locatorType, string locatorValue)
        {
            return await GetAsync("text", locatorType, locatorValue);
        }

        /// <summary>
        /// 获取元素 HTML
        /// </summary>
        public async Task<CommandResult> GetHtmlAsync(string locatorType, string locatorValue)
        {
            return await GetAsync("html", locatorType, locatorValue);
        }

        /// <summary>
        /// 获取元素值（适用于输入框）
        /// </summary>
        public async Task<CommandResult> GetValueAsync(string locatorType, string locatorValue)
        {
            return await GetAsync("value", locatorType, locatorValue);
        }

        /// <summary>
        /// 获取元素属性
        /// </summary>
        public async Task<CommandResult> GetAttributeAsync(string locatorType, string locatorValue, string attrName)
        {
            return await GetAsync("attr", locatorType, locatorValue, attrName);
        }

        /// <summary>
        /// 获取页面标题
        /// </summary>
        public async Task<CommandResult> GetTitleAsync()
        {
            return await GetAsync("title");
        }

        /// <summary>
        /// 获取页面 URL
        /// </summary>
        public async Task<CommandResult> GetUrlAsync()
        {
            return await GetAsync("url");
        }

        /// <summary>
        /// 获取元素数量
        /// </summary>
        public async Task<CommandResult> GetCountAsync(string locatorType, string locatorValue)
        {
            return await GetAsync("count", locatorType, locatorValue);
        }

        /// <summary>
        /// 获取元素边界框（位置和尺寸）
        /// </summary>
        public async Task<CommandResult> GetBoxAsync(string locatorType, string locatorValue)
        {
            return await GetAsync("box", locatorType, locatorValue);
        }

        /// <summary>
        /// 获取元素计算样式
        /// </summary>
        public async Task<CommandResult> GetStylesAsync(string locatorType, string locatorValue, string? propertyName = null)
        {
            var options = new Dictionary<string, string>
            {
                { "styles", "true" },
                { locatorType, locatorValue }
            };
            if (!string.IsNullOrEmpty(propertyName))
                options["name"] = propertyName;

            return await ExecuteCommandAsync("get", options);
        }

        #endregion

        #region 状态检查

        /// <summary>
        /// 检查元素状态
        /// </summary>
        /// <param name="checkType">检查类型: visible, enabled, checked</param>
        /// <param name="locatorType">定位器类型</param>
        /// <param name="locatorValue">定位器值</param>
        public async Task<CommandResult> IsAsync(string checkType, string locatorType, string locatorValue)
        {
            var options = new Dictionary<string, string>
            {
                { checkType, "true" },
                { locatorType, locatorValue }
            };
            return await ExecuteCommandAsync("is", options);
        }

        /// <summary>
        /// 检查元素是否可见
        /// </summary>
        public async Task<CommandResult> IsVisibleAsync(string locatorType, string locatorValue)
        {
            return await IsAsync("visible", locatorType, locatorValue);
        }

        /// <summary>
        /// 检查元素是否可用（可交互）
        /// </summary>
        public async Task<CommandResult> IsEnabledAsync(string locatorType, string locatorValue)
        {
            return await IsAsync("enabled", locatorType, locatorValue);
        }

        /// <summary>
        /// 检查复选框/单选框是否被选中
        /// </summary>
        public async Task<CommandResult> IsCheckedAsync(string locatorType, string locatorValue)
        {
            return await IsAsync("checked", locatorType, locatorValue);
        }

        #endregion

        #region 截图与输出

        /// <summary>
        /// 截图
        /// </summary>
        /// <param name="filePath">保存路径（可选，默认 temp 目录）</param>
        /// <param name="fullPage">是否截取整个页面</param>
        /// <param name="locatorType">元素定位器（可选，只截取元素）</param>
        /// <param name="locatorValue">元素定位器值</param>
        public async Task<CommandResult> ScreenshotAsync(string? filePath = null, bool fullPage = false,
            string? locatorType = null, string? locatorValue = null)
        {
            var options = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(filePath))
                options["path"] = filePath;
            if (fullPage)
                options["full-page"] = "true";
            if (!string.IsNullOrEmpty(locatorType))
                options[locatorType] = locatorValue ?? "";

            return await ExecuteCommandAsync("screenshot", options);
        }

        /// <summary>
        /// 生成 PDF
        /// </summary>
        /// <param name="filePath">保存路径</param>
        /// <param name="pageSize">页面大小: Letter, A4, A3</param>
        /// <param name="landscape">是否横向</param>
        public async Task<CommandResult> PdfAsync(string filePath, string pageSize = "A4", bool landscape = false)
        {
            var options = new Dictionary<string, string>
            {
                { "path", filePath },
                { "page-size", pageSize }
            };
            if (landscape)
                options["landscape"] = "true";

            return await ExecuteCommandAsync("pdf", options);
        }

        /// <summary>
        /// 获取页面快照（HTML）
        /// </summary>
        public async Task<CommandResult> SnapshotAsync()
        {
            return await ExecuteCommandAsync("snapshot");
        }

        #endregion

        #region JavaScript 执行

        /// <summary>
        /// 执行 JavaScript 代码
        /// </summary>
        /// <param name="script">JavaScript 代码</param>
        /// <param name="args">传递给脚本的参数</param>
        public async Task<CommandResult> EvalAsync(string script, Dictionary<string, object>? args = null)
        {
            var options = new Dictionary<string, string>
            {
                { "script", script }
            };
            if (args != null)
                options["args"] = JsonSerializer.Serialize(args);

            return await ExecuteCommandAsync("eval", options);
        }

        #endregion

        #region 下载管理

        /// <summary>
        /// 设置下载路径并启用下载
        /// </summary>
        public void EnableDownload(string downloadPath)
        {
            SetOption("download-path", downloadPath);
        }

        /// <summary>
        /// 等待下载完成
        /// </summary>
        /// <param name="pattern">文件名匹配模式（可选）</param>
        /// <param name="timeout">超时时间（毫秒）</param>
        public async Task<CommandResult> WaitForDownloadAsync(string? pattern = null, int timeout = 60000)
        {
            var options = new Dictionary<string, string>
            {
                { "timeout", timeout.ToString() }
            };
            if (!string.IsNullOrEmpty(pattern))
                options["pattern"] = pattern;

            return await ExecuteCommandAsync("download", options);
        }

        #endregion
    }

    /// <summary>
    /// 命令执行结果
    /// </summary>
    public class CommandResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// 尝试将输出解析为指定类型
        /// </summary>
        public T? GetParsedOutput<T>() where T : class
        {
            try
            {
                return JsonSerializer.Deserialize<T>(Output);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取输出文本（去除首尾空白）
        /// </summary>
        public string GetText()
        {
            return Output.Trim();
        }

        /// <summary>
        /// 检查输出是否包含指定文本
        /// </summary>
        public bool Contains(string text)
        {
            return Output.Contains(text);
        }

        public override string ToString()
        {
            return $"CommandResult: Success={Success}, ExitCode={ExitCode}, Output={Output.Substring(0, Math.Min(100, Output.Length))}...";
        }
    }

    /// <summary>
    /// 元素定位器类型枚举
    /// </summary>
    public static class LocatorTypes
    {
        public const string Role = "role";
        public const string Text = "text";
        public const string Label = "label";
        public const string Placeholder = "placeholder";
        public const string Alt = "alt";
        public const string Title = "title";
        public const string TestId = "testid";
        public const string Id = "id";
        public const string Css = "css";
        public const string XPath = "xpath";
    }

    /// <summary>
    /// 常用按键常量
    /// </summary>
    public static class Keys
    {
        public const string Enter = "Enter";
        public const string Escape = "Escape";
        public const string Backspace = "Backspace";
        public const string Delete = "Delete";
        public const string Tab = "Tab";
        public const string ArrowUp = "ArrowUp";
        public const string ArrowDown = "ArrowDown";
        public const string ArrowLeft = "ArrowLeft";
        public const string ArrowRight = "ArrowRight";
        public const string Home = "Home";
        public const string End = "End";
        public const string PageUp = "PageUp";
        public const string PageDown = "PageDown";
    }

    /// <summary>
    /// 常用角色常量
    /// </summary>
    public static class Roles
    {
        public const string Button = "button";
        public const string Link = "link";
        public const string Textbox = "textbox";
        public const string Checkbox = "checkbox";
        public const string Radio = "radio";
        public const string Combobox = "combobox";
        public const string MenuItem = "menuitem";
        public const string Menu = "menu";
        public const string Tab = "tab";
        public const string Image = "img";
        public const string Heading = "heading";
        public const string Paragraph = "paragraph";
    }
}

// ==================== 使用示例 ====================
/*
using AgentBrowser;

class Program
{
    static async Task Main(string[] args)
    {
        // 创建插件实例
        var browser = new AgentBrowserPlugin("agent-browser");
        
        // 设置无头模式
        browser.SetHeadless(true);
        
        // 打开网页
        var result = await browser.OpenAsync("https://example.com", "networkidle");
        Console.WriteLine($"Open result: {result.Success}");
        
        // 获取页面标题
        var title = await browser.GetTitleAsync();
        Console.WriteLine($"Page title: {title.GetText()}");
        
        // 查找并点击元素
        await browser.ClickAsync("text", "Learn More");
        
        // 输入文本
        await browser.FillAsync("placeholder", "Search", "test query");
        await browser.PressAsync(Keys.Enter);
        
        // 等待加载
        await browser.WaitForNetworkIdleAsync();
        
        // 获取元素文本
        var text = await browser.GetTextAsync("css", ".content");
        Console.WriteLine($"Content: {text.GetText()}");
        
        // 截图
        await browser.ScreenshotAsync("screenshot.png", fullPage: true);
        
        // 关闭浏览器
        await browser.CloseAsync();
    }
}
*/