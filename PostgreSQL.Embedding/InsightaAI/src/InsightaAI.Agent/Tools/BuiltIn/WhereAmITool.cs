using System.Runtime.InteropServices;
using System.Text.Json;
using InsightaAI.LLM.Abstractions;
using InsightaAI.LLM.Models;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// whereami 工具 - 告知 Agent 当前环境上下文信息
/// </summary>
public class WhereAmITool : IToolExecutor
{
    public string Name => "whereami";

    public ToolDefinition Definition { get; }

    public WhereAmITool()
    {
        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "获取当前环境信息，包括时间、操作系统、工作目录、会话 ID 等。当你需要了解当前上下文时使用。",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { },
                required = Array.Empty<string>()
            })
        };
    }

    public Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        var now = DateTime.Now;
        var tz = TimeZoneInfo.Local;

        var info = new Dictionary<string, object>
        {
            ["time"] = new
            {
                local = now.ToString("yyyy-MM-dd HH:mm:ss"),
                utc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                timezone = tz.Id,
                timezone_display = tz.DisplayName
            },
            ["os"] = new
            {
                description = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.OSArchitecture.ToString(),
                platform = Environment.OSVersion.Platform.ToString()
            },
            ["workspace"] = Directory.GetCurrentDirectory(),
            ["session"] = context.ConversationId,
            ["agent"] = context.AgentId,
            ["runtime"] = RuntimeInformation.FrameworkDescription
        };

        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        return Task.FromResult(ToolResult.FromText(json));
    }
}
