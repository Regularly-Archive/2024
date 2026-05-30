using System.Reflection;
using System.Text.Json;
using InsightaAI.LLM.Models;

namespace InsightaAI.LLM.Abstractions;

/// <summary>
/// 工具扫描器 - 从程序集扫描带 [Tool] 标记的方法
/// </summary>
public static class ToolScanner
{
    /// <summary>
    /// 扫描程序集中的所有工具
    /// </summary>
    public static IEnumerable<IToolExecutor> ScanAssembly(Assembly assembly)
    {
        var tools = new List<IToolExecutor>();

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            {
                var toolAttr = method.GetCustomAttribute<ToolAttribute>();
                if (toolAttr == null) continue;

                var executor = CreateToolExecutor(toolAttr, method, type);
                if (executor != null)
                {
                    tools.Add(executor);
                }
            }
        }

        return tools;
    }

    /// <summary>
    /// 扫描多个程序集
    /// </summary>
    public static IEnumerable<IToolExecutor> ScanAssemblies(params Assembly[] assemblies)
    {
        return assemblies.SelectMany(ScanAssembly);
    }

    /// <summary>
    /// 扫描当前应用程序域中的所有程序集
    /// </summary>
    public static IEnumerable<IToolExecutor> ScanAllLoadedAssemblies()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(ScanAssembly);
    }

    /// <summary>
    /// 从方法创建工具执行器（静态方法）
    /// </summary>
    public static IToolExecutor? CreateToolExecutorFromMethod(MethodInfo method)
    {
        var toolAttr = method.GetCustomAttribute<ToolAttribute>();
        if (toolAttr == null) return null;

        return CreateToolExecutor(toolAttr, method, method.DeclaringType!);
    }

    /// <summary>
    /// 从方法创建工具执行器（实例方法，需要提供实例）
    /// </summary>
    public static IToolExecutor? CreateToolExecutorFromMethod(MethodInfo method, object instance)
    {
        var toolAttr = method.GetCustomAttribute<ToolAttribute>();
        if (toolAttr == null) return null;

        var schema = BuildJsonSchema(method);
        var description = toolAttr.Description;

        // 创建委托，使用提供的实例
        Func<IDictionary<string, object>, ToolExecutionContext, Task<ToolResult>> handler =
            (args, context) => InvokeMethodAsync(method, instance, args, context);

        return new DelegateToolExecutor(toolAttr.Name, description, schema, handler);
    }

    private static IToolExecutor? CreateToolExecutor(ToolAttribute toolAttr, MethodInfo method, Type type)
    {
        // 构建 JSON Schema
        var schema = BuildJsonSchema(method);
        var description = toolAttr.Description;

        // 判断是静态方法还是实例方法
        var isStatic = method.IsStatic;

        // 创建委托
        Func<IDictionary<string, object>, ToolExecutionContext, Task<ToolResult>> handler;

        if (isStatic)
        {
            handler = (args, context) => InvokeMethodAsync(method, null, args, context);
        }
        else
        {
            // 实例方法需要创建实例
            handler = (args, context) =>
            {
                var instance = Activator.CreateInstance(type);
                return InvokeMethodAsync(method, instance, args, context);
            };
        }

        return new DelegateToolExecutor(toolAttr.Name, description, schema, handler);
    }

    private static JsonElement BuildJsonSchema(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var param in parameters)
        {
            // 跳过 ToolExecutionContext 参数，它会被自动注入
            if (param.ParameterType == typeof(ToolExecutionContext))
                continue;

            var paramAttr = param.GetCustomAttribute<ToolParameterAttribute>();
            var paramName = param.Name ?? "unknown";

            var paramSchema = new Dictionary<string, object>
            {
                ["type"] = GetJsonType(param.ParameterType),
                ["description"] = paramAttr?.Description ?? ""
            };

            // 如果有默认值，不是必填
            if (param.HasDefaultValue)
            {
                paramSchema["default"] = param.DefaultValue;
            }
            else if (paramAttr?.Required != false)
            {
                required.Add(paramName);
            }

            // 处理枚举类型
            if (param.ParameterType.IsEnum)
            {
                paramSchema["enum"] = Enum.GetNames(param.ParameterType);
            }

            properties[paramName] = paramSchema;
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return JsonSerializer.SerializeToElement(schema);
    }

    private static string GetJsonType(Type type)
    {
        // 处理 Nullable<T>
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType == typeof(string))
            return "string";
        if (underlyingType == typeof(int) || underlyingType == typeof(long) ||
            underlyingType == typeof(short) || underlyingType == typeof(byte))
            return "integer";
        if (underlyingType == typeof(float) || underlyingType == typeof(double) ||
            underlyingType == typeof(decimal))
            return "number";
        if (underlyingType == typeof(bool))
            return "boolean";
        if (underlyingType.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
            return "array";
        if (underlyingType.IsEnum)
            return "string";

        return "object";
    }

    private static async Task<ToolResult> InvokeMethodAsync(MethodInfo method, object? instance, IDictionary<string, object> args, ToolExecutionContext context)
    {
        try
        {
            var parameters = method.GetParameters();
            var invokeArgs = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                var paramName = param.Name!;

                // 自动注入 ToolExecutionContext
                if (param.ParameterType == typeof(ToolExecutionContext))
                {
                    invokeArgs[i] = context;
                }
                else if (args.TryGetValue(paramName, out var value))
                {
                    invokeArgs[i] = ConvertValue(value, param.ParameterType);
                }
                else if (param.HasDefaultValue)
                {
                    invokeArgs[i] = param.DefaultValue;
                }
                else
                {
                    invokeArgs[i] = GetDefault(param.ParameterType);
                }
            }

            var result = method.Invoke(instance, invokeArgs);

            // 处理返回值
            return await ConvertToToolResult(result);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not OperationCanceledException)
        {
            return ToolResult.FromError($"Tool execution failed: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (OperationCanceledException)
        {
            throw; // 重新抛出取消异常，让调用者处理
        }
        catch (Exception ex)
        {
            return ToolResult.FromError($"Tool execution failed: {ex.Message}");
        }
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
            return GetDefault(targetType);

        // 处理 Nullable<T>
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // 类型转换
        if (underlyingType == typeof(string))
            return value.ToString();
        if (underlyingType == typeof(int))
            return Convert.ToInt32(value);
        if (underlyingType == typeof(long))
            return Convert.ToInt64(value);
        if (underlyingType == typeof(float))
            return Convert.ToSingle(value);
        if (underlyingType == typeof(double))
            return Convert.ToDouble(value);
        if (underlyingType == typeof(bool))
            return Convert.ToBoolean(value);
        if (underlyingType.IsEnum)
        {
            var strValue = value.ToString()!;
            if (Enum.TryParse(underlyingType, strValue, true, out var enumValue))
                return enumValue!;

            throw new ArgumentException($"Cannot convert '{strValue}' to enum type {underlyingType.Name}");
        }

        return value;
    }

    /// <summary>
    /// 将方法返回值转换为 ToolResult
    /// 支持：ToolResult, Task<ToolResult>, Task<T>, Task, string, 其他类型
    /// </summary>
    private static async Task<ToolResult> ConvertToToolResult(object? result)
    {
        if (result == null)
            return ToolResult.FromText("Done");

        // ToolResult
        if (result is ToolResult toolResult)
            return toolResult;

        // Task<ToolResult>
        if (result is Task<ToolResult> taskToolResult)
            return await taskToolResult;

        // 检查是否是 Task<T> (但不是 Task 和 Task<ToolResult>)
        var resultType = result.GetType();
        if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            // 使用动态类型获取结果
            var task = (Task)result;
            await task;

            // 通过反射获取 Result 属性
            var resultProperty = resultType.GetProperty("Result");
            if (resultProperty != null)
            {
                var taskResult = resultProperty.GetValue(result);
                return ConvertObjectToToolResult(taskResult);
            }
        }

        // Task (无返回值)
        if (result is Task taskNoResult)
        {
            await taskNoResult;
            return ToolResult.FromText("Done");
        }

        // 非 Task 类型
        return ConvertObjectToToolResult(result);
    }

    /// <summary>
    /// 将普通对象转换为 ToolResult
    /// </summary>
    private static ToolResult ConvertObjectToToolResult(object? value)
    {
        if (value == null)
            return ToolResult.FromText("Done");

        if (value is ToolResult toolResult)
            return toolResult;

        if (value is string strResult)
            return ToolResult.FromText(strResult);

        return ToolResult.FromText(value.ToString() ?? "");
    }

    private static object? GetDefault(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
