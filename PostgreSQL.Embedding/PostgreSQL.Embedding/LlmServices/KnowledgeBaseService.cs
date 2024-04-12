using DocumentFormat.OpenXml.Spreadsheet;
using Irony.Parsing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.KernelMemory;
using Microsoft.KernelMemory.Handlers;
using Microsoft.KernelMemory.Pipeline;
using Npgsql;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using SqlSugar;
using System.Text;
using Constants = PostgreSQL.Embedding.Common.Constants;

namespace PostgreSQL.Embedding.LlmServices
{
    public class KnowledgeBaseService : IKnowledgeBaseService
    {
        private readonly string _postgrelConnectionString;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly IMemoryService _memoryService;
        private readonly IRepository<DocumentImportRecord> _importRecordRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly IRepository<TablePrefixMapping> _tablePrefixMappingRepository;
        private readonly ILogger<KnowledgeBaseService> _logger;

        public KnowledgeBaseService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            IMemoryService memoryService,
            IRepository<DocumentImportRecord> importRecordRepository,
            IRepository<KnowledgeBase> knowledgeBaseRepository,
            IRepository<TablePrefixMapping> tablePrefixMappingRepository,
            ILogger<KnowledgeBaseService> logger
            )
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _postgrelConnectionString = configuration["ConnectionStrings:Default"]!;
            _memoryService = memoryService;
            _importRecordRepository = importRecordRepository;
            _knowledgeBaseRepository = knowledgeBaseRepository;
            _tablePrefixMappingRepository = tablePrefixMappingRepository;
            _logger = logger;
        }

        public Task<KnowledgeBase> CreateKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            if (!knowledgeBase.MaxTokensPerParagraph.HasValue)
                knowledgeBase.MaxTokensPerParagraph = DefaultTextPartitioningOptions.MaxTokensPerParagraph;

            if (!knowledgeBase.MaxTokensPerLine.HasValue)
                knowledgeBase.MaxTokensPerLine = DefaultTextPartitioningOptions.MaxTokensPerLine;

            if (!knowledgeBase.OverlappingTokens.HasValue)
                knowledgeBase.OverlappingTokens = DefaultTextPartitioningOptions.OverlappingTokens;

            return _knowledgeBaseRepository.AddAsync(knowledgeBase);
        }

        public Task UpdateKnowledgeBase(KnowledgeBase knowledgeBase)
        {
            return _knowledgeBaseRepository.UpdateAsync(knowledgeBase);
        }

        /// <summary>
        /// 查询知识库分块详情
        /// </summary>
        /// <param name="knowledgeBaseId">知识库ID</param>
        /// <param name="fileName">文件名</param>
        /// <returns></returns>
        public async Task<PageResult<KMPartition>> GetKnowledgeBaseChunks(long knowledgeBaseId, string fileName = null, int pageIndex = 1, int pageSize = 10)
        {
            // 组装 Kernel Memory 表名
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            var tablePrefixMapping = await _tablePrefixMappingRepository.SingleOrDefaultAsync(x => x.FullName == knowledgeBase.EmbeddingModel);
            var tableName = $"sk-{tablePrefixMapping.ShortName.ToLower()}-default";

            using var connection = new NpgsqlConnection(_postgrelConnectionString);
            await connection.OpenAsync();

            var totalCount = GetKnowledgeBaseChunksCount(connection, tableName, knowledgeBaseId, fileName);
            var partitions = GetKnowledgeBaseChunksPageList(connection, tableName, pageIndex, pageSize, knowledgeBaseId, fileName);

            var pageResult = new PageResult<KMPartition>() { TotalCount = (int)totalCount, Rows = partitions };
            return pageResult;
        }

        private long GetKnowledgeBaseChunksCount(NpgsqlConnection connection, string tableName, long knowledgeBaseId, string fileName = null)
        {
            var sqlText = $"""SELECT COUNT(*) FROM "{tableName}" t WHERE t.tags @> ARRAY['{KernelMemoryTags.KnowledgeBaseId}:{knowledgeBaseId}']""";
            if (!string.IsNullOrEmpty(fileName))
                sqlText += $""" AND t.tags @> ARRAY['{KernelMemoryTags.FileName}:{fileName}'] """;

            using var command = new NpgsqlCommand(sqlText, connection);
            var count = (long)command.ExecuteScalar();
            return count;
        }

        private List<KMPartition> GetKnowledgeBaseChunksPageList(NpgsqlConnection connection, string tableName, int pageIndex, int pageSize, long knowledgeBaseId, string fileName = null)
        {
            // 拼接 SQL 语句，按标签进行过滤
            var sqlTextPagination = $"""SELECT t.* FROM "{tableName}" t WHERE t.tags @> ARRAY['{KernelMemoryTags.KnowledgeBaseId}:{knowledgeBaseId}']""";
            if (!string.IsNullOrEmpty(fileName))
                sqlTextPagination += $""" AND t.tags @> ARRAY['{KernelMemoryTags.FileName}:{fileName}'] """;
            sqlTextPagination += $" LIMIT {pageSize} OFFSET {(pageIndex - 1) * pageSize}";

            using var queryCommand = new NpgsqlCommand(sqlTextPagination, connection);
            using var reader = queryCommand.ExecuteReader();

            var partitions = new List<KMPartition>();
            while (reader.Read())
            {
                var partition = ParseAsKMPartition(reader);
                partitions.Add(partition);
            }

            return partitions;
        }

        /// <summary>
        /// 从文档构建知识库
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="knowledgeBaseId">知识库ID</param>
        /// <param name="files">文件列表</param>
        /// <returns></returns>
        public async Task ImportKnowledgeFromFiles(string taskId, long knowledgeBaseId, IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);

                // 如果文件重复则直接忽略
                // Todo：考虑增加针对文件的 SHA 校验
                var record = await _importRecordRepository.SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.FileName == fileName); ;
                if (record != null) continue;

                // 增加文件导入记录
                await _importRecordRepository.AddAsync(new DocumentImportRecord()
                {
                    TaskId = taskId,
                    FileName = fileName,
                    QueueStatus = (int)QueueStatus.Uploaded,
                    KnowledgeBaseId = knowledgeBaseId,
                    DocumentType = (int)DocumentType.File,
                    Content = file
                });
            }
        }

        /// <summary>
        /// 从本文创建知识库
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="knowledgeBaseId"></param>
        /// <param name="text"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task ImportKnowledgeFromText(string taskId, long knowledgeBaseId, string text)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);
            await _importRecordRepository.AddAsync(new DocumentImportRecord()
            {
                TaskId = taskId,
                FileName = Guid.NewGuid().ToString("N"),
                QueueStatus = (int)QueueStatus.Uploaded,
                KnowledgeBaseId = knowledgeBaseId,
                DocumentType = (int)DocumentType.Text,
                Content = text,
            });
        }

        /// <summary>
        /// 从网址构建知识库
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <param name="knowledgeBaseId">知识库ID</param>
        /// <param name="url">网址</param>
        /// <returns></returns>
        public async Task ImportKnowledgeFromUrl(string taskId, long knowledgeBaseId, string url)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);
            await _importRecordRepository.AddAsync(new DocumentImportRecord()
            {
                TaskId = taskId,
                FileName = Guid.NewGuid().ToString("N"),
                QueueStatus = (int)QueueStatus.Uploaded,
                KnowledgeBaseId = knowledgeBaseId,
                DocumentType = (int)DocumentType.Url,
                Content = url,
            });
        }

        /// <summary>
        /// 异步处理队列任务
        /// </summary>
        /// <returns></returns>
        public async Task HandleImportingQueueAsync(int batchLimit = 5)
        {
            var webHostEnvironment = _serviceProvider.GetRequiredService<IWebHostEnvironment>();

            var records = await _importRecordRepository.FindAsync(x => x.QueueStatus == (int)QueueStatus.Uploaded);
            _logger.LogInformation($"There are {records.Count} files to be processed.");

            var tasks = records.OrderBy(x => x.CreatedAt).Take(batchLimit).Select(async record =>
            {
                var knowledgeBase = await GetKnowledgeBaseById(record.KnowledgeBaseId, true);
                if (knowledgeBase != null)
                {
                    if (record.DocumentType == (int)DocumentType.Text)
                    {
                        await HandleTextImportAsync(record, knowledgeBase);
                    }
                    else if (record.DocumentType == (int)DocumentType.Url)
                    {
                        await HandleUrlImportAsync(record, knowledgeBase);
                    }
                    else if (record.DocumentType == (int)DocumentType.File)
                    {
                        await HandleFileImportAsync(record, knowledgeBase);
                    }
                }
            });

            await Task.WhenAll(tasks);
        }

        private async Task HandleFileImportAsync(DocumentImportRecord record, KnowledgeBase knowledgeBase)
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var memoryService = serviceProviderScope.ServiceProvider.GetService<IMemoryService>();
            var memoryServerless = await memoryService.CreateByKnowledgeBase(knowledgeBase);
            var importRecordRepository = serviceProviderScope.ServiceProvider.GetService<IRepository<DocumentImportRecord>>();
            AddDefaultHandlers(memoryServerless);

            var tags = new TagCollection
            {
                { KernelMemoryTags.TaskId, record.TaskId },
                { KernelMemoryTags.FileName, record.FileName },
                { KernelMemoryTags.KnowledgeBaseId, record.KnowledgeBaseId.ToString() },
            };
            var document = new Document(tags: tags, filePaths: new List<string> { record.Content });

            // 更新文件导入记录
            record.QueueStatus = (int)QueueStatus.Processing;
            record.ProcessStartTime = DateTime.Now;
            await importRecordRepository.UpdateAsync(record);

            // 导入文档
            await memoryServerless.ImportDocumentAsync(document, steps: new List<string>()
            {
                "extract_text",
                "split_text_in_partitions",
                "generate_embeddings",
                "save_memory_records",
                UpdateQueueStatusHandler.GetCurrentStepName()
            });
        }

        private async Task HandleTextImportAsync(DocumentImportRecord record, KnowledgeBase knowledgeBase)
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var memoryService = serviceProviderScope.ServiceProvider.GetService<IMemoryService>();
            var memoryServerless = await memoryService.CreateByKnowledgeBase(knowledgeBase);
            var importRecordRepository = serviceProviderScope.ServiceProvider.GetService<IRepository<DocumentImportRecord>>();
            AddDefaultHandlers(memoryServerless);

            var tags = new TagCollection
            {
                { KernelMemoryTags.TaskId, record.TaskId },
                { KernelMemoryTags.FileName, record.FileName},
                { KernelMemoryTags.KnowledgeBaseId, record.KnowledgeBaseId.ToString() },
            };

            // 更新文件导入记录
            record.QueueStatus = (int)QueueStatus.Processing;
            record.ProcessStartTime = DateTime.Now;
            await importRecordRepository.UpdateAsync(record);

            await memoryServerless.ImportTextAsync(record.Content, tags: tags, steps: new List<string>()
            {
                "extract_text",
                "split_text_in_partitions",
                "generate_embeddings",
                "save_memory_records",
                UpdateQueueStatusHandler.GetCurrentStepName()
            });
        }

        private async Task HandleUrlImportAsync(DocumentImportRecord record, KnowledgeBase knowledgeBase)
        {
            using var serviceProviderScope = _serviceProvider.CreateScope();
            var memoryService = serviceProviderScope.ServiceProvider.GetService<IMemoryService>();
            var memoryServerless = await memoryService.CreateByKnowledgeBase(knowledgeBase);
            var importRecordRepository = serviceProviderScope.ServiceProvider.GetService<IRepository<DocumentImportRecord>>();
            AddDefaultHandlers(memoryServerless);

            var tags = new TagCollection
            {
                { KernelMemoryTags.TaskId, record.TaskId },
                { KernelMemoryTags.FileName, record.FileName},
                { KernelMemoryTags.KnowledgeBaseId, record.KnowledgeBaseId.ToString() },
                { KernelMemoryTags.Url, record.Content },
            };

            // 更新文件导入记录
            record.QueueStatus = (int)QueueStatus.Processing;
            record.ProcessStartTime = DateTime.Now;
            await importRecordRepository.UpdateAsync(record);

            await memoryServerless.ImportWebPageAsync(record.Content, tags: tags, steps: new List<string>()
            {
                "extract_text",
                "split_text_in_partitions",
                "generate_embeddings",
                "save_memory_records",
                UpdateQueueStatusHandler.GetCurrentStepName()
            });
        }

        public async Task DeleteKnowledgeBaseChunksById(long knowledgeBaseId)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var records = await _importRecordRepository.FindAsync(x => x.KnowledgeBaseId == knowledgeBaseId);
            foreach (var record in records)
            {
                await memoryServerless.DeleteDocumentAsync(record.FileName);
                await _importRecordRepository.DeleteAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.FileName == record.FileName);
            }
        }

        public async Task DeleteKnowledgeBaseChunksByFileName(long knowledgeBaseId, string fileName)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);
            await memoryServerless.DeleteDocumentAsync(fileName);
            await _importRecordRepository.DeleteAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.FileName == fileName);
        }

        public async Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double minRelevance = 0, int limit = 5)
        {
            var kmSearchResult = new KMSearchResult() { Question = question };

            // 查询知识库
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var memoryFilter = new MemoryFilter()
                .ByTag(KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString());

            var searchResult = await memoryServerless.SearchAsync(question, filter: memoryFilter, minRelevance: minRelevance, limit: limit);
            if (searchResult.NoResult) return kmSearchResult;

            if (searchResult.Results.Any())
            {
                kmSearchResult.RelevantSources =
                    searchResult.Results.Select(x => new KMCitation()
                    {
                        SourceName = x.SourceName,
                        Partitions = x.Partitions.Select(y => new KMPartition(y)).ToList()
                    })
                    .ToList();
            }
            return kmSearchResult;
        }

        public async Task<KMAskResult> AskAsync(long knowledgeBaseId, string question, double minRelevance = 0)
        {
            var askResult = new KMAskResult() { Question = question, RelevantSources = new List<KMCitation>() };

            // 查询知识库
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var memoryFilter = new MemoryFilter()
                .ByTag(KernelMemoryTags.KnowledgeBaseId, knowledgeBaseId.ToString());

            var memoryAnswer = await memoryServerless.AskAsync(question, filter: memoryFilter, minRelevance: minRelevance);
            askResult.Answer = memoryAnswer.Result;

            if (memoryAnswer.RelevantSources.Any())
            {
                askResult.RelevantSources = memoryAnswer.RelevantSources.Select(x => new KMCitation()
                {
                    SourceName = x.SourceName,
                    Partitions = x.Partitions.Select(y => new KMPartition(y)).ToList()
                })
                .ToList();
            }

            return askResult;
        }

        public async Task<bool> IsDocumentReady(long knowledgeBaseId, string fileName)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);
            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);
            return (await memoryServerless.IsDocumentReadyAsync(fileName));
        }

        public async Task<List<KnowledgeBaseFile>> GetKnowledgeBaseFiles(long knowledgeBaseId)
        {
            var records = await _importRecordRepository.FindAsync(x => x.KnowledgeBaseId == knowledgeBaseId);
            return records.Select(x => new KnowledgeBaseFile()
            {
                FileName = x.FileName,
                KnowledgeBaseId = x.KnowledgeBaseId,
                QueueStatus = x.QueueStatus,
            })
            .ToList();
        }

        private async Task<KnowledgeBase> GetKnowledgeBaseById(long knowledgeBaseId, bool renew = false)
        {
            KnowledgeBase knowledgeBase = null;
            if (!renew)
            {
                knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            }
            else
            {
                var serviceProviderScope = _serviceProvider.CreateScope();
                var knowledgeBaseRespository = serviceProviderScope.ServiceProvider.GetService<IRepository<KnowledgeBase>>();
                knowledgeBase = await knowledgeBaseRespository.GetAsync(knowledgeBaseId);
            }

            if (knowledgeBase == null) throw new InvalidOperationException("The knowledgebase must exists.");

            return knowledgeBase;
        }

        private void AddDefaultHandlers(MemoryServerless memoryServerless)
        {
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<UpdateQueueStatusHandler>();

            memoryServerless.Orchestrator.AddHandler<TextExtractionHandler>("extract_text");
            memoryServerless.Orchestrator.AddHandler<TextPartitioningHandler>("split_text_in_partitions");
            memoryServerless.Orchestrator.AddHandler<GenerateEmbeddingsHandler>("generate_embeddings");
            memoryServerless.Orchestrator.AddHandler<SaveRecordsHandler>("save_memory_records");
            memoryServerless.Orchestrator.AddHandler(new UpdateQueueStatusHandler(_importRecordRepository, logger));
        }

        private KMPartition ParseAsKMPartition(NpgsqlDataReader reader)
        {
            var partion = new Microsoft.KernelMemory.Citation.Partition();
            partion.Text = reader["content"].ToString();
            partion.PartitionNumber = int.Parse(ParseFromTags(reader, KernelMemoryTags.PartitionNumber));
            partion.SectionNumber = int.Parse(ParseFromTags(reader, KernelMemoryTags.SectionNumber));

            var tags = new TagCollection
            {
                { KernelMemoryTags.DocumentId, ParseFromTags(reader, KernelMemoryTags.DocumentId) },
                { KernelMemoryTags.TaskId, ParseFromTags(reader, KernelMemoryTags.TaskId) },
                { KernelMemoryTags.KnowledgeBaseId, ParseFromTags(reader, KernelMemoryTags.KnowledgeBaseId) },
                { KernelMemoryTags.FileName, ParseFromTags(reader, KernelMemoryTags.FileName) }
            };

            partion.Tags = tags;
            return new KMPartition(partion);
        }

        private string ParseFromTags(NpgsqlDataReader reader, string key)
        {
            var tags = ((string[])reader["tags"]);
            var tag = tags.FirstOrDefault(x => x.IndexOf(key) != -1);
            if (tag != null)
                return tag.Split(new Char[] { ':' })[1];

            return string.Empty;
        }
    }

    internal class UpdateQueueStatusHandler : IPipelineStepHandler
    {
        public string StepName => "update_quque_status";
        public static string GetCurrentStepName() => "update_quque_status";

        private readonly IRepository<DocumentImportRecord> _importRecordRepository;
        private readonly ILogger<UpdateQueueStatusHandler> _logger;
        public UpdateQueueStatusHandler(IRepository<DocumentImportRecord> importRecordRepository, ILogger<UpdateQueueStatusHandler> logger)
        {
            _importRecordRepository = importRecordRepository;
            _logger = logger;
        }

        public async Task<(bool success, DataPipeline updatedPipeline)> InvokeAsync(DataPipeline pipeline, CancellationToken cancellationToken = default)
        {
            var taskId = pipeline.Tags[KernelMemoryTags.TaskId].FirstOrDefault();
            var fileName = pipeline.Tags[KernelMemoryTags.FileName].FirstOrDefault();
            var knowledgeBaseId = long.Parse(pipeline.Tags[KernelMemoryTags.KnowledgeBaseId].FirstOrDefault());


            var record = await _importRecordRepository.SingleOrDefaultAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.TaskId == taskId && x.FileName == fileName);
            if (record != null)
            {
                record.QueueStatus = (int)QueueStatus.Complete;
                record.ProcessEndTime = DateTime.Now;
                var totalSeconds = Math.Round((record.ProcessEndTime.Value - record.ProcessStartTime.Value).TotalSeconds, 2);
                record.ProcessDuartionTime = totalSeconds;
                await _importRecordRepository?.UpdateAsync(record);
                _logger.LogInformation($"Importing document '{fileName}' finished in {totalSeconds} seconds.");
            }

            return (true, pipeline);
        }
    }
}
