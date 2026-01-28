using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using PostgreSQL.Embedding.Infrastructure.FileStorage;
using System;

namespace PostgreSQL.Embedding.Infrastructure.FileStorage
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加文件存储服务
        /// </summary>
        public static IServiceCollection AddFileStorage(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // 配置 MinIO 客户端
            services.AddMinio(minioClient =>
            {
                var minioConfig = configuration.GetSection("MinioConfig");
                var endpoint = minioConfig["Url"]
                    ?? throw new InvalidOperationException("MinioConfig:Url is not configured.");
                var accessKey = minioConfig["AccessKey"]
                    ?? throw new InvalidOperationException("MinioConfig:AccessKey is not configured.");
                var secretKey = minioConfig["SecretKey"]
                    ?? throw new InvalidOperationException("MinioConfig:SecretKey is not configured.");

                minioClient
                    .WithEndpoint(new Uri(endpoint))
                    .WithCredentials(accessKey, secretKey)
                    .WithSSL(false);
            });

            // 注册文件存储服务
            services.AddScoped<IFileStorageService, MinioFileStorageService>();

            return services;
        }
    }
}
