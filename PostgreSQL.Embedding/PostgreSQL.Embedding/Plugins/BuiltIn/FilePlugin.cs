using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Plugins.Abstration;
using SharpCompress.Common;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    /// <summary>
    /// 文件操作结果
    /// </summary>
    public class FileInfoResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Content { get; set; }
        public long? FileSize { get; set; }
        public DateTime? LastModified { get; set; }
        public DateTime? CreationDate { get; set; }
    }

    /// <summary>
    /// 目录列表结果
    /// </summary>
    public class DirectoryListingResult
    {
        public string Path { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new();
        public List<string> Directories { get; set; } = new();
        public int TotalFiles { get; set; }
        public int TotalDirectories { get; set; }
        public string? Message { get; set; }
    }

    [KernelPlugin(Description = "沙箱内文件操作插件。提供安全的文件读写功能，支持读取文件头部、尾部或全部内容，以及创建和写入文件。", Version = "1.0")]
    public class FilePlugin : BasePlugin
    {
        private readonly ILogger<FilePlugin> _logger;

        public FilePlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _logger = _serviceProvider.GetService<ILoggerFactory>().CreateLogger<FilePlugin>();
        }

        /// <summary>
        /// 读取文件全部内容
        /// </summary>
        [KernelFunction]
        [Description("读取文本文件的全部内容。返回文件内容以及文件信息（大小、修改时间）。")]
        public async Task<string> ReadFileAsync(
            [Description("文件路径")] string filePath,
            Kernel kernel)
        {
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
            var targetPath = sandboxContext.ResolvePath(filePath);

            if (!File.Exists(targetPath))
                throw new ArgumentException($"The file does not exist: {filePath}");

            var fileInfo = new FileInfo(targetPath);
            var content = await File.ReadAllTextAsync(targetPath);

            return content;
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
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
            var targetPath = sandboxContext.ResolvePath(filePath);

            if (!File.Exists(targetPath))
                throw new ArgumentException($"The file does not exist: {filePath}");

            // 读取前N行
            var lineCount = 0;
            var content = new System.Text.StringBuilder();

            using var reader = new StreamReader(targetPath);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null && lineCount < lines)
            {
                content.AppendLine(line);
                lineCount++;
            }

            return content.ToString();
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
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
            var targetPath = sandboxContext.ResolvePath(filePath);

            if (!File.Exists(targetPath))
                throw new ArgumentException($"The file does not exist: {filePath}");

            // 读取后N行
            const int bufferSize = 8192;
            var queue = new Queue<string>(lines);

            using var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(0, SeekOrigin.End);

            var position = stream.Position;
            var lineBuffer = new System.Text.StringBuilder();
            var bytesRead = 0;

            // 从文件末尾向前读取
            while (position > 0 && queue.Count < lines)
            {
                position = Math.Max(0, position - bufferSize);
                stream.Seek(position, SeekOrigin.Begin);

                var buffer = new byte[bufferSize];
                bytesRead = stream.Read(buffer, 0, buffer.Length);

                for (var i = bytesRead - 1; i >= 0; i--)
                {
                    var ch = (char)buffer[i];
                    if (ch == '\n' || ch == '\r')
                    {
                        if (lineBuffer.Length > 0)
                        {
                            var line = lineBuffer.ToString();
                            lineBuffer.Clear();
                            if (queue.Count >= lines)
                            {
                                break;
                            }
                            queue.Enqueue(line);
                        }
                    }
                    else
                    {
                        lineBuffer.Insert(0, ch);
                    }
                }

                if (position == 0 && lineBuffer.Length > 0)
                {
                    queue.Enqueue(lineBuffer.ToString());
                    lineBuffer.Clear();
                }
            }

            var content = new System.Text.StringBuilder();
            foreach (var line in queue.Reverse())
            {
                content.AppendLine(line);
            }

            return content.ToString();
        }

        /// <summary>
        /// 写入文件（覆盖）
        /// </summary>
        [KernelFunction]
        [Description("创建新文件或覆盖现有文件。如果文件已存在将被完全替换。")]
        public async Task<bool> WriteFileAsync(
            [Description("要创建/覆盖的文件路径（相对于沙箱目录或绝对路径）")] string filePath,
            [Description("要写入文件的内容")] string content,
            Kernel kernel)
        {
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
            var targetPath = sandboxContext.ResolvePath(filePath);

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(targetPath, content);
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
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
            var targetPath = sandboxContext.ResolvePath(filePath);

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            await File.AppendAllTextAsync(targetPath, content);
            return true;
        }

        /// <summary>
        /// 检查文件/目录是否存在
        /// </summary>
        [KernelFunction]
        [Description("检查指定路径的文件或目录是否存在。")]
        public bool ExistsAsync(
            [Description("要检查的路径")] string path,
            Kernel kernel = null)
        {
            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
            var targetPath = sandboxContext?.ResolvePath(path);
            return File.Exists(targetPath) || Directory.Exists(targetPath);
        }



        /// <summary>
        /// 创建目录
        /// </summary>
        [KernelFunction]
        [Description("创建一个新目录（如果父目录不存在也会一并创建）。")]
        public bool CreateDirectory(
            [Description("要创建的目录路径")] string directoryPath,
            Kernel kernel = null)
        {
            var result = new FileInfoResult();

            var sandboxContext = kernel.GetAgentExecutionContext().GetSandboxContext();
            var targetPath = sandboxContext.ResolvePath(directoryPath);

            Directory.CreateDirectory(targetPath);
            return true;
        }
    }
}
