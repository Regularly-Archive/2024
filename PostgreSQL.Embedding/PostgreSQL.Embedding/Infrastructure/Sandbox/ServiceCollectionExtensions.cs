using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PostgreSQL.Embedding.Infrastructure.Sandbox;

/// <summary>
/// Sandbox 服务集合扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Docker Sandbox 服务
    /// </summary>
    public static IServiceCollection AddDockerSandbox(this IServiceCollection services, IConfiguration configuration)
    {
        // 注册配置
        services.Configure<SandboxOptions>(options =>
        {
            var section = configuration.GetSection("SandboxConfig");
            if (section.Exists())
            {
                section.Bind(options);
            }
        });

        // 注册服务
        services.AddSingleton<DockerContainerManager>();
        services.AddSingleton<SandboxService>();

        // 注册后台清理服务
        services.AddHostedService<SandboxCleanupService>();

        return services;
    }

    /// <summary>
    /// 添加 Docker Sandbox 服务 (使用自定义配置)
    /// </summary>
    public static IServiceCollection AddDockerSandbox(this IServiceCollection services, Action<SandboxOptions>? configureOptions = null)
    {
        // 注册配置
        services.Configure(configureOptions ?? (_ => { }));

        // 注册服务
        services.AddSingleton<DockerContainerManager>();
        services.AddSingleton<SandboxService>();

        // 注册后台清理服务
        services.AddHostedService<SandboxCleanupService>();

        return services;
    }
}
