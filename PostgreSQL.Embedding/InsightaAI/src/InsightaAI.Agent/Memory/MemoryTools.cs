using System.Text;
using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Memory;

/// <summary>
/// 记忆工具注册器
/// </summary>
public static class MemoryTools
{
    /// <summary>
    /// 注册所有记忆工具到工具注册表
    /// </summary>
    public static void RegisterAll(ToolRegistry registry, IMemoryManager memoryManager, string userId)
    {
        registry.Register(new SaveMemoryTool(memoryManager, userId));
        registry.Register(new UpdateMemoryTool(memoryManager, userId));
        registry.Register(new DeleteMemoryTool(memoryManager, userId));
        registry.Register(new SearchMemoryTool(memoryManager, userId));
        registry.Register(new GetUserProfileTool(memoryManager, userId));
    }
}

/// <summary>
/// 保存记忆工具
/// </summary>
internal class SaveMemoryTool : IToolExecutor
{
    private readonly IMemoryManager _memoryManager;
    private readonly string _userId;

    public string Name => "save_memory";

    public ToolDefinition Definition { get; }

    public SaveMemoryTool(IMemoryManager memoryManager, string userId)
    {
        _memoryManager = memoryManager;
        _userId = userId;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = @"保存信息到长期记忆中。
记忆类型（type）：
- user：用户角色、偏好、知识背景（始终私有）
- feedback：用户对工作方式的指导，如纠正和确认（默认私有）
- project：项目进行中的工作、目标、决策（倾向团队共享）
- reference：外部系统资源指针、文档位置（通常团队共享）

注意：不要保存代码模式、git历史、调试方案、临时任务细节等可从代码推导的信息。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    content = new
                    {
                        type = "string",
                        description = "要保存的内容"
                    },
                    type = new
                    {
                        type = "string",
                        @enum = new[] { "user", "feedback", "project", "reference" },
                        description = "记忆类型。不指定则自动分类。"
                    },
                    tags = new
                    {
                        type = "string",
                        description = "标签列表，用逗号分隔。如：'csharp,docker,optimization'。不指定则自动提取。"
                    },
                    project = new
                    {
                        type = "string",
                        description = "关联的项目名称（用于团队记忆）。"
                    }
                },
                required = new[] { "content" }
            })
        };
    }

    public async Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        var content = args.TryGetValue("content", out var c) ? c?.ToString() : null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return ToolResult.FromError("Missing required parameter: content");
        }

        MemoryType? type = null;
        if (args.TryGetValue("type", out var t) && t?.ToString() is string typeStr)
        {
            if (Enum.TryParse<MemoryType>(typeStr, true, out var parsedType))
                type = parsedType;
        }

        List<string>? tags = null;
        if (args.TryGetValue("tags", out var tg) && tg?.ToString() is string tagsStr)
        {
            tags = tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
        }

        var project = args.TryGetValue("project", out var p) ? p?.ToString() : null;

        var entry = await _memoryManager.SaveMemoryAsync(
            _userId, content, type, tags, "user_input", project, context.CancellationToken);

        // 检查是否被过滤
        if (entry.Source == "filtered")
        {
            return ToolResult.FromText("Memory was filtered (matches exclusion rules). Nothing saved.");
        }

        return ToolResult.FromText($"Memory saved (id: {entry.Id}, type: {entry.Type}, scope: {entry.Scope}, tags: [{string.Join(", ", entry.Tags)}])");
    }
}

/// <summary>
/// 更新记忆工具
/// </summary>
internal class UpdateMemoryTool : IToolExecutor
{
    private readonly IMemoryManager _memoryManager;
    private readonly string _userId;

    public string Name => "update_memory";

    public ToolDefinition Definition { get; }

    public UpdateMemoryTool(IMemoryManager memoryManager, string userId)
    {
        _memoryManager = memoryManager;
        _userId = userId;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = @"更新已有的长期记忆。需要先通过 search_memory 获取记忆 ID。
可以更新记忆的内容、类型或标签。至少提供一个更新字段。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    memory_id = new
                    {
                        type = "string",
                        description = "要更新的记忆 ID（通过 search_memory 获取）"
                    },
                    content = new
                    {
                        type = "string",
                        description = "新的记忆内容（可选）"
                    },
                    type = new
                    {
                        type = "string",
                        @enum = new[] { "user", "feedback", "project", "reference" },
                        description = "新的记忆类型（可选）"
                    },
                    tags = new
                    {
                        type = "string",
                        description = "新的标签列表，用逗号分隔（可选）"
                    }
                },
                required = new[] { "memory_id" }
            })
        };
    }

    public async Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        var memoryId = args.TryGetValue("memory_id", out var id) ? id?.ToString() : null;
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            return ToolResult.FromError("Missing required parameter: memory_id");
        }

        var content = args.TryGetValue("content", out var c) ? c?.ToString() : null;

        MemoryType? type = null;
        if (args.TryGetValue("type", out var t) && t?.ToString() is string typeStr)
        {
            if (Enum.TryParse<MemoryType>(typeStr, true, out var parsedType))
                type = parsedType;
        }

        List<string>? tags = null;
        if (args.TryGetValue("tags", out var tg) && tg?.ToString() is string tagsStr)
        {
            tags = tagsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
        }

        // 至少需要一个更新字段
        if (string.IsNullOrWhiteSpace(content) && !type.HasValue && tags == null)
        {
            return ToolResult.FromError("At least one update field (content, type, or tags) is required.");
        }

        var success = await _memoryManager.UpdateMemoryAsync(
            _userId, memoryId, content, type, tags, context.CancellationToken);

        if (!success)
        {
            return ToolResult.FromError($"Memory '{memoryId}' not found or update failed.");
        }

        return ToolResult.FromText($"Memory '{memoryId}' updated successfully.");
    }
}

