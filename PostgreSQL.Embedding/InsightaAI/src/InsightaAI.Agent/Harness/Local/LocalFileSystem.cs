using InsightaAI.Agent.Abstractions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using System.Text;
using System.Text.RegularExpressions;

namespace InsightaAI.Agent.Harness.Local;

/// <summary>
/// 本地文件系统实现
/// </summary>
public class LocalFileSystem : IFileSystem
{
    public async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var encoding = DetectEncoding(fullPath);
        return await File.ReadAllTextAsync(fullPath, encoding, cancellationToken);
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

    public async Task WriteFileAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir ?? Path.GetTempPath(), Guid.NewGuid() + ".tmp");
        try
        {
            for (var retry = 0; ; retry++)
            {
                try
                {
                    await File.WriteAllTextAsync(tempPath, content, encoding, cancellationToken);
                    File.Move(tempPath, fullPath, overwrite: true);
                    return;
                }
                catch (IOException) when (retry < 5)
                {
                    await Task.Delay(Random.Shared.Next(50, 200) * (retry + 1), cancellationToken);
                }
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    public async Task AppendFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var encoding = DetectEncoding(fullPath);

        // 确保目录存在
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.AppendAllTextAsync(fullPath, content, encoding, cancellationToken);
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
        => GlobAsync(pattern, basePath, options: null, cancellationToken: cancellationToken);

    public Task<string[]> GlobAsync(
        string pattern,
        string? basePath,
        GlobOptions? options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var searchPath = basePath != null ? Path.GetFullPath(basePath) : Directory.GetCurrentDirectory();

        if (!Directory.Exists(searchPath))
            throw new DirectoryNotFoundException($"Directory not found: {searchPath}");

        options ??= new GlobOptions();
        var excludePatterns = options.UseDefaultExcludes
            ? GlobOptions.DefaultExcludePatterns.Concat(options.ExcludePatterns)
            : options.ExcludePatterns;

        // Microsoft.Extensions.FileSystemGlobbing does not implement the ? wildcard.
        // Use the same wildcard matcher as excludes when it appears in an include pattern.
        if (pattern.Contains('?'))
        {
            var filesWithQuestionWildcard = Directory.EnumerateFiles(searchPath, "*", SearchOption.AllDirectories)
                .Where(file =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(searchPath, file).Replace('\\', '/');
                    return MatchWildcardPattern(pattern, relativePath) &&
                        !excludePatterns.Any(exclude => MatchWildcardPattern(exclude, relativePath));
                })
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.FromResult(filesWithQuestionWildcard);
        }

        var matcher = new Matcher();
        matcher.AddInclude(pattern);
        foreach (var excludePattern in excludePatterns.Distinct(StringComparer.OrdinalIgnoreCase))
            matcher.AddExclude(excludePattern);

        var result = matcher.Execute(
            new DirectoryInfoWrapper(new DirectoryInfo(searchPath))
        );

        if (!result.HasMatches) return Task.FromResult(Array.Empty<string>());

        var files = result.Files
            .Select(x => Path.GetFullPath(Path.Combine(searchPath, x.Path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(files);
    }

    public async Task<GrepResult> GrepAsync(string pattern, string path, GrepOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new GrepOptions();
        if (options.MaxResults is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxResults must be greater than zero.");

        var fullPath = Path.GetFullPath(path);
        var matches = new List<GrepMatch>();
        var filesSearched = new HashSet<string>();

        try
        {
            IEnumerable<string> filesToSearch;
            var searchRoot = fullPath;
            if (File.Exists(fullPath))
            {
                filesToSearch = new[] { fullPath };
                searchRoot = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
            }
            else if (Directory.Exists(fullPath))
            {
                filesToSearch = Directory.EnumerateFiles(fullPath, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = options.Recursive,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false
                });
            }
            else
            {
                return new GrepResult
                {
                    Matches = Array.Empty<GrepMatch>(),
                    FileCount = 0,
                    TotalMatches = 0
                };
            }

            var regexOptions = RegexOptions.CultureInvariant;
            if (options.IgnoreCase) regexOptions |= RegexOptions.IgnoreCase;

            Regex? regex = null;
            if (options.UseRegex)
                regex = new Regex(pattern, regexOptions, TimeSpan.FromSeconds(2));

            foreach (var file in filesToSearch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(searchRoot, file);
                if (options.ExcludePatterns.Any(excludePattern =>
                    MatchWildcardPattern(excludePattern, relativePath)))
                {
                    continue;
                }

                try
                {
                    if (await IsProbablyBinaryAsync(file, cancellationToken))
                        continue;

                    using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    filesSearched.Add(file);
                    var lineNumber = 0;
                    while (await reader.ReadLineAsync(cancellationToken) is { } line)
                    {
                        lineNumber++;
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
                                LineNumber = lineNumber,
                                LineContent = line
                            });

                            if (options.MaxResults.HasValue && matches.Count >= options.MaxResults.Value)
                            {
                                return new GrepResult
                                {
                                    Matches = matches,
                                    FileCount = filesSearched.Count,
                                    TotalMatches = matches.Count,
                                    Truncated = true
                                };
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (RegexMatchTimeoutException ex)
                {
                    throw new InvalidOperationException($"Regex timed out while searching '{file}'.", ex);
                }
                catch
                {
                    // Skip files which cannot be read or decoded.
                }
            }

            return new GrepResult
            {
                Matches = matches,
                FileCount = filesSearched.Count,
                TotalMatches = matches.Count,
                Truncated = false
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Grep failed: {ex.Message}", ex);
        }
    }

    private static async Task<bool> IsProbablyBinaryAsync(string path, CancellationToken cancellationToken)
    {
        const int sampleSize = 8 * 1024;
        var buffer = new byte[sampleSize];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: sampleSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
        return buffer.AsSpan(0, count).Contains((byte)0);
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
            else
            {
                sb.Append(Regex.Escape(pattern[i].ToString()));
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

    public Encoding DetectEncoding(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.Default, detectEncodingFromByteOrderMarks: true);
        reader.Read();
        return reader.CurrentEncoding;
    }
}
