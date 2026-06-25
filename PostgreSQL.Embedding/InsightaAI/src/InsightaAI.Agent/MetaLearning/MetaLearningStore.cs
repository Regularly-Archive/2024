using System.Text;

namespace InsightaAI.Agent.MetaLearning;

/// <summary>
/// 元学习存储 - 管理 meta-learning 目录下的 markdown 文件
/// </summary>
public sealed class MetaLearningStore
{
    private readonly string _lessonsDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>
    /// 所有教训文件的名称（不含 .md 后缀）
    /// </summary>
    public static readonly string[] LessonFiles = ["tools", "environment", "workflows"];

    /// <summary>
    /// 每个文件最大教训条目数，超过后自动裁剪
    /// </summary>
    public const int MaxLessonsPerFile = 50;

    public MetaLearningStore(string lessonsDirectory)
    {
        _lessonsDirectory = lessonsDirectory;
    }

    /// <summary>
    /// 使用默认路径创建（~/.agents/skills/meta-learning/）
    /// </summary>
    public MetaLearningStore() : this(GetDefaultLessonsDirectory()) { }

    /// <summary>
    /// 确保 lessons 目录和初始文件存在，并部署 SKILL.md
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_lessonsDirectory);

        // 部署 SKILL.md（从嵌入资源复制）
        var skillPath = Path.Combine(_lessonsDirectory, "SKILL.md");
        if (!File.Exists(skillPath))
        {
            await ExtractEmbeddedSkillAsync(skillPath, cancellationToken);
        }

        // 初始化教训文件
        foreach (var name in LessonFiles)
        {
            var path = GetFilePath(name);
            if (!File.Exists(path))
            {
                var header = name switch
                {
                    "tools" => "# 工具使用教训\n\n<!-- 由 hooks 自动写入，记录工具调用的正确做法 -->\n",
                    "environment" => "# 环境相关教训\n\n<!-- 记录 OS、Shell、路径等环境相关正确做法 -->\n",
                    "workflows" => "# 工作流最佳实践\n\n<!-- Agent 主动记录的工作流模式和最佳实践 -->\n",
                    _ => $"# {name} 教训\n\n"
                };
                await File.WriteAllTextAsync(path, header, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 从嵌入资源中提取 SKILL.md
    /// </summary>
    private static async Task ExtractEmbeddedSkillAsync(string targetPath, CancellationToken cancellationToken)
    {
        var assembly = typeof(MetaLearningStore).Assembly;
        var resourceName = "InsightaAI.Agent.MetaLearning.SKILL.md";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return;

        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);
        await File.WriteAllTextAsync(targetPath, content, cancellationToken);
    }

    /// <summary>
    /// 追加一条教训到指定文件，并更新 SKILL.md 索引
    /// </summary>
    public async Task AppendLessonAsync(string category, string lesson, CancellationToken cancellationToken = default)
    {
        var fileName = LessonFiles.Contains(category) ? category : "tools";
        var path = GetFilePath(fileName);

        // 确保目录存在
        Directory.CreateDirectory(_lessonsDirectory);

        // 日期标记
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var entry = $"\n- [{date}] {lesson}\n";

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, entry, cancellationToken);

            // 更新 SKILL.md 索引
            await UpdateSkillIndexAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 追加教训（带去重检查，原子操作避免竞态）
    /// </summary>
    /// <param name="category">教训分类</param>
    /// <param name="lesson">教训内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="dedupKey">去重键，格式: {tool}:{error_type}，如 bash:command_not_found</param>
    public async Task AppendLessonIfNotExistsAsync(
        string category,
        string lesson,
        CancellationToken cancellationToken = default,
        string? dedupKey = null)
    {
        var fileName = LessonFiles.Contains(category) ? category : "tools";
        var path = GetFilePath(fileName);

        // 确保目录存在
        Directory.CreateDirectory(_lessonsDirectory);

        // 生成去重键：优先用结构化 key，回退到关键词匹配
        var effectiveKey = dedupKey ?? ExtractKeyword(lesson);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            // 在锁内检查是否已有类似教训
            if (!string.IsNullOrEmpty(effectiveKey) && File.Exists(path))
            {
                var existingContent = await File.ReadAllTextAsync(path, cancellationToken);

                // 结构化 key 用精确匹配，关键词用单词边界匹配
                bool isDuplicate;
                if (dedupKey != null)
                {
                    // 精确匹配 [key] 标记
                    isDuplicate = existingContent.Contains($"[{dedupKey}]",
                        StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    // 回退到单词边界匹配
                    var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(effectiveKey)}\b";
                    isDuplicate = System.Text.RegularExpressions.Regex.IsMatch(existingContent, pattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }

                if (isDuplicate)
                {
                    return; // 已有类似教训，跳过
                }
            }

            // 写入教训（带结构化标记）
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var tag = dedupKey != null ? $" [{dedupKey}]" : "";
            var entry = $"\n- [{date}]{tag} {lesson}\n";
            await File.AppendAllTextAsync(path, entry, cancellationToken);

            // 更新 SKILL.md 索引
            await UpdateSkillIndexAsync(cancellationToken);

            // 自动裁剪：超过阈值时保留最近的条目
            await TrimIfNeededAsync(path, MaxLessonsPerFile, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 裁剪教训文件，只保留最近的 maxCount 条
    /// </summary>
    private async Task TrimIfNeededAsync(string path, int maxCount, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return;

        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var lines = content.Split('\n');

        // 提取头部（非教训行）和教训条目
        var headerLines = lines.TakeWhile(l => !l.TrimStart().StartsWith("- [")).ToArray();
        var lessonLines = lines.Where(l => l.TrimStart().StartsWith("- [")).ToArray();

        if (lessonLines.Length <= maxCount)
            return;

        // 只保留最近的 maxCount 条
        var keptLessons = lessonLines.TakeLast(maxCount).ToArray();
        var newContent = string.Join("\n", headerLines) + "\n" + string.Join("\n", keptLessons) + "\n";

        await File.WriteAllTextAsync(path, newContent, cancellationToken);
    }

    /// <summary>
    /// 从教训中提取关键词用于去重
    /// </summary>
    private static string? ExtractKeyword(string lesson)
    {
        // 停用词列表
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
            "have", "has", "had", "do", "does", "did", "will", "would", "could",
            "should", "may", "might", "can", "shall", "must", "need",
            "to", "of", "in", "for", "on", "with", "at", "by", "from",
            "this", "that", "these", "those", "it", "its",
            "and", "or", "but", "not", "no", "nor",
            "use", "used", "using", "check", "try", "make", "run",
            "先", "用", "再", "的", "了", "是", "不", "要", "会", "能"
        };

        // 提取引号中的内容作为关键词
        var quoteMatch = System.Text.RegularExpressions.Regex.Match(lesson, @"`([^`]+)`");
        if (quoteMatch.Success)
            return quoteMatch.Groups[1].Value;

        // 提取第一个非停用词的英文单词
        var wordMatches = System.Text.RegularExpressions.Regex.Matches(lesson, @"\b(\w{3,})\b");
        foreach (System.Text.RegularExpressions.Match match in wordMatches)
        {
            var word = match.Groups[1].Value;
            if (!stopWords.Contains(word))
                return word;
        }

        return null;
    }

    /// <summary>
    /// 更新 SKILL.md 中的教训索引
    /// </summary>
    private async Task UpdateSkillIndexAsync(CancellationToken cancellationToken)
    {
        var skillPath = Path.Combine(_lessonsDirectory, "SKILL.md");
        if (!File.Exists(skillPath))
            return;

        var content = await File.ReadAllTextAsync(skillPath, cancellationToken);

        // 构建索引内容
        var indexBuilder = new StringBuilder();
        indexBuilder.AppendLine("## 教训文件索引");
        indexBuilder.AppendLine();
        indexBuilder.AppendLine("| 文件 | 教训数量 | 最近更新 |");
        indexBuilder.AppendLine("|------|----------|----------|");

        foreach (var name in LessonFiles)
        {
            var lessonPath = GetFilePath(name);
            if (!File.Exists(lessonPath))
            {
                indexBuilder.AppendLine($"| {name}.md | 0 | - |");
                continue;
            }

            var lessonContent = await File.ReadAllTextAsync(lessonPath, cancellationToken);
            var lessonCount = lessonContent.Split('\n')
                .Count(l => l.TrimStart().StartsWith("- ["));

            // 获取最后一条教训的日期
            var lastLesson = lessonContent.Split('\n')
                .LastOrDefault(l => l.TrimStart().StartsWith("- ["));
            var lastDate = "-";
            if (lastLesson != null)
            {
                var dateMatch = System.Text.RegularExpressions.Regex.Match(lastLesson, @"\[(\d{4}-\d{2}-\d{2})\]");
                if (dateMatch.Success)
                    lastDate = dateMatch.Groups[1].Value;
            }

            indexBuilder.AppendLine($"| {name}.md | {lessonCount} | {lastDate} |");
        }

        indexBuilder.AppendLine();

        // 替换或追加索引部分
        var indexMarker = "## 教训文件索引";

        if (content.Contains(indexMarker))
        {
            // 替换现有索引
            var markerStart = content.IndexOf(indexMarker);
            var existingIndexEnd = content.IndexOf("## ", markerStart + indexMarker.Length);
            var beforeIndex = content[..markerStart];
            var afterIndex = existingIndexEnd > 0 ? content[existingIndexEnd..] : "";
            content = beforeIndex + indexBuilder.ToString() + afterIndex;
        }
        else
        {
            // 追加索引到末尾
            content += "\n" + indexBuilder.ToString();
        }

        await File.WriteAllTextAsync(skillPath, content, cancellationToken);
    }

    /// <summary>
    /// 读取指定教训文件内容
    /// </summary>
    public async Task<string> ReadLessonsAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var path = GetFilePath(fileName);
        if (!File.Exists(path))
            return "";

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    /// <summary>
    /// 读取所有教训文件的索引摘要
    /// </summary>
    public async Task<string> ReadIndexAsync(CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        foreach (var name in LessonFiles)
        {
            var path = GetFilePath(name);
            if (!File.Exists(path))
                continue;

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            var lines = content.Split('\n')
                .Where(l => l.TrimStart().StartsWith("- ["))
                .ToArray();

            sb.AppendLine($"### {name}.md ({lines.Length} lessons)");
            if (lines.Length > 0)
            {
                // 只显示最近 5 条
                foreach (var line in lines.TakeLast(5))
                {
                    sb.AppendLine(line);
                }
                if (lines.Length > 5)
                {
                    sb.AppendLine($"  _...and {lines.Length - 5} more_");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 检查某条教训是否已存在（单词边界匹配）
    /// </summary>
    public async Task<bool> HasSimilarLessonAsync(string fileName, string keyword, CancellationToken cancellationToken = default)
    {
        var content = await ReadLessonsAsync(fileName, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            return false;

        // 使用单词边界匹配，避免 "read" 匹配到 "bread"
        var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(keyword)}\b";
        return System.Text.RegularExpressions.Regex.IsMatch(content, pattern,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private string GetFilePath(string fileName)
    {
        return Path.Combine(_lessonsDirectory, $"{fileName}.md");
    }

    private static string GetDefaultLessonsDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".agents", "skills", "meta-learning");
    }
}
