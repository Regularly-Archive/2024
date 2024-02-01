using Microsoft.Extensions.DependencyInjection;
using MongoRepository.Abstration;
using MongoRepository.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using MongoRepository.Configuration;
using MongoDB.Driver;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace MongoRepository.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMongoRepository(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection(nameof(MongoDbConfiguration));
            if (!section.Exists()) return services;

            services.Configure<MongoDbConfiguration>(section);
            services.AddSingleton(sp => sp.GetRequiredService<IOptions<MongoDbConfiguration>>().Value);
            services.AddSingleton<IMongoDbConnection, MongoDbConnection>();
            services.AddScoped<IMongoDbContext, MongoDbContext>();
            services.AddScoped<IMongoDbUnitOfWork, MongoDbUnitOfWork>();
            services.AutoScanEntities();
            return services;
        }

        private static IServiceCollection AutoScanEntities(this IServiceCollection services)
        {
            var assembly = Assembly.GetEntryAssembly();
            var entityTypes = assembly.GetTypes().Where(x => x.IsSubclassOf(typeof(BaseEntity)));
            foreach (var entityType in entityTypes)
            {
                var interfaceType = typeof(IMongoDbRepository<>).MakeGenericType(entityType);
                var implementType = typeof(MongoDbRepository<>).MakeGenericType(entityType);
                services.AddScoped(interfaceType, implementType);
            }

            return services;
        }
    }
}