/// <summary>
/// 删除记忆工具
/// </summary>
internal class DeleteMemoryTool : IToolExecutor
{
    private readonly IMemoryManager _memoryManager;
    private readonly string _userId;

    public string Name => "delete_memory";

    public ToolDefinition Definition { get; }

    public DeleteMemoryTool(IMemoryManager memoryManager, string userId)
    {
        _memoryManager = memoryManager;
        _userId = userId;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "删除长期记忆。需要先通过 search_memory 获取记忆 ID。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    memory_id = new
                    {
                        type = "string",
                        description = "要删除的记忆 ID（通过 search_memory 获取）"
                    }
                },
                required = new[] { "memory_id" }
            })
        };
    }

    public async Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        var memoryId = args.TryGetValue("memory_id", out var id) ? id?.ToString() : null;
        if (string.IsNullOrWhiteSpace(memoryId))
        {
            return ToolResult.FromError("Missing required parameter: memory_id");
        }

        var success = await _memoryManager.DeleteMemoryAsync(
            _userId, memoryId, context.CancellationToken);

        if (!success)
        {
            return ToolResult.FromError($"Memory '{memoryId}' not found.");
        }

        return ToolResult.FromText($"Memory '{memoryId}' deleted successfully.");
    }
}

/// <summary>
/// 搜索记忆工具
/// </summary>
internal class SearchMemoryTool : IToolExecutor
{
    private readonly IMemoryManager _memoryManager;
    private readonly string _userId;

    public string Name => "search_memory";

    public ToolDefinition Definition { get; }

    public SearchMemoryTool(IMemoryManager memoryManager, string userId)
    {
        _memoryManager = memoryManager;
        _userId = userId;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "搜索长期记忆。使用自然语言查询查找相关的记忆信息，如用户偏好、项目配置、过去的决策等。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    query = new
                    {
                        type = "string",
                        description = "搜索查询（自然语言）"
                    },
                    type = new
                    {
                        type = "string",
                        @enum = new[] { "user", "feedback", "project", "reference" },
                        description = "限定搜索的记忆类型"
                    },
                    max_results = new
                    {
                        type = "integer",
                        description = "最大返回结果数。默认 5。"
                    },
                    project = new
                    {
                        type = "string",
                        description = "限定搜索的项目（搜索团队记忆）"
                    }
                },
                required = new[] { "query" }
            })
        };
    }

    public async Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        var query = args.TryGetValue("query", out var q) ? q?.ToString() : null;
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.FromError("Missing required parameter: query");
        }

        MemoryType? type = null;
        if (args.TryGetValue("type", out var t) && t?.ToString() is string typeStr)
        {
            if (Enum.TryParse<MemoryType>(typeStr, true, out var parsedType))
                type = parsedType;
        }

        var maxResults = 5;
        if (args.TryGetValue("max_results", out var m) && m?.ToString() is string maxStr)
        {
            if (int.TryParse(maxStr, out var parsedMax))
                maxResults = parsedMax;
        }

        var project = args.TryGetValue("project", out var p) ? p?.ToString() : null;

        var memories = await _memoryManager.SearchRelevantMemoriesAsync(
            _userId, query, maxResults, type, project, context.CancellationToken);

        if (memories.Count == 0)
        {
            return ToolResult.FromText("No matching memories found.");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Found {memories.Count} relevant memories:");
        sb.AppendLine();

        foreach (var memory in memories)
        {
            var score = memory.RelevanceScore.HasValue ? $" (score: {memory.RelevanceScore:F2})" : "";
            sb.AppendLine($"[{memory.Type}]{score} {memory.Name}");
            sb.AppendLine($"  {memory.Content}");
            if (memory.Tags.Count > 0)
                sb.AppendLine($"  Tags: {string.Join(", ", memory.Tags)}");
            sb.AppendLine();
        }

        return ToolResult.FromText(sb.ToString());
    }
}

/// <summary>
/// 获取用户画像工具
/// </summary>
internal class GetUserProfileTool : IToolExecutor
{
    private readonly IMemoryManager _memoryManager;
    private readonly string _userId;

    public string Name => "get_user_profile";

    public ToolDefinition Definition { get; }

    public GetUserProfileTool(IMemoryManager memoryManager, string userId)
    {
        _memoryManager = memoryManager;
        _userId = userId;

        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "获取用户偏好和项目上下文。返回用户的技术栈、沟通风格、项目信息等。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    project = new
                    {
                        type = "string",
                        description = "当前项目名称（可选，用于获取项目特定上下文）"
                    }
                }
            })
        };
    }

    public async Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        var project = args.TryGetValue("project", out var p) ? p?.ToString() : null;

        var userContext = await _memoryManager.GetUserContextAsync(_userId, project, context.CancellationToken);

        if (string.IsNullOrWhiteSpace(userContext))
        {
            return ToolResult.FromText("No user profile or memories found.");
        }

        return ToolResult.FromText(userContext);
    }
}
