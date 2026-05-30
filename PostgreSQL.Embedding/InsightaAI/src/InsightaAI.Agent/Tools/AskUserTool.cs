using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools;

/// <summary>
/// 向用户提问工具 - 用于 Agent 需要澄清或获取信息时
/// </summary>
public class AskUserTool : IToolExecutor
{
    private readonly Func<string, Task<string>> _askHandler;

    public string Name => "ask_user";

    public ToolDefinition Definition { get; }

    /// <summary>
    /// 创建 AskUserTool
    /// </summary>
    /// <param name="askHandler">提问处理函数，接收问题，返回用户回答</param>
    public AskUserTool(Func<string, Task<string>> askHandler)
    {
        _askHandler = askHandler;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "向用户提问以获取澄清或额外信息。当你需要更多信息来完成任务时使用。",
            Schema = System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    question = new
                    {
                        type = "string",
                        description = "要问用户的问题"
                    }
                },
                required = new[] { "question" }
            })
        };
    }

    public async Task<ToolResult> ExecuteAsync(
        IDictionary<string, object> args,
        ToolExecutionContext context)
    {
        if (!args.TryGetValue("question", out var questionObj) || questionObj is not string question)
        {
            return ToolResult.FromError("Missing required parameter: question");
        }

        try
        {
            var answer = await _askHandler(question);
            return ToolResult.FromText(answer);
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Failed to get user answer: {ex.Message}");
        }
    }
}

/// <summary>
/// 使用 [Tool] Attribute 的静态工具方法示例
/// </summary>
public static class StaticToolExamples
{
    [Tool("ask_user_static", "向用户提问（静态版本）")]
    public static async Task<ToolResult> AskUser(
        [ToolParameter("要问用户的问题")] string question)
    {
        // 这里需要一个全局的提问处理器
        // 实际使用时应该通过 DI 或其他方式注入
        Console.WriteLine($"\n[Agent 问] {question}");
        Console.Write("[你的回答] ");
        var answer = Console.ReadLine() ?? "";
        return ToolResult.FromText(answer);
    }

    [Tool("save_memory", "保存信息到记忆中")]
    public static ToolResult SaveMemory(
        [ToolParameter("记忆的键")] string key,
        [ToolParameter("记忆的内容")] string content)
    {
        // 这里可以实现持久化存储
        Console.WriteLine($"[Memory] Saved '{key}': {content}");
        return ToolResult.FromText($"Memory saved: {key}");
    }

    [Tool("get_memory", "从记忆中获取信息")]
    public static ToolResult GetMemory(
        [ToolParameter("记忆的键")] string key)
    {
        // 这里可以实现从存储中读取
        return ToolResult.FromText($"Memory for '{key}': (not implemented)");
    }

    /// <summary>
    /// 示例：使用 ToolExecutionContext 自动注入
    /// ToolExecutionContext 参数不需要 [ToolParameter] 标记，会自动注入
    /// </summary>
    [Tool("log_with_context", "记录日志（带上下文信息）")]
    public static ToolResult LogWithContext(
        [ToolParameter("日志消息")] string message,
        ToolExecutionContext context)  // 自动注入，不需要 [ToolParameter]
    {
        var log = $"[Agent:{context.AgentId}][ToolCall:{context.ToolCallId}] {message}";
        Console.WriteLine(log);
        return ToolResult.FromText($"Logged: {message}");
    }
}
