using System.Text.RegularExpressions;
using InsightaAI.LLM.Abstractions;

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

        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
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
        // 简单的通配符匹配
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }
}
