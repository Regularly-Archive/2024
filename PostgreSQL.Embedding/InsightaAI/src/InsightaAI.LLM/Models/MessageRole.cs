namespace InsightaAI.LLM.Models;

/// <summary>
/// 消息角色
/// </summary>
public enum MessageRole
{
    /// <summary>系统提示词</summary>
    System,

    /// <summary>用户消息</summary>
    User,

    /// <summary>助手回复</summary>
    Assistant,

    /// <summary>工具执行结果</summary>
    ToolResult
}
