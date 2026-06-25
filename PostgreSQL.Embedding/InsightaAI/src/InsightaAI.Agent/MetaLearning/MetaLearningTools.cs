using System.Text.Json;
using InsightaAI.Agent.Abstractions;

namespace InsightaAI.Agent.MetaLearning;

/// <summary>
/// 元学习工具注册
/// </summary>
public static class MetaLearningTools
{
    /// <summary>
    /// 注册 learn_lesson 和 read_lessons 工具到 ToolRegistry
    /// </summary>
    public static void RegisterAll(ToolRegistry registry, MetaLearningStore store)
    {
        RegisterLearnLesson(registry, store);
        RegisterReadLessons(registry, store);
    }

    private static void RegisterLearnLesson(ToolRegistry registry, MetaLearningStore store)
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""category"": {
                    ""type"": ""string"",
                    ""description"": ""教训分类: tools(工具调用), environment(环境), workflows(工作流)"",
                    ""enum"": [""tools"", ""environment"", ""workflows""]
                },
                ""lesson"": {
                    ""type"": ""string"",
                    ""description"": ""一条简洁可操作的教训，如: 不要用 curl，用 Invoke-WebRequest""
                }
            },
            ""required"": [""category"", ""lesson""]
        }");

        registry.RegisterFunction(
            "learn_lesson",
            "记录一条工具使用教训。当工具调用失败、被纠正、或发现更好的做法时调用此工具。" +
            "教训应该简洁、具体、可操作。",
            schema,
            async (args, ctx) =>
            {
                var category = args["category"]?.ToString() ?? "tools";
                var lesson = args["lesson"]?.ToString();

                if (string.IsNullOrWhiteSpace(lesson))
                {
                    return ToolResult.FromError("lesson is required");
                }

                // 原子操作：检查 + 写入（避免竞态）
                await store.AppendLessonIfNotExistsAsync(category, lesson, ctx.CancellationToken);
                return ToolResult.FromText($"Lesson recorded in {category}.md: {lesson}");
            });
    }

    private static void RegisterReadLessons(ToolRegistry registry, MetaLearningStore store)
    {
        var schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""file"": {
                    ""type"": ""string"",
                    ""description"": ""要读取的教训文件: tools, environment, workflows, 或 all(索引摘要)"",
                    ""enum"": [""tools"", ""environment"", ""workflows"", ""all""]
                }
            },
            ""required"": [""file""]
        }");

        registry.RegisterFunction(
            "read_lessons",
            "读取已积累的教训。在开始新任务前读取相关教训可以避免重复犯错。",
            schema,
            async (args, ctx) =>
            {
                var file = args["file"]?.ToString() ?? "all";

                if (file == "all")
                {
                    var index = await store.ReadIndexAsync(ctx.CancellationToken);
                    return ToolResult.FromText(string.IsNullOrWhiteSpace(index)
                        ? "No lessons learned yet."
                        : index);
                }

                var content = await store.ReadLessonsAsync(file, ctx.CancellationToken);
                return ToolResult.FromText(string.IsNullOrWhiteSpace(content)
                    ? $"No lessons in {file}.md yet."
                    : content);
            });
    }
}
