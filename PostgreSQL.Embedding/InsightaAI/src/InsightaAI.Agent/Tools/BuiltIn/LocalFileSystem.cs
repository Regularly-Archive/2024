using System.Text.RegularExpressions;
using InsightaAI.Agent.Abstractions;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// 本地文件系统实现
/// </summary>
public class LocalFileSystem : IFileSystem
{
    public async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    public async Task<FileContent> ReadFileLinesAsync(
        string path,
        int? offset = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);

        var totalLines = lines.Length;
        var startLine = offset ?? 0;

        // 确保 startLine 在有效范围内
        startLine = Math.Max(0, Math.Min(startLine, totalLines));

        // 获取指定范围的行
        var selectedLines = limit.HasValue
            ? lines.Skip(startLine).Take(limit.Value).ToArray()
            : lines.Skip(startLine).ToArray();

        return new FileContent
        {
            Path = fullPath,
            Content = string.Join(Environment.NewLine, selectedLines),
            TotalLines = totalLines,
            StartLine = startLine,
            LineCount = selectedLines.Length
        };
    }

    public async Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);

        // 确保目录存在
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // 原子写入：先写临时文件，再替换原文件
        var tempPath = fullPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, content, cancellationToken);
        File.Move(tempPath, fullPath, overwrite: true);
    }

    public async Task AppendFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);

        // 确保目录存在
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.AppendAllTextAsync(fullPath, content, cancellationToken);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        return Task.FromResult(File.Exists(fullPath) || Directory.Exists(fullPath));
    }

    public Task<string[]> ListDirectoryAsync(string path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {fullPath}");
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var entries = Directory.GetFileSystemEntries(fullPath, "*", option);

        return Task.FromResult(entries);
    }

    public Task<string[]> GlobAsync(string pattern, string? basePath = null, CancellationToken cancellationToken = default)
    {
        var searchPath = basePath != null ? Path.GetFullPath(basePath) : Directory.GetCurrentDirectory();

        // 简单的 glob 实现
        var files = Directory.GetFiles(searchPath, pattern, SearchOption.AllDirectories);

        return Task.FromResult(files);
    }

    public Task<GrepResult> GrepAsync(string pattern, string path, GrepOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new GrepOptions();
        var fullPath = Path.GetFullPath(path);
        var matches = new List<GrepMatch>();
        var filesSearched = new HashSet<string>();

        try
        {
            // 确定搜索路径
            string[] filesToSearch;
            if (File.Exists(fullPath))
            {
                filesToSearch = new[] { fullPath };
            }
            else if (Directory.Exists(fullPath))
            {
                var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                filesToSearch = Directory.GetFiles(fullPath, "*.*", searchOption);
            }
            else
            {
                return Task.FromResult(new GrepResult
                {
                    Matches = Array.Empty<GrepMatch>(),
                    FileCount = 0,
                    TotalMatches = 0
                });
            }

            // 过滤排除的文件
            filesToSearch = filesToSearch.Where(f =>
            {
                var relativePath = Path.GetRelativePath(fullPath, f);
                return !options.ExcludePatterns.Any(pattern =>
                    MatchWildcardPattern(pattern, relativePath));
            }).ToArray();

            // 编译正则表达式
            var regexOptions = RegexOptions.Compiled;
            if (options.IgnoreCase) regexOptions |= RegexOptions.IgnoreCase;

            Regex? regex = null;
            if (options.UseRegex)
            {
                try
                {
                    regex = new Regex(pattern, regexOptions);
                }
                catch
                {
                    // 如果正则表达式无效，使用普通字符串匹配
                }
            }

            // 搜索每个文件
            foreach (var file in filesToSearch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var lines = File.ReadAllLines(file);
                    filesSearched.Add(file);

                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i];
                        bool isMatch;

                        if (regex != null)
                        {
                            isMatch = regex.IsMatch(line);
                        }
                        else if (options.IgnoreCase)
                        {
                            isMatch = line.Contains(pattern, StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            isMatch = line.Contains(pattern);
                        }

                        if (isMatch)
                        {
                            matches.Add(new GrepMatch
                            {
                                FilePath = file,
                                LineNumber = i + 1,
                                LineContent = line
                            });

                            // 检查是否达到最大结果数
                            if (options.MaxResults.HasValue && matches.Count >= options.MaxResults.Value)
                            {
                                return Task.FromResult(new GrepResult
                                {
                                    Matches = matches,
                                    FileCount = filesSearched.Count,
                                    TotalMatches = matches.Count,
                                    Truncated = true
                                });
                            }
                        }
                    }
                }
                catch
                {
                    // 跳过无法读取的文件
                }
            }

            return Task.FromResult(new GrepResult
            {
                Matches = matches,
                FileCount = filesSearched.Count,
                TotalMatches = matches.Count,
                Truncated = false
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Grep failed: {ex.Message}", ex);
        }
    }

    private static bool MatchWildcardPattern(string pattern, string text)
    {
        // 规范化路径分隔符（Windows 兼容）
        text = text.Replace('\\', '/');
        pattern = pattern.Replace('\\', '/');

        // 如果模式不包含路径分隔符，检查是否匹配路径中的任意一段
        // 例如 pattern="bin" 应匹配 "src/bin/Debug/file.cs"（匹配 "bin" 段）
        if (!pattern.Contains('/'))
        {
            var segments = text.Split('/');
            foreach (var segment in segments)
            {
                if (MatchGlobSegment(pattern, segment))
                    return true;
            }
            return false;
        }

        // 将 glob 模式转换为正则表达式
        var regexPattern = GlobToRegex(pattern);
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// 将 glob 模式转换为正则表达式
    /// 支持: ** (匹配任意目录), * (匹配单个段), ? (匹配单个字符)
    /// </summary>
    private static string GlobToRegex(string pattern)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('^');

        int i = 0;
        while (i < pattern.Length)
        {
            if (i + 1 < pattern.Length && pattern[i] == '*' && pattern[i + 1] == '*')
            {
                // ** 匹配任意数量的目录（包括空）
                sb.Append(".*");
                i += 2;
                // 跳过随后的 /
                if (i < pattern.Length && pattern[i] == '/')
                    i++;
            }
            else if (pattern[i] == '*')
            {
                // * 匹配单个段内的任意字符（不包括 /）
                sb.Append("[^/]*");
                i++;
            }
            else if (pattern[i] == '?')
            {
                // ? 匹配单个字符（不包括 /）
                sb.Append("[^/]");
                i++;
            }
            else if (pattern[i] == '.')
            {
                // . 需要转义
                sb.Append("\\.");
                i++;
            }
            else
            {
                // 其他字符原样保留
                sb.Append(pattern[i]);
                i++;
            }
        }

        sb.Append('$');
        return sb.ToString();
    }

    /// <summary>
    /// 匹配单个路径段（不含 **）
    /// </summary>
    private static bool MatchGlobSegment(string pattern, string text)
    {
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", "[^/]") + "$";

        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }
}
