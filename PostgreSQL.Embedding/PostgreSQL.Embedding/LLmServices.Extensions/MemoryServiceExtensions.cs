using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Runtime.CompilerServices;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class MemoryServiceExtensions
    {
        public static IKnowledgeBaseService AsKnowledgeBaseService(this IMemoryService memoryService, IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var importRecordRepository = serviceProvider.GetRequiredService<IRepository<DocumentImportRecord>>();
            var knowledgeBaseRepository = serviceProvider.GetRequiredService<IRepository<KnowledgeBase>>();
            var tablePrefixMappingRepository = serviceProvider.GetRequiredService<IRepository<TablePrefixMapping>>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            return new KnowledgeBaseService(
                serviceProvider,
                configuration,
                memoryService,
                importRecordRepository,
                knowledgeBaseRepository,
                tablePrefixMappingRepository,
                loggerFactory.CreateLogger<KnowledgeBaseService>()
           );
        }

        public static IFullTextSearchService AsFullTextSearchService(this IMemoryService memoryService, IServiceProvider serviceProvider)
        {
            return serviceProvider.GetService<IFullTextSearchService>();
        }
    }
}
