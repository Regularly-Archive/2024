using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Tests.Shared;

/// <summary>
/// 测试用静态工具类
/// </summary>
public static class TestStaticTools
{
    [Tool("save_memory", "保存信息到记忆中")]
    public static ToolResult SaveMemory(
        [ToolParameter("记忆的键")] string key,
        [ToolParameter("记忆的内容")] string content)
    {
        return ToolResult.FromText($"Memory saved: {key}");
    }

    [Tool("get_memory", "从记忆中获取信息")]
    public static ToolResult GetMemory(
        [ToolParameter("记忆的键")] string key)
    {
        return ToolResult.FromText($"Memory for '{key}': (not implemented)");
    }
}
