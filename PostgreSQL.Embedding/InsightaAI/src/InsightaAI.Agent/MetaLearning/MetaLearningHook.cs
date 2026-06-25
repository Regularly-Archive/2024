using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Hooks;

namespace InsightaAI.Agent.MetaLearning;

/// <summary>
/// 元学习 Hook - 自动捕获工具错误并记录教训
/// </summary>
public sealed class MetaLearningHook : IToolHook
{
    private readonly MetaLearningStore _store;
    private readonly Func<string, string, CancellationToken, Task>? _onLessonLearned;

    /// <summary>
    /// 创建 MetaLearningHook
    /// </summary>
    /// <param name="store">元学习存储</param>
    /// <param name="onLessonLearned">可选回调：当教训被记录时触发（可用于通知 UI）</param>
    public MetaLearningHook(
        MetaLearningStore store,
        Func<string, string, CancellationToken, Task>? onLessonLearned = null)
    {
        _store = store;
        _onLessonLearned = onLessonLearned;
    }

    /// <summary>
    /// MetaLearningHook 不需要拦截执行前，始终允许
    /// </summary>
    public Task<ToolHookResult> OnBeforeExecutionAsync(
        string toolName, string arguments, ToolExecutionContext context)
    {
        return Task.FromResult(ToolHookResult.Allow);
    }

    /// <summary>
    /// 工具执行后检查是否失败，自动记录教训
    /// </summary>
    public async Task OnAfterExecutionAsync(
        string toolName,
        ToolResult result,
        ToolExecutionContext context)
    {
        // 只处理失败的工具调用
        if (!result.IsError)
            return;

        var errorText = result.Content?
            .OfType<LLM.Models.TextBlock>()
            .FirstOrDefault()?.Text ?? "";

        if (string.IsNullOrWhiteSpace(errorText))
            return;

        // 生成教训和去重键
        var lesson = GenerateLesson(toolName, errorText);
        var dedupKey = $"{toolName.ToLowerInvariant()}:{ExtractErrorType(errorText)}";

        // 写入教训（内置去重检查，避免 TOCTOU 竞态）
        await _store.AppendLessonIfNotExistsAsync("tools", lesson, context.CancellationToken, dedupKey);

        // 触发回调
        if (_onLessonLearned != null)
        {
            await _onLessonLearned(toolName, lesson, context.CancellationToken);
        }
    }

    /// <summary>
    /// 从工具错误中生成解决方案导向的教训
    /// </summary>
    private static string GenerateLesson(string toolName, string error)
    {
        var tool = toolName.ToLowerInvariant();
        var errorLower = error.ToLowerInvariant();

        // Bash 工具的常见错误 → 解决方案
        if (tool == "bash")
        {
            if (errorLower.Contains("command not found") || errorLower.Contains("is not recognized"))
            {
                var cmd = ExtractCommand(error);
                return cmd switch
                {
                    "curl" => "Windows: 用 curl.exe 或 Invoke-WebRequest 代替 curl（PowerShell 别名冲突）",
                    "grep" => "Windows: 用 Select-String 或 findstr 代替 grep",
                    "ls" => "Windows: 用 Get-ChildItem (别名 gci/dir) 代替 ls",
                    "cat" => "Windows: 用 Get-Content (别名 gc/type) 代替 cat",
                    "rm" => "Windows: 用 Remove-Item (别名 ri/del/erase) 代替 rm",
                    "mkdir" => "Windows: 用 New-Item -ItemType Directory 代替 mkdir",
                    _ => $"命令 `{cmd}` 不存在: 用 Get-Command {cmd} 检查是否安装，或搜索替代命令"
                };
            }
            if (errorLower.Contains("permission denied"))
            {
                return "权限不足: 以管理员身份运行，或检查文件/目录的 ACL 权限设置";
            }
            if (errorLower.Contains("no such file or directory"))
            {
                return "路径不存在: 先用 Glob 或 Test-Path 确认路径存在，再执行操作";
            }
            if (errorLower.Contains("is not recognized as an internal or external command"))
            {
                var cmd = ExtractCommand(error);
                return $"命令 `{cmd}` 未找到: 检查 PATH 环境变量或使用完整路径";
            }
        }

        // 文件操作工具的常见错误 → 解决方案
        if (tool is "read_file" or "edit_file" or "write_file")
        {
            if (errorLower.Contains("file not found") || errorLower.Contains("does not exist"))
            {
                return $"文件不存在: 先用 Glob 查找正确路径，再执行 {tool} 操作";
            }
            if (errorLower.Contains("access denied") || errorLower.Contains("permission"))
            {
                return $"文件被占用或无权限: 检查是否有其他进程锁定文件，或用管理员权限运行";
            }
            if (errorLower.Contains("path is not valid") || errorLower.Contains("invalid path"))
            {
                return "路径格式错误: Windows 路径用反斜杠 \\，确保没有非法字符";
            }
        }

        // 通用: 提取关键信息并给出建议
        var shortError = error.Length > 100 ? error[..100] + "..." : error;
        return $"`{toolName}` 失败: {shortError} → 检查参数是否正确，或查阅文档";
    }

    /// <summary>
    /// 从错误消息中提取命令名
    /// </summary>
    private static string ExtractCommand(string error)
    {
        // "curl: command not found" → "curl"
        // "'curl' is not recognized" → "curl"
        var match = System.Text.RegularExpressions.Regex.Match(error, @"'?(\w+)'?\s+(?:command not found|is not recognized)");
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    /// <summary>
    /// 从错误消息中提取语义错误类型
    /// </summary>
    private static string ExtractErrorType(string error)
    {
        var lower = error.ToLowerInvariant();

        // 命令不存在
        if (lower.Contains("command not found") || lower.Contains("is not recognized"))
            return "command_not_found";

        // 权限不足
        if (lower.Contains("permission denied") || lower.Contains("access denied"))
            return "permission_denied";

        // 文件/路径不存在
        if (lower.Contains("no such file") || lower.Contains("file not found") || lower.Contains("does not exist"))
            return "file_not_found";

        // 路径无效
        if (lower.Contains("path is not valid") || lower.Contains("invalid path"))
            return "invalid_path";

        // 网络错误
        if (lower.Contains("connection refused") || lower.Contains("timeout") || lower.Contains("network"))
            return "network_error";

        // 语法错误
        if (lower.Contains("syntax error") || lower.Contains("unexpected token"))
            return "syntax_error";

        // 类型错误
        if (lower.Contains("type error") || lower.Contains("cannot convert"))
            return "type_error";

        // 超时
        if (lower.Contains("timed out") || lower.Contains("deadline exceeded"))
            return "timeout";

        // 通用：取前 30 个字符作为 fallback
        var shortError = error.Length > 30 ? error[..30] : error;
        return $"unknown_{shortError.GetHashCode():x}";
    }
}
