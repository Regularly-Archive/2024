using System.Reflection;

namespace InsightaAI.Agent.Abstractions;

/// <summary>
/// ToolRegistry 扩展方法
/// </summary>
public static class ToolRegistryExtensions
{
    /// <summary>
    /// 从指定程序集扫描并注册工具
    /// </summary>
    public static ToolRegistry FromAssembly(this ToolRegistry registry, Assembly assembly)
    {
        var tools = ToolScanner.ScanAssembly(assembly);
        return registry.RegisterAll(tools);
    }

    /// <summary>
    /// 从多个程序集扫描并注册工具
    /// </summary>
    public static ToolRegistry FromAssemblies(this ToolRegistry registry, params Assembly[] assemblies)
    {
        var tools = ToolScanner.ScanAssemblies(assemblies);
        return registry.RegisterAll(tools);
    }

    /// <summary>
    /// 从当前应用程序域扫描并注册工具
    /// </summary>
    public static ToolRegistry ScanAllLoadedAssemblies(this ToolRegistry registry)
    {
        var tools = ToolScanner.ScanAllLoadedAssemblies();
        return registry.RegisterAll(tools);
    }

    /// <summary>
    /// 从包含指定类型的程序集扫描并注册工具
    /// </summary>
    public static ToolRegistry ScanAssemblyContaining<T>(this ToolRegistry registry)
    {
        return registry.FromAssembly(typeof(T).Assembly);
    }

    /// <summary>
    /// 从包含指定类型的程序集扫描并注册工具
    /// </summary>
    public static ToolRegistry ScanAssemblyContaining(this ToolRegistry registry, Type type)
    {
        return registry.FromAssembly(type.Assembly);
    }

    /// <summary>
    /// 注册带 [Tool] 标记的静态方法作为工具
    /// </summary>
    public static ToolRegistry RegisterMethod(this ToolRegistry registry, MethodInfo method)
    {
        var toolAttr = method.GetCustomAttribute<ToolAttribute>();
        if (toolAttr == null)
        {
            throw new InvalidOperationException($"Method '{method.Name}' does not have [Tool] attribute.");
        }

        var executor = ToolScanner.CreateToolExecutorFromMethod(method)
            ?? throw new InvalidOperationException($"Failed to create tool executor for method '{method.Name}'.");

        registry.Register(executor);
        return registry;
    }

    /// <summary>
    /// 注册带 [Tool] 标记的实例方法作为工具（需要提供实例）
    /// </summary>
    public static ToolRegistry RegisterMethod(this ToolRegistry registry, MethodInfo method, object instance)
    {
        var toolAttr = method.GetCustomAttribute<ToolAttribute>();
        if (toolAttr == null)
        {
            throw new InvalidOperationException($"Method '{method.Name}' does not have [Tool] attribute.");
        }

        var executor = ToolScanner.CreateToolExecutorFromMethod(method, instance)
            ?? throw new InvalidOperationException($"Failed to create tool executor for method '{method.Name}'.");

        registry.Register(executor);
        return registry;
    }
}
