using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Tests.Shared;

/// <summary>
/// 获取当前时间工具 (用于测试)
/// </summary>
public class GetCurrentTimeTool : ITool
{
    public string Name => "get_current_time";

    public ToolDefinition Definition => new()
    {
        Name = "get_current_time",
        Description = "Get the current date and time.",
        Schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""timezone"": {
                    ""type"": ""string"",
                    ""description"": ""Timezone (e.g., 'UTC', 'Asia/Shanghai'). Default is local timezone.""
                }
            }
        }")
    };

    public Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        var timezone = args.TryGetValue("timezone", out var tz) ? tz?.ToString() : null;

        DateTimeOffset now;
        if (!string.IsNullOrEmpty(timezone))
        {
            try
            {
                var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tzInfo);
            }
            catch
            {
                now = DateTimeOffset.UtcNow;
            }
        }
        else
        {
            now = DateTimeOffset.Now;
        }

        return Task.FromResult(ToolResult.FromText(now.ToString("yyyy-MM-dd HH:mm:ss zzz")));
    }
}

/// <summary>
/// 保存笔记工具 (用于测试)
/// </summary>
public class SaveNoteTool : ITool
{
    private readonly Dictionary<string, string> _notes;

    public SaveNoteTool(Dictionary<string, string>? notes = null)
    {
        _notes = notes ?? new Dictionary<string, string>();
    }

    public string Name => "save_note";

    public ToolDefinition Definition => new()
    {
        Name = "save_note",
        Description = "Save a note for later reference.",
        Schema = JsonSerializer.Deserialize<JsonElement>(@"{
            ""type"": ""object"",
            ""properties"": {
                ""key"": {
                    ""type"": ""string"",
                    ""description"": ""Note identifier""
                },
                ""content"": {
                    ""type"": ""string"",
                    ""description"": ""Note content""
                }
            },
            ""required"": [""key"", ""content""]
        }")
    };

    public Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        var key = args.TryGetValue("key", out var k) ? k?.ToString() ?? "" : "";
        var content = args.TryGetValue("content", out var c) ? c?.ToString() ?? "" : "";

        if (string.IsNullOrEmpty(key))
        {
            return Task.FromResult(ToolResult.FromError("Key is required."));
        }

        _notes[key] = content;
        return Task.FromResult(ToolResult.FromText($"Note '{key}' saved successfully."));
    }
}
