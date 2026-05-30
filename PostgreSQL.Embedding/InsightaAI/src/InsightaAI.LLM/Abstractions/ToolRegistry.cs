using System.Collections.Concurrent;
using InsightaAI.LLM.Models;

namespace InsightaAI.LLM.Abstractions;

/// <summary>
/// 工具注册表 - 管理和执行工具
/// </summary>
public class ToolRegistry
{
    private readonly ConcurrentDictionary<string, IToolExecutor> _executors = new();

    /// <summary>
    /// 注册工具执行器
    /// </summary>
    public ToolRegistry Register(IToolExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executors[executor.Name] = executor;
        return this;
    }

    /// <summary>
    /// 批量注册工具执行器
    /// </summary>
    public ToolRegistry RegisterAll(IEnumerable<IToolExecutor> executors)
    {
        foreach (var executor in executors)
        {
            Register(executor);
        }
        return this;
    }

    /// <summary>
    /// 注册委托工具
    /// </summary>
    public ToolRegistry RegisterFunction(
        string name,
        string description,
        System.Text.Json.JsonElement schema,
        Func<IDictionary<string, object>, ToolExecutionContext, Task<ToolResult>> handler)
    {
        var executor = new DelegateToolExecutor(name, description, schema, handler);
        return Register(executor);
    }

    /// <summary>
    /// 获取所有工具定义
    /// </summary>
    public ToolDefinition[] GetDefinitions()
    {
        return _executors.Values
            .Select(e => e.Definition)
            .ToArray();
    }

    /// <summary>
    /// 执行工具调用
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(
        ToolCallBlock toolCall,
        ToolExecutionContext context)
    {
        if (!_executors.TryGetValue(toolCall.Name, out var executor))
        {
            return ToolResult.FromError($"Tool '{toolCall.Name}' not found.");
        }

        try
        {
            // 解析参数
            var args = ParseArguments(toolCall.Arguments);
            return await executor.ExecuteAsync(args, context);
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Tool execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查工具是否已注册
    /// </summary>
    public bool HasTool(string name) => _executors.ContainsKey(name);

    /// <summary>
    /// 获取已注册的工具名称
    /// </summary>
    public IEnumerable<string> GetRegisteredToolNames() => _executors.Keys;

    private static IDictionary<string, object> ParseArguments(System.Text.Json.JsonElement arguments)
    {
        var result = new Dictionary<string, object>();

        // 处理 null 或 undefined
        if (arguments.ValueKind is System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined)
        {
            return result;
        }

        // 必须是对象类型
        if (arguments.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"Expected arguments to be a JSON object, but got {arguments.ValueKind}");
        }

        foreach (var property in arguments.EnumerateObject())
        {
            result[property.Name] = ConvertJsonElement(property.Value);
        }

        return result;
    }

    private static object ConvertJsonElement(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString()!,
            System.Text.Json.JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null!,
            _ => element.GetRawText()
        };
    }
}

/// <summary>
/// 委托工具执行器
/// </summary>
internal class DelegateToolExecutor : IToolExecutor
{
    private readonly Func<IDictionary<string, object>, ToolExecutionContext, Task<ToolResult>> _handler;

    public string Name { get; }
    public ToolDefinition Definition { get; }

    public DelegateToolExecutor(
        string name,
        string description,
        System.Text.Json.JsonElement schema,
        Func<IDictionary<string, object>, ToolExecutionContext, Task<ToolResult>> handler)
    {
        Name = name;
        Definition = new ToolDefinition
        {
            Name = name,
            Description = description,
            Schema = schema
        };
        _handler = handler;
    }

    public Task<ToolResult> ExecuteAsync(
        IDictionary<string, object> args,
        ToolExecutionContext context)
    {
        return _handler(args, context);
    }
}
