using Microsoft.Extensions.DependencyInjection;
using PostgreSQL.Embedding.Infrastructure.Messaging;

namespace PostgreSQL.Embedding.Infrastructure.Messaging
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加消息服务
        /// </summary>
        public static IServiceCollection AddMessaging(this IServiceCollection services)
        {
            services.AddSingleton<INotificationService, NotificationService>();

            return services;
        }
    }
}
