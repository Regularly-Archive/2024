using System.Text;

namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// 文件系统接口
/// </summary>
public interface IFileSystem
{
    /// <summary>
    /// 读取文件全部内容
    /// </summary>
    Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按行读取文件内容
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="offset">起始行号（从 0 开始）</param>
    /// <param name="limit">读取行数</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<FileContent> ReadFileLinesAsync(
        string path,
        int? offset = null,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入文件（覆盖）
    /// </summary>
    Task WriteFileAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken = default);

    /// <summary>
    /// 追加写入文件
    /// </summary>
    Task AppendFileAsync(string path, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查文件/目录是否存在
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出目录内容
    /// </summary>
    Task<string[]> ListDirectoryAsync(string path, bool recursive = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按文件名模式搜索文件
    /// </summary>
    Task<string[]> GlobAsync(string pattern, string? basePath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按文件名模式搜索文件，并应用排除规则
    /// </summary>
    Task<string[]> GlobAsync(
        string pattern,
        string? basePath,
        GlobOptions? options,
        CancellationToken cancellationToken = default) => GlobAsync(pattern, basePath, cancellationToken);

    /// <summary>
    /// 搜索文件内容
    /// </summary>
    Task<GrepResult> GrepAsync(string pattern, string path, GrepOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检测文件编码（基于 BOM 和内容分析）
    /// </summary>
    Encoding DetectEncoding(string filePath);
}

/// <summary>
/// 文件内容
/// </summary>
public record FileContent
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// 文件内容
    /// </summary>
    public string Content { get; init; } = "";

    /// <summary>
    /// 总行数
    /// </summary>
    public int TotalLines { get; init; }

    /// <summary>
    /// 返回的起始行号
    /// </summary>
    public int StartLine { get; init; }

    /// <summary>
    /// 返回的行数
    /// </summary>
    public int LineCount { get; init; }
}

/// <summary>
/// Grep 搜索结果
/// </summary>
public record GrepResult
{
    /// <summary>
    /// 匹配的行
    /// </summary>
    public IReadOnlyList<GrepMatch> Matches { get; init; } = Array.Empty<GrepMatch>();

    /// <summary>
    /// 匹配的文件数
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// 总匹配数
    /// </summary>
    public int TotalMatches { get; init; }

    /// <summary>
    /// 是否被截断
    /// </summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Grep 匹配项
/// </summary>
public record GrepMatch
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public string FilePath { get; init; } = "";

    /// <summary>
    /// 行号
    /// </summary>
    public int LineNumber { get; init; }

    /// <summary>
    /// 行内容
    /// </summary>
    public string LineContent { get; init; } = "";
}

/// <summary>
/// Grep 搜索选项
/// </summary>
public record GrepOptions
{
    /// <summary>
    /// 是否递归搜索子目录
    /// </summary>
    public bool Recursive { get; init; } = true;

    /// <summary>
    /// 是否忽略大小写
    /// </summary>
    public bool IgnoreCase { get; init; }

    /// <summary>
    /// 是否使用正则表达式
    /// </summary>
    public bool UseRegex { get; init; } = true;

    /// <summary>
    /// 是否显示行号
    /// </summary>
    public bool ShowLineNumbers { get; init; } = true;

    /// <summary>
    /// 是否只显示文件名
    /// </summary>
    public bool FilesOnly { get; init; }

    /// <summary>
    /// 要排除的模式
    /// </summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 最大返回结果数
    /// </summary>
    public int? MaxResults { get; init; }
}

/// <summary>
/// Glob 搜索选项
/// </summary>
public record GlobOptions
{
    /// <summary>
    /// 默认忽略的常见构建和依赖目录。
    /// </summary>
    public static IReadOnlyList<string> DefaultExcludePatterns { get; } =
        ["**/bin/**", "**/obj/**", "**/node_modules/**"];

    /// <summary>
    /// 是否应用 <see cref="DefaultExcludePatterns"/>。
    /// </summary>
    public bool UseDefaultExcludes { get; init; } = true;

    /// <summary>
    /// 要额外排除的 glob 模式。
    /// </summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();
}
