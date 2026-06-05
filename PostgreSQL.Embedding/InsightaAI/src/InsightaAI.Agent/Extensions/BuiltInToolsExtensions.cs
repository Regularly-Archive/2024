using InsightaAI.Agent.Models;
using InsightaAI.Agent.Tools.BuiltIn;
using InsightaAI.LLM.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace InsightaAI.Agent.Extensions;

/// <summary>
/// 内置工具扩展方法
/// </summary>
public static class BuiltInToolsExtensions
{
    /// <summary>
    /// 注册默认的 Shell 执行器和文件系统实现
    /// </summary>
    public static IServiceCollection AddBuiltInToolServices(this IServiceCollection services)
    {
        // 注册默认实现（如果尚未注册）
        services.TryAddSingleton<IShellExecutor, LocalShellExecutor>();
        services.TryAddSingleton<IFileSystem, LocalFileSystem>();

        return services;
    }

    /// <summary>
    /// 向 ToolRegistry 注册所有内置工具
    /// </summary>
    /// <param name="registry">工具注册表</param>
    /// <param name="shellExecutor">Shell 执行器（可选，不提供则使用本地实现）</param>
    /// <param name="fileSystem">文件系统（可选，不提供则使用本地实现）</param>
    /// <returns></returns>
    public static ToolRegistry AddBuiltInTools(
        this ToolRegistry registry,
        IShellExecutor? shellExecutor = null,
        IFileSystem? fileSystem = null)
    {
        // 使用提供的实现或创建默认实现
        shellExecutor ??= new LocalShellExecutor();
        fileSystem ??= new LocalFileSystem();

        // 创建共享的文件读取状态
        var readState = new FileReadState();

        // 注册接口模式的工具
        registry.Register(new FileReadTool(fileSystem, readState));
        registry.Register(new FileWriteTool(fileSystem));
        registry.Register(new FileEditTool(fileSystem, readState));
        registry.Register(new GrepTool(fileSystem));
        registry.Register(new GlobTool(fileSystem));
        registry.Register(new BashTool(shellExecutor));
        registry.Register(new WhereAmITool());

        // 注册 Attribute 模式的工具（扫描当前程序集）
        registry.FromAssembly(typeof(WebSearchTool).Assembly);

        return registry;
    }

    /// <summary>
    /// 向 ToolRegistry 注册内置工具（使用 DI 容器中的服务）
    /// </summary>
    public static ToolRegistry AddBuiltInTools(
        this ToolRegistry registry,
        IServiceProvider serviceProvider)
    {
        var shellExecutor = serviceProvider.GetService<IShellExecutor>() ?? new LocalShellExecutor();
        var fileSystem = serviceProvider.GetService<IFileSystem>() ?? new LocalFileSystem();

        return registry.AddBuiltInTools(shellExecutor, fileSystem);
    }
}

/// <summary>
/// ServiceCollection 扩展方法（用于 TryAdd 模式）
/// </summary>
internal static class ServiceCollectionExtensions
{
    public static void TryAddSingleton<TService, TImplementation>(
        this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        if (!services.Any(d => d.ServiceType == typeof(TService)))
        {
            services.AddSingleton<TService, TImplementation>();
        }
    }
}
