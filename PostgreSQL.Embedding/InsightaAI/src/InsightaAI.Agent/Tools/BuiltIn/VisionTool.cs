using InsightaAI.Agent.Abstractions;
using InsightaAI.Agent.Vision;
using InsightaAI.LLM.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace InsightaAI.Agent.Tools.BuiltIn;

/// <summary>
/// read_image 工具 - 分析图片并返回文字描述。
/// 薄壳：参数解析 + 错误转换，多模态逻辑委托给 <see cref="IVisionService"/>。
/// </summary>
public class VisionTool : ITool
{
    public string Name => "read_image";

    public ToolDefinition Definition { get; }

    public VisionTool()
    {
        Definition = new ToolDefinition
        {
            Name = Name,
            Description = "Analyze an image with the configured vision model and return a text description. " +
                "Accepts a local file path or an http(s) URL. Use when you need to see the content of an image " +
                "(photos, screenshots, diagrams, charts). You may ask a specific question about the image via 'prompt'. " +
                "Requires a vision model to be configured.",
            Schema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    source = new
                    {
                        type = "string",
                        description = "Image location: local file path or http(s) URL."
                    },
                    prompt = new
                    {
                        type = "string",
                        description = "Optional instruction for the vision model, e.g. a specific question about the image. " +
                            "Defaults to a detailed description."
                    }
                },
                required = new[] { "source" }
            })
        };
    }

    public async Task<ToolResult> ExecuteAsync(IDictionary<string, object> args, ToolExecutionContext context)
    {
        var source = args.TryGetValue("source", out var s) ? s?.ToString() : null;
        if (string.IsNullOrWhiteSpace(source))
            return ToolResult.FromError("Parameter 'source' is required: a local file path or an http(s) URL.");

        var prompt = args.TryGetValue("prompt", out var p) && !string.IsNullOrWhiteSpace(p?.ToString())
            ? p!.ToString()!
            : "Describe this image in as much detail as possible.";

        var vision = context.Services?.GetService<IVisionService>();
        if (vision is null)
            return ToolResult.FromError("Vision model is not configured. Configure a vision model " +
                "(e.g. via 'config vision-model') before using this tool.");

        try
        {
            var description = await vision.AnalyzeAsync(source, prompt, context.CancellationToken);
            if (string.IsNullOrWhiteSpace(description))
                return ToolResult.FromError("Vision model returned an empty description.");

            return ToolResult.FromText(description);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Failed to analyze image '{source}': {ex.Message}");
        }
    }
}
