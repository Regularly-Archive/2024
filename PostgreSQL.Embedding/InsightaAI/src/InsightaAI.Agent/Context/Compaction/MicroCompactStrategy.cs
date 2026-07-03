using System.Text.Json;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Context.Compaction;

/// <summary>
/// 微压缩策略 - 零成本清理旧工具结果
/// </summary>
/// <remarks>
/// 策略逻辑：
/// 1. 保留最近 N 个工具结果的完整内容
/// 2. 对于更早的工具结果，根据工具类型进行智能截断
/// 3. 保留 Tool Use/Result 配对结构
/// 4. 保留元数据（工具名、状态、关键参数）
/// </remarks>
public sealed class MicroCompactStrategy : ICompactStrategy
{
    public string Name => "MicroCompact";
    public int Priority => 1; // 最高优先级

    /// <summary>
    /// 可压缩的工具及其截断策略
    /// </summary>
    private static readonly Dictionary<string, ToolTruncationStrategy> CompactableTools = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bash"] = new BashTruncationStrategy(),
        ["powershell"] = new BashTruncationStrategy(), // 复用 bash 策略
        ["execute_command"] = new BashTruncationStrategy(),
        ["read_file"] = new FileReadTruncationStrategy(),
        ["grep"] = new GrepTruncationStrategy(),
        ["glob"] = new GlobTruncationStrategy(),
        ["web_search"] = new WebSearchTruncationStrategy(),
        ["web_fetch"] = new WebFetchTruncationStrategy(),
        ["edit_file"] = new EditFileTruncationStrategy(),
        ["write_file"] = new WriteFileTruncationStrategy(),
    };

    public bool ShouldCompact(IReadOnlyList<Message> messages, int estimatedTokens, ContextBudget budget)
    {
        // 检查是否达到微压缩阈值
        if (estimatedTokens < budget.MicroCompactTriggerTokens)
            return false;

        // 检查是否有可压缩的工具结果
        return HasCompactableToolResults(messages, budget.KeepRecentToolResults);
    }

    public Task<CompactionResult> CompactAsync(
        List<Message> messages,
        ContextBudget budget,
        ITokenEstimator tokenEstimator,
        int preCompactTokens,
        CancellationToken cancellationToken = default)
    {
        var preCompactMessages = messages.Count;

        // 找出所有 tool_use + tool_result 配对
        var toolPairs = FindToolPairs(messages);

        // 按时间倒序排列（最新的在前）
        toolPairs.Reverse();

        int compactedCount = 0;

        for (int i = 0; i < toolPairs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (toolUseIndex, toolResultIndex, toolName) = toolPairs[i];

            // 保留最近 N 个工具结果
            if (i < budget.KeepRecentToolResults)
                continue;

            // 检查是否是可压缩的工具
            if (!CompactableTools.TryGetValue(toolName, out var truncationStrategy))
                continue;

            // 获取工具结果消息
            var toolResultMessage = messages[toolResultIndex];

            // 截断工具结果
            var truncatedContent = TruncateToolResult(toolResultMessage, toolName, truncationStrategy);

            // 替换消息内容
            messages[toolResultIndex] = new Message
            {
                Role = MessageRole.ToolResult,
                ToolCallId = toolResultMessage.ToolCallId,
                ToolName = toolResultMessage.ToolName,
                Content = [new TextBlock { Text = truncatedContent }]
            };

            compactedCount++;
        }

        // 估算压缩后的 token 数量
        var postCompactTokens = EstimateMessagesTokens(messages, tokenEstimator);

        return Task.FromResult(new CompactionResult
        {
            StrategyName = Name,
            PreCompactTokens = preCompactTokens,
            PostCompactTokens = postCompactTokens,
            PreCompactMessages = preCompactMessages,
            PostCompactMessages = messages.Count,
            RequestMessages = messages.ToArray()
        });
    }

    /// <summary>
    /// 查找所有 tool_use + tool_result 配对
    /// </summary>
    /// <returns>列表，每项包含 (toolUseIndex, toolResultIndex, toolName)</returns>
    private static List<(int ToolUseIndex, int ToolResultIndex, string ToolName)> FindToolPairs(List<Message> messages)
    {
        var pairs = new List<(int, int, string)>();

        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];

            // 查找包含 tool_use 的 Assistant 消息
            if (message.Role == MessageRole.Assistant)
            {
                foreach (var block in message.Content)
                {
                    if (block is ToolCallBlock toolCall)
                    {
                        // 查找对应的 tool_result
                        for (int j = i + 1; j < messages.Count; j++)
                        {
                            var potentialResult = messages[j];
                            if (potentialResult.Role == MessageRole.ToolResult &&
                                potentialResult.ToolCallId == toolCall.Id)
                            {
                                pairs.Add((i, j, toolCall.Name));
                                break;
                            }
                        }
                    }
                }
            }
        }

        return pairs;
    }

    /// <summary>
    /// 检查是否有可压缩的工具结果
    /// </summary>
    private static bool HasCompactableToolResults(IReadOnlyList<Message> messages, int keepRecent)
    {
        int toolResultCount = 0;

        for (int i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Role == MessageRole.ToolResult)
            {
                toolResultCount++;

                // 超过保留数量后，检查是否有可压缩的
                if (toolResultCount > keepRecent)
                {
                    var toolName = messages[i].ToolName ?? "";
                    if (CompactableTools.ContainsKey(toolName))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 估算消息列表的 token 数量
    /// </summary>
    private static int EstimateMessagesTokens(List<Message> messages, ITokenEstimator tokenEstimator)
    {
        int total = 0;
        foreach (var message in messages)
        {
            total += 4; // 消息开销
            foreach (var block in message.Content)
            {
                if (block is TextBlock textBlock)
                    total += tokenEstimator.EstimateTokens(textBlock.Text);
                else if (block is ThinkingBlock thinkingBlock)
                    total += tokenEstimator.EstimateTokens(thinkingBlock.Thinking);
                else if (block is ImageBlock)
                    total += 2000;
                else if (block is ToolCallBlock toolCall)
                {
                    total += tokenEstimator.EstimateTokens(toolCall.Name);
                    total += tokenEstimator.EstimateTokens(toolCall.Arguments.GetRawText());
                }
                else if (block is ToolResultBlock toolResult)
                {
                    foreach (var content in toolResult.Content)
                    {
                        if (content is TextBlock text)
                            total += tokenEstimator.EstimateTokens(text.Text);
                        else if (content is ImageBlock)
                            total += 2000;
                    }
                }
            }
        }
        return total;
    }

    /// <summary>
    /// 截断工具结果
    /// </summary>
    private static string TruncateToolResult(Message toolResultMessage, string toolName, ToolTruncationStrategy strategy)
    {
        var originalContent = toolResultMessage.GetTextContent();
        var isError = toolResultMessage.Content.OfType<TextBlock>().Any(t => t.Text.Contains("error", StringComparison.OrdinalIgnoreCase));

        // 构建元数据头
        var metadata = $"[Tool: {toolName}] {(isError ? "Error" : "Success")}";

        // 应用工具特定的截断策略
        var truncated = strategy.Truncate(originalContent, toolName);

        return $"{metadata}\n{truncated}";
    }
}

/// <summary>
/// 工具截断策略基类
/// </summary>
public abstract class ToolTruncationStrategy
{
    /// <summary>
    /// 截断工具输出
    /// </summary>
    /// <param name="content">原始内容</param>
    /// <param name="toolName">工具名称</param>
    /// <returns>截断后的内容</returns>
    public abstract string Truncate(string content, string toolName);
}

/// <summary>
/// Bash/PowerShell 截断策略 - 保留最后 N 行
/// </summary>
public sealed class BashTruncationStrategy : ToolTruncationStrategy
{
    private const int KeepLastLines = 5;

    public override string Truncate(string content, string toolName)
    {
        if (string.IsNullOrEmpty(content))
            return "[empty output]";

        var lines = content.Split('\n');

        if (lines.Length <= KeepLastLines)
            return content;

        var lastLines = lines[^KeepLastLines..];
        return $"[output truncated, showing last {KeepLastLines} lines]\n{string.Join("\n", lastLines)}";
    }
}

/// <summary>
/// 文件读取截断策略 - 保留文件路径和行数信息
/// </summary>
public sealed class FileReadTruncationStrategy : ToolTruncationStrategy
{
    public override string Truncate(string content, string toolName)
    {
        if (string.IsNullOrEmpty(content))
            return "[empty file]";

        var lines = content.Split('\n');

        // 尝试从内容中提取文件路径
        var filePath = ExtractFilePath(content);

        return $"[file content truncated: {filePath ?? "unknown"}, {lines.Length} lines]";
    }

    private static string? ExtractFilePath(string content)
    {
        // 尝试匹配常见的文件路径模式
        var lines = content.Split('\n');
        if (lines.Length > 0)
        {
            var firstLine = lines[0].Trim();
            if (firstLine.Contains('/') || firstLine.Contains('\\'))
                return firstLine;
        }
        return null;
    }
}

/// <summary>
/// Grep 截断策略 - 保留匹配数量和搜索模式
/// </summary>
public sealed class GrepTruncationStrategy : ToolTruncationStrategy
{
    public override string Truncate(string content, string toolName)
    {
        if (string.IsNullOrEmpty(content))
            return "[no results]";

        var lines = content.Split('\n');
        var matchCount = lines.Count(l => !string.IsNullOrWhiteSpace(l));

        return $"[grep results truncated: {matchCount} matches]";
    }
}

/// <summary>
/// Glob 截断策略 - 保留文件数量
/// </summary>
public sealed class GlobTruncationStrategy : ToolTruncationStrategy
{
    public override string Truncate(string content, string toolName)
    {
        if (string.IsNullOrEmpty(content))
            return "[no files found]";

        var lines = content.Split('\n');
        var fileCount = lines.Count(l => !string.IsNullOrWhiteSpace(l));

        return $"[glob results truncated: {fileCount} files found]";
    }
}

/// <summary>
/// Web Search 截断策略 - 保留搜索结果数量
/// </summary>
public sealed class WebSearchTruncationStrategy : ToolTruncationStrategy
{
    public override string Truncate(string content, string toolName)
    {
        if (string.IsNullOrEmpty(content))
            return "[no results]";

        // 尝试解析 JSON 获取结果数量
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                return $"[search results truncated: {results.GetArrayLength()} results]";
            }
        }
        catch
        {
            // JSON 解析失败，使用简单计数
        }

        var lines = content.Split('\n');
        return $"[search results truncated: {lines.Length} lines]";
    }
}

/// <summary>
/// Web Fetch 截断策略 - 保留 URL 和内容长度
/// </summary>
public sealed class WebFetchTruncationStrategy : ToolTruncationStrategy
{
    public override string Truncate(string content, string toolName)
    {
        if (string.IsNullOrEmpty(content))
            return "[empty content]";

        return $"[web content truncated: {content.Length} chars]";
    }
}

/// <summary>
/// Edit File 截断策略 - 保留文件路径和操作状态
/// </summary>
public sealed class EditFileTruncationStrategy : ToolTruncationStrategy
{
    public override string Truncate(string content, string toolName)
    {
        // Edit 结果通常很短，保留原样
        if (string.IsNullOrEmpty(content))
            return "[edit completed]";

        return content.Length > 100 ? "[edit completed]" : content;
    }
}

/// <summary>
/// Write File 截断策略 - 保留文件路径和写入大小
/// </summary>
public sealed class WriteFileTruncationStrategy : ToolTruncationStrategy
{
    public override string Truncate(string content, string toolName)
    {
        if (string.IsNullOrEmpty(content))
            return "[file written]";

        return $"[file written: {content.Length} bytes]";
    }
}
