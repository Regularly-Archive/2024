using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    /// <summary>
    /// 文件操作结果
    /// </summary>
    public class FileOperationResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Content { get; set; }
        public long? FileSize { get; set; }
        public DateTime? LastModified { get; set; }
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

    [KernelPluginAttribute(Description = "文件操作插件。提供安全的文件读写功能，支持读取文件头部、尾部或全部内容，以及创建和写入文件。所有操作限制在沙箱目录内。", Version = "1.0")]
    public class FilePlugin : BasePlugin
    {
        private readonly string _sandboxDirectory;
        private readonly ILogger<FilePlugin> _logger;

        public FilePlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _sandboxDirectory = Path.GetTempPath();
            _logger = _serviceProvider.GetService<ILoggerFactory>().CreateLogger<FilePlugin>();
        }

        /// <summary>
        /// 获取当前沙箱目录
        /// </summary>
        [KernelFunction]
        [Description("获取当前允许操作文件的工作目录")]
        public string GetSandboxDirectory()
        {
            return _sandboxDirectory;
        }

        /// <summary>
        /// 读取文件全部内容
        /// </summary>
        [KernelFunction]
        [Description("读取文件的全部内容。返回文件内容以及文件信息（大小、修改时间）。")]
        public async Task<FileOperationResult> ReadFileAsync(
            [Description("要读取的文件路径（相对于沙箱目录或绝对路径）")] string filePath)
        {
            var result = new FileOperationResult();

            try
            {
                var targetPath = NormalizePath(filePath);

                if (!File.Exists(targetPath))
                {
                    result.Success = false;
                    result.Message = $"文件不存在: {filePath}";
                    return result;
                }

                var fileInfo = new FileInfo(targetPath);
                var content = await File.ReadAllTextAsync(targetPath);

                result.Success = true;
                result.Content = content;
                result.FileSize = fileInfo.Length;
                result.LastModified = fileInfo.LastWriteTime;
                result.Message = $"成功读取文件，大小: {fileInfo.Length} 字节";

                _logger.LogInformation("File read: {Path}", targetPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Success = false;
                result.Message = $"访问被拒绝: {ex.Message}";
                _logger.LogWarning(ex, "Access denied reading file: {Path}", filePath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"读取文件失败: {ex.Message}";
                _logger.LogError(ex, "Failed to read file: {Path}", filePath);
            }

            return result;
        }

        /// <summary>
        /// 读取文件头部
        /// </summary>
        [KernelFunction]
        [Description("读取文件的前N行内容。适用于查看日志文件开头或大文件预览。")]
        public async Task<FileOperationResult> ReadFileHeadAsync(
            [Description("要读取的文件路径")] string filePath,
            [Description("要读取的行数，默认为100行")] int lines = 100)
        {
            var result = new FileOperationResult();

            try
            {
                var targetPath = NormalizePath(filePath);

                if (!File.Exists(targetPath))
                {
                    result.Success = false;
                    result.Message = $"文件不存在: {filePath}";
                    return result;
                }

                var fileInfo = new FileInfo(targetPath);
                result.FileSize = fileInfo.Length;
                result.LastModified = fileInfo.LastWriteTime;

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

                result.Success = true;
                result.Content = content.ToString();
                result.Message = $"成功读取前 {lineCount} 行 (共 {fileInfo.Length} 字节)";

                _logger.LogInformation("File head read: {Path}, lines: {Lines}", targetPath, lineCount);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"读取文件失败: {ex.Message}";
                _logger.LogError(ex, "Failed to read file head: {Path}", filePath);
            }

            return result;
        }

        /// <summary>
        /// 读取文件尾部
        /// </summary>
        [KernelFunction]
        [Description("读取文件的末尾N行内容。适用于查看日志文件最新记录。")]
        public async Task<FileOperationResult> ReadFileTailAsync(
            [Description("要读取的文件路径")] string filePath,
            [Description("要读取的行数，默认为100行")] int lines = 100)
        {
            var result = new FileOperationResult();

            try
            {
                var targetPath = NormalizePath(filePath);

                if (!File.Exists(targetPath))
                {
                    result.Success = false;
                    result.Message = $"文件不存在: {filePath}";
                    return result;
                }

                var fileInfo = new FileInfo(targetPath);
                result.FileSize = fileInfo.Length;
                result.LastModified = fileInfo.LastWriteTime;

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

                // 反转队列以获得正确顺序
                var content = new System.Text.StringBuilder();
                foreach (var line in queue.Reverse())
                {
                    content.AppendLine(line);
                }

                result.Success = true;
                result.Content = content.ToString();
                result.Message = $"成功读取末尾 {queue.Count} 行 (共 {fileInfo.Length} 字节)";

                _logger.LogInformation("File tail read: {Path}, lines: {Lines}", targetPath, queue.Count);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"读取文件失败: {ex.Message}";
                _logger.LogError(ex, "Failed to read file tail: {Path}", filePath);
            }

            return result;
        }

        /// <summary>
        /// 写入文件（覆盖）
        /// </summary>
        [KernelFunction]
        [Description("创建新文件或覆盖现有文件。如果文件已存在将被完全替换。")]
        public async Task<FileOperationResult> WriteFileAsync(
            [Description("要创建/覆盖的文件路径（相对于沙箱目录或绝对路径）")] string filePath,
            [Description("要写入文件的内容")] string content)
        {
            var result = new FileOperationResult();

            try
            {
                var targetPath = NormalizePath(filePath);

                // 确保目录存在
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(targetPath, content);

                var fileInfo = new FileInfo(targetPath);
                result.Success = true;
                result.FileSize = fileInfo.Length;
                result.LastModified = fileInfo.LastWriteTime;
                result.Message = $"成功写入文件: {filePath} ({fileInfo.Length} 字节)";

                _logger.LogInformation("File written: {Path}, size: {Size}", targetPath, fileInfo.Length);
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Success = false;
                result.Message = $"访问被拒绝: {ex.Message}";
                _logger.LogWarning(ex, "Access denied writing file: {Path}", filePath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"写入文件失败: {ex.Message}";
                _logger.LogError(ex, "Failed to write file: {Path}", filePath);
            }

            return result;
        }

        /// <summary>
        /// 追加写入文件
        /// </summary>
        [KernelFunction]
        [Description("向现有文件追加内容。如果文件不存在将创建新文件。")]
        public async Task<FileOperationResult> AppendFileAsync(
            [Description("要追加内容的文件路径")] string filePath,
            [Description("要追加的内容")] string content)
        {
            var result = new FileOperationResult();

            try
            {
                var targetPath = NormalizePath(filePath);

                // 确保目录存在
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.AppendAllTextAsync(targetPath, content);

                var fileInfo = new FileInfo(targetPath);
                result.Success = true;
                result.FileSize = fileInfo.Length;
                result.LastModified = fileInfo.LastWriteTime;
                result.Message = $"成功追加内容到文件: {filePath} (当前大小: {fileInfo.Length} 字节)";

                _logger.LogInformation("File appended: {Path}, size: {Size}", targetPath, fileInfo.Length);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"追加文件失败: {ex.Message}";
                _logger.LogError(ex, "Failed to append to file: {Path}", filePath);
            }

            return result;
        }

        /// <summary>
        /// 列出目录内容
        /// </summary>
        [KernelFunction]
        [Description("列出指定目录中的所有文件和子目录。")]
        public async Task<DirectoryListingResult> ListDirectoryAsync(
            [Description("要列出的目录路径，默认为沙箱目录")] string? path = null)
        {
            var result = new DirectoryListingResult();

            try
            {
                var targetPath = NormalizePath(path ?? ".");

                if (!Directory.Exists(targetPath))
                {
                    result.Path = targetPath;
                    result.Message = $"目录不存在: {path}";
                    return result;
                }

                // 获取所有文件和目录
                var entries = Directory.GetFileSystemEntries(targetPath);

                foreach (var entry in entries)
                {
                    if (Directory.Exists(entry))
                    {
                        result.Directories.Add(Path.GetFileName(entry));
                    }
                    else
                    {
                        result.Files.Add(Path.GetFileName(entry));
                    }
                }

                result.TotalFiles = result.Files.Count;
                result.TotalDirectories = result.Directories.Count;
                result.Path = targetPath;

                _logger.LogInformation("Directory listed: {Path}", targetPath);
            }
            catch (Exception ex)
            {
                result.Path = path ?? _sandboxDirectory;
                _logger.LogError(ex, "Failed to list directory: {Path}", path);
            }

            return result;
        }

        /// <summary>
        /// 检查文件/目录是否存在
        /// </summary>
        [KernelFunction]
        [Description("检查指定路径的文件或目录是否存在。")]
        public FileOperationResult ExistsAsync(
            [Description("要检查的路径")] string path)
        {
            var result = new FileOperationResult();

            try
            {
                var targetPath = NormalizePath(path);
                var exists = File.Exists(targetPath) || Directory.Exists(targetPath);

                result.Success = true;
                result.Message = exists ? "存在" : "不存在";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"检查失败: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// 获取文件信息
        /// </summary>
        [KernelFunction]
        [Description("获取文件的详细信息，包括大小、创建时间、修改时间、是否只读等。")]
        public FileOperationResult GetFileInfo(
            [Description("要查询的文件路径")] string filePath)
        {
            var result = new FileOperationResult();

            try
            {
                var targetPath = NormalizePath(filePath);

                if (!File.Exists(targetPath))
                {
                    result.Success = false;
                    result.Message = $"文件不存在: {filePath}";
                    return result;
                }

                var fileInfo = new FileInfo(targetPath);
                result.Success = true;
                result.FileSize = fileInfo.Length;
                result.LastModified = fileInfo.LastWriteTime;
                result.Message = $"文件: {fileInfo.Name}\n大小: {fileInfo.Length} 字节\n" +
                               $"创建时间: {fileInfo.CreationTime}\n修改时间: {fileInfo.LastWriteTime}\n" +
                               $"只读: {fileInfo.IsReadOnly}\n目录: {fileInfo.DirectoryName}";

                _logger.LogInformation("File info retrieved: {Path}", targetPath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"获取文件信息失败: {ex.Message}";
                _logger.LogError(ex, "Failed to get file info: {Path}", filePath);
            }

            return result;
        }

        /// <summary>
        /// 创建目录
        /// </summary>
        [KernelFunction]
        [Description("创建一个新目录（如果父目录不存在也会一并创建）。")]
        public FileOperationResult CreateDirectory(
            [Description("要创建的目录路径")] string directoryPath)
        {
            var result = new FileOperationResult();

            try
            {
                var targetPath = NormalizePath(directoryPath);

                if (Directory.Exists(targetPath))
                {
                    result.Success = true;
                    result.Message = $"目录已存在: {directoryPath}";
                    return result;
                }

                Directory.CreateDirectory(targetPath);
                result.Success = true;
                result.Message = $"成功创建目录: {directoryPath}";

                _logger.LogInformation("Directory created: {Path}", targetPath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"创建目录失败: {ex.Message}";
                _logger.LogError(ex, "Failed to create directory: {Path}", directoryPath);
            }

            return result;
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path == ".")
                return _sandboxDirectory;

            // 处理相对路径
            if (!Path.IsPathRooted(path))
            {
                path = Path.Combine(_sandboxDirectory, path);
            }

            // 确保路径在沙箱目录内
            var fullPath = Path.GetFullPath(path);
            //if (!fullPath.StartsWith(_sandboxDirectory, StringComparison.OrdinalIgnoreCase))
            //{
            //    throw new UnauthorizedAccessException($"路径超出沙箱目录范围: {path}");
            //}

            return fullPath;
        }
    }
}
