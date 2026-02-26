using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Infrastructure.Sandbox;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "沙箱内文件操作插件。通过命令行提供安全的文件读写功能，支持读取文件头部、尾部或全部内容，以及创建和写入文件。", Version = "2.0")]
    public class FilePlugin : BasePlugin
    {
        private readonly ILogger<FilePlugin> _logger;
        private readonly SandboxService? _sandboxService;

        public FilePlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _logger = _serviceProvider.GetService<ILoggerFactory>().CreateLogger<FilePlugin>();
            _sandboxService = _serviceProvider.GetService<SandboxService>();
        }

        /// <summary>
        /// 获取沙箱内的文件路径
        /// </summary>
        private string GetSandboxPath(Kernel kernel, string filePath)
        {
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
            var resolvedPath = sandboxContext.ToLocalPath(filePath);
            var relativePath = Path.GetRelativePath(sandboxContext.RunDir, resolvedPath);
            return $"/sandbox/{relativePath.Replace('\\', '/')}";
        }

        /// <summary>
        /// 在沙箱中执行命令
        /// </summary>
        private async Task<CommandResult> ExecuteInSandboxAsync(Kernel kernel, string command)
        {
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();

            var sessionId = Path.GetFileName(sandboxContext.SessionDir);
            var volumeMappings = sandboxContext.GetVolumeMappings();

            var session = await _sandboxService!.GetOrCreateSessionAsync(sessionId, volumeMappings);
            return await _sandboxService.ExecuteAsync(sessionId, command);
        }

        /// <summary>
        /// 读取文件全部内容
        /// </summary>
        [KernelFunction]
        [Description("读取文本文件的全部内容。返回文件内容。")]
        public async Task<string> ReadFileAsync(
            [Description("文件路径（相对于沙箱目录）")] string filePath,
            Kernel kernel)
        {
            var sandboxPath = GetSandboxPath(kernel, filePath);
            var result = await ExecuteInSandboxAsync(kernel, $"cat \"{sandboxPath}\"");

            if (result.ExitCode != 0)
                throw new ArgumentException($"Failed to read file: {result.Stderr}");

            return result.Stdout;
        }

        /// <summary>
        /// 读取文件头部
        /// </summary>
        [KernelFunction]
        [Description("读取文件的前N行内容。适用于查看日志文件开头或大文件预览。")]
        public async Task<string> ReadFileHeadAsync(
            [Description("要读取的文件路径")] string filePath,
            [Description("要读取的行数，默认为100行")] int lines = 100,
            Kernel kernel = null)
        {
            var sandboxPath = GetSandboxPath(kernel, filePath);
            var result = await ExecuteInSandboxAsync(kernel, $"head -n {lines} \"{sandboxPath}\"");

            if (result.ExitCode != 0)
                throw new ArgumentException($"Failed to read file head: {result.Stderr}");

            return result.Stdout;
        }

        /// <summary>
        /// 读取文件尾部
        /// </summary>
        [KernelFunction]
        [Description("读取文件的末尾N行内容。适用于查看日志文件最新记录。")]
        public async Task<string> ReadFileTailAsync(
            [Description("要读取的文件路径")] string filePath,
            [Description("要读取的行数，默认为100行")] int lines = 100,
            Kernel kernel = null)
        {
            var sandboxPath = GetSandboxPath(kernel, filePath);
            var result = await ExecuteInSandboxAsync(kernel, $"tail -n {lines} \"{sandboxPath}\"");

            if (result.ExitCode != 0)
                throw new ArgumentException($"Failed to read file tail: {result.Stderr}");

            return result.Stdout;
        }

        /// <summary>
        /// 写入文件（覆盖）
        /// </summary>
        [KernelFunction]
        [Description("创建新文件或覆盖现有文件。如果文件已存在将被完全替换。")]
        public async Task<bool> WriteFileAsync(
            [Description("要创建/覆盖的文件路径（相对于沙箱目录）")] string filePath,
            [Description("要写入文件的内容")] string content,
            Kernel kernel)
        {
            var sandboxPath = GetSandboxPath(kernel, filePath);
            var escapedContent = content.Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");

            // 先创建目录
            var dirPath = Path.GetDirectoryName(sandboxPath)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(dirPath))
            {
                await ExecuteInSandboxAsync(kernel, $"mkdir -p \"{dirPath}\"");
            }

            // 写入文件
            var result = await ExecuteInSandboxAsync(kernel, $"echo \"{escapedContent}\" > \"{sandboxPath}\"");

            if (result.ExitCode != 0)
                throw new ArgumentException($"Failed to write file: {result.Stderr}");

            return true;
        }

        /// <summary>
        /// 追加写入文件
        /// </summary>
        [KernelFunction]
        [Description("向现有文件追加内容。如果文件不存在将创建新文件。")]
        public async Task<bool> AppendFileAsync(
            [Description("要追加内容的文件路径")] string filePath,
            [Description("要追加的内容")] string content,
            Kernel kernel)
        {
            var sandboxPath = GetSandboxPath(kernel, filePath);
            var escapedContent = content.Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");

            var result = await ExecuteInSandboxAsync(kernel, $"echo \"{escapedContent}\" >> \"{sandboxPath}\"");

            if (result.ExitCode != 0)
                throw new ArgumentException($"Failed to append file: {result.Stderr}");

            return true;
        }

        /// <summary>
        /// 检查文件/目录是否存在
        /// </summary>
        [KernelFunction]
        [Description("检查指定路径的文件或目录是否存在。")]
        public async Task<bool> ExistsAsync(
            [Description("要检查的路径")] string path,
            Kernel kernel = null)
        {
            var sandboxPath = GetSandboxPath(kernel, path);
            var result = await ExecuteInSandboxAsync(kernel, $"test -e \"{sandboxPath}\" && echo 'exists' || echo 'not exists'");

            return result.Stdout.Trim() == "exists";
        }

        /// <summary>
        /// 创建目录
        /// </summary>
        [KernelFunction]
        [Description("创建一个新目录（如果父目录不存在也会一并创建）。")]
        public async Task<bool> CreateDirectory(
            [Description("要创建的目录路径")] string directoryPath,
            Kernel kernel = null)
        {
            var sandboxPath = GetSandboxPath(kernel, directoryPath);
            var result = await ExecuteInSandboxAsync(kernel, $"mkdir -p \"{sandboxPath}\"");

            if (result.ExitCode != 0)
                throw new ArgumentException($"Failed to create directory: {result.Stderr}");

            return true;
        }

        /// <summary>
        /// 列出目录内容
        /// </summary>
        [KernelFunction]
        [Description("列出指定目录下的所有文件和子目录。")]
        public async Task<string> ListDirectoryAsync(
            [Description("要列出的目录路径")] string directoryPath,
            Kernel kernel = null)
        {
            var sandboxPath = GetSandboxPath(kernel, directoryPath);
            var result = await ExecuteInSandboxAsync(kernel, $"ls -la \"{sandboxPath}\"");

            if (result.ExitCode != 0)
                throw new ArgumentException($"Failed to list directory: {result.Stderr}");

            return result.Stdout;
        }

        /// <summary>
        /// 搜索文件内容（基于 grep）
        /// </summary>
        [KernelFunction]
        [Description("在文件中搜索指定的关键词或正则表达式（使用 grep 命令）。支持递归搜索、忽略大小写、正则表达式匹配等选项。")]
        public async Task<string> SearchAsync(
            [Description("要搜索的关键词或正则表达式")] string pattern,
            [Description("要搜索的文件或目录路径")] string filePath,
            [Description("是否递归搜索子目录，默认 true")] bool recursive = true,
            [Description("是否忽略大小写，默认 false")] bool ignoreCase = false,
            [Description("是否使用正则表达式，默认 true")] bool useRegex = true,
            [Description("显示匹配行的行号，默认 true")] bool showLineNumbers = true,
            [Description("只显示匹配的文件名，不显示具体内容，默认 false")] bool filesOnly = false,
            [Description("要排除的文件或目录模式，可多个用逗号分隔")] string exclude = "",
            Kernel kernel = null)
        {
            var sandboxPath = GetSandboxPath(kernel, filePath);

            var options = "";
            if (recursive) options += " -r";
            if (ignoreCase) options += " -i";
            if (useRegex) options += " -E";
            if (showLineNumbers) options += " -n";
            if (filesOnly) options += " -l";

            if (!string.IsNullOrEmpty(exclude))
            {
                var excludePatterns = exclude.Split(',').Select(e => $"--exclude='{e.Trim()}'");
                options += " " + string.Join(" ", excludePatterns);
            }

            var command = $"grep{options} \"{pattern}\" \"{sandboxPath}\"";
            var result = await ExecuteInSandboxAsync(kernel, command);

            // grep 返回 1 表示没有匹配，这是正常的
            if (result.ExitCode != 0 && result.ExitCode != 1)
                throw new ArgumentException($"Failed to search: {result.Stderr}");

            return string.IsNullOrEmpty(result.Stdout) ? "No matches found." : result.Stdout;
        }

        /// <summary>
        /// 编辑文件（基于 sed 的文本替换）
        /// </summary>
        [KernelFunction]
        [Description("对文件进行文本替换（使用 sed 命令）。支持单行替换、全局替换、精确匹配或正则表达式替换。")]
        public async Task<bool> EditFileAsync(
            [Description("要编辑的文件路径")] string filePath,
            [Description("要替换的原始文本")] string searchPattern,
            [Description("替换后的文本")] string replacement,
            [Description("是否全局替换（替换所有匹配项），默认 false（只替换第一个）")] bool global = false,
            [Description("是否使用正则表达式匹配，默认 false（精确匹配）")] bool useRegex = false,
            [Description("是否忽略大小写，默认 false")] bool ignoreCase = false,
            Kernel kernel = null)
        {
            var sandboxPath = GetSandboxPath(kernel, filePath);

            // 备份原文件
            var backupPath = $"{sandboxPath}.bak";
            await ExecuteInSandboxAsync(kernel, $"cp \"{sandboxPath}\" \"{backupPath}\"");

            string sedCommand;
            if (useRegex)
            {
                var flag = "g";
                if (ignoreCase) flag = "gi";
                sedCommand = $"sed -E 's/{searchPattern}/{replacement}/{flag}' \"{sandboxPath}\" > \"{sandboxPath}.tmp\" && mv \"{sandboxPath}.tmp\" \"{sandboxPath}\"";
            }
            else
            {
                if (ignoreCase)
                {
                    // 忽略大小写需要用 sed 的 I 标志
                    var flag = global ? "g" : "";
                    sedCommand = $"sed 's/{searchPattern}/{replacement}/{flag}I' \"{sandboxPath}\" > \"{sandboxPath}.tmp\" && mv \"{sandboxPath}.tmp\" \"{sandboxPath}\"";
                }
                else
                {
                    var flag = global ? "g" : "";
                    sedCommand = $"sed 's/{searchPattern}/{replacement}/{flag}' \"{sandboxPath}\" > \"{sandboxPath}.tmp\" && mv \"{sandboxPath}.tmp\" \"{sandboxPath}\"";
                }
            }

            var result = await ExecuteInSandboxAsync(kernel, sedCommand);

            if (result.ExitCode != 0)
            {
                // 恢复备份
                await ExecuteInSandboxAsync(kernel, $"mv \"{backupPath}\" \"{sandboxPath}\"");
                throw new ArgumentException($"Failed to edit file: {result.Stderr}");
            }

            // 删除备份文件
            await ExecuteInSandboxAsync(kernel, $"rm \"{backupPath}\"");

            return true;
        }
    }
}
