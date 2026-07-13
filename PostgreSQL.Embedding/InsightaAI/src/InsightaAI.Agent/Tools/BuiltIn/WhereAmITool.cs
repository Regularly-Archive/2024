using System.Runtime.InteropServices;
using System.Text.Json;
using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Models;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;

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
            Description = "获取当前环境信息，包括时间、操作系统、工作目录、会话 ID、模型信息等。当你需要了解当前上下文时使用。",
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

        var agentConfig = context.Services!.GetRequiredService<AgentConfig>();

        var info = new Dictionary<string, object>
        {
            ["time"] = new
            {
                local = now.ToString("yyyy-MM-dd HH:mm:ss dddd"),
                utc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss dddd"),
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
            ["session"] = context.SessionId ?? string.Empty,
            ["agent"] = context.AgentId,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["modelId"] = agentConfig!.Model
        };

        return Task.FromResult(ToolResult.From(info));
    }
}
