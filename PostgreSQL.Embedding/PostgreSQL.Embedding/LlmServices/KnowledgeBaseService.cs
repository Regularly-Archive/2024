using DocumentFormat.OpenXml.Math;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json;
using Npgsql;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.Common.Models.RAG;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using PostgreSQL.Embedding.Utils;
using SqlSugar;
using DocumentType = PostgreSQL.Embedding.Common.DocumentType;

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
        private readonly IEnumerable<IKnowledgeRetrievalService> _knowledgeRetrievalServices;

        public KnowledgeBaseService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            IMemoryService memoryService,
            IRepository<DocumentImportRecord> importRecordRepository,
            IRepository<KnowledgeBase> knowledgeBaseRepository,
            IRepository<TablePrefixMapping> tablePrefixMappingRepository,
            ILogger<KnowledgeBaseService> logger,
            IEnumerable<IKnowledgeRetrievalService> knowledgeRetrievalServices
            )
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _postgrelConnectionString = configuration["ConnectionStrings:Default"]!;
            _memoryService = memoryService;
            _importRecordRepository = importRecordRepository;
            _knowledgeBaseRepository = knowledgeBaseRepository;
            _tablePrefixMappingRepository = tablePrefixMappingRepository;
            _knowledgeRetrievalServices = knowledgeRetrievalServices;
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
        public async Task<PagedResult<KMPartition>> GetKnowledgeBaseChunks(long knowledgeBaseId, string fileName = null, int pageIndex = 1, int pageSize = 10)
        {
            // 组装 Kernel Memory 表名
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            var tablePrefixMapping = await _tablePrefixMappingRepository.FindAsync(x => x.FullName == knowledgeBase.EmbeddingModel);
            var tableName = $"sk-{tablePrefixMapping.ShortName.ToLower()}-default";

            using var connection = new NpgsqlConnection(_postgrelConnectionString);
            await connection.OpenAsync();

            var totalCount = GetKnowledgeBaseChunksCount(connection, tableName, knowledgeBaseId, fileName);
            var partitions = GetKnowledgeBaseChunksPageList(connection, tableName, pageIndex, pageSize, knowledgeBaseId, fileName);

            var pageResult = new PagedResult<KMPartition>() { TotalCount = (int)totalCount, Rows = partitions };
            return pageResult;
        }

        /// <summary>
        /// 获取文档分块信息总数
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="tableName"></param>
        /// <param name="knowledgeBaseId"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private long GetKnowledgeBaseChunksCount(NpgsqlConnection connection, string tableName, long knowledgeBaseId, string fileName = null)
        {
            var sqlText = $"""SELECT COUNT(*) FROM "{tableName}" t WHERE t.tags @> ARRAY['{KernelMemoryTags.KnowledgeBaseId}:{knowledgeBaseId}']""";
            if (!string.IsNullOrEmpty(fileName))
                sqlText += $""" AND t.tags @> ARRAY['{KernelMemoryTags.FileName}:{fileName}'] """;

            using var command = new NpgsqlCommand(sqlText, connection);
            var count = (long)command.ExecuteScalar();
            return count;
        }

        /// <summary>
        /// 文档分块信息分页查询
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="tableName"></param>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <param name="knowledgeBaseId"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
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
        public async Task ImportKnowledgeFromFiles(string taskId, long knowledgeBaseId, IEnumerable<string> filePaths)
        {
            foreach (var filePath in filePaths)
            {
                var fileName = Path.GetFileName(filePath);

                // 如果文件重复则直接忽略
                // Todo：考虑增加针对文件的 SHA 校验
                var record = await _importRecordRepository.FindAsync(x => x.KnowledgeBaseId == knowledgeBaseId && x.FileName == fileName); ;
                if (record != null) continue;

                // 增加文件导入记录
                await _importRecordRepository.AddAsync(new DocumentImportRecord()
                {
                    TaskId = taskId,
                    FileName = fileName,
                    QueueStatus = (int)QueueStatus.Uploaded,
                    KnowledgeBaseId = knowledgeBaseId,
                    DocumentType = (int)DocumentType.File,
                    Content = filePath
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
        public async Task ImportKnowledgeFromUrl(string taskId, long knowledgeBaseId, string url, int urlType, string contentSelector)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);
            if (urlType == (int)UrlType.Generic)
            {
                var extractionResult = await WebPageExtractor.ExtractWebPageAsync(url, contentSelector);
                await _importRecordRepository.AddAsync(new DocumentImportRecord()
                {
                    TaskId = taskId,
                    FileName = extractionResult.Title ?? Guid.NewGuid().ToString("N"),
                    QueueStatus = (int)QueueStatus.Uploaded,
                    KnowledgeBaseId = knowledgeBaseId,
                    DocumentType = (int)DocumentType.Url,
                    Content = JsonConvert.SerializeObject(extractionResult),
                });
            }
            else if (urlType == (int)UrlType.RSS)
            {
                var extractionResults = await RSSExtractor.ExtractAsync(url);
                foreach (var extractionResult in extractionResults)
                {
                    await _importRecordRepository.AddAsync(new DocumentImportRecord()
                    {
                        TaskId = taskId,
                        FileName = extractionResult.Title,
                        QueueStatus = (int)QueueStatus.Uploaded,
                        KnowledgeBaseId = knowledgeBaseId,
                        DocumentType = (int)DocumentType.Url,
                        Content = JsonConvert.SerializeObject(extractionResult)
                    });
                }
            }
            else if (urlType == (int)UrlType.Sitemap)
            {
                var sitemapEntries = await SitemapParser.ParseSitemap(url);
                foreach(var sitemapEntry in sitemapEntries)
                {
                    await _importRecordRepository.AddAsync(new DocumentImportRecord()
                    {
                        TaskId = taskId,
                        FileName = sitemapEntry.Url,
                        QueueStatus = (int)QueueStatus.Uploaded,
                        KnowledgeBaseId = knowledgeBaseId,
                        DocumentType = (int)DocumentType.Url,
                        Content = string.Empty
                    });
                }
            }
        }

        public async Task DeleteKnowledgeBaseChunksById(long knowledgeBaseId)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);

            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);

            var records = await _importRecordRepository.FindListAsync(x => x.KnowledgeBaseId == knowledgeBaseId);
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

        public async Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, RetrievalType retrievalType = RetrievalType.Mixed, double minRelevance = 0, int limit = 5)
        {
            var retrievalService = _knowledgeRetrievalServices.FirstOrDefault(x => x.RetrievalType == retrievalType);
            if (retrievalService != null)
                return (await retrievalService.SearchAsync(knowledgeBaseId, question, minRelevance, limit));


            var kmSearchResult = new KMSearchResult() { Question = question };
            foreach (var knowledgeRetrievalService in _knowledgeRetrievalServices)
            {
                var searchResult = await knowledgeRetrievalService.SearchAsync(knowledgeBaseId, question, minRelevance, limit);
                if (!searchResult.RelevantSources.Any()) continue;
                kmSearchResult.RelevantSources.AddRange(searchResult.RelevantSources);
            }

            return kmSearchResult;
        }

        public async Task<KMAskResult> AskAsync(long knowledgeBaseId, string question, RetrievalType retrievalType = RetrievalType.Mixed, double minRelevance = 0, int limit = 5)
        {
            if (retrievalType == RetrievalType.Vectors)
                return await AskByVectorsAsync(knowledgeBaseId, question, minRelevance, limit);

            var llmModelRepository = _serviceProvider.GetService<IRepository<LlmModel>>();
            var kernelService = _serviceProvider.GetService<IKernelService>();
            var promptTemplateService = _serviceProvider.GetService<PromptTemplateService>();

            var textModel = await llmModelRepository.FindAsync(x => x.ModelType == (int)ModelType.TextGeneration && x.IsDefaultModel == true);
            var kernel = await kernelService.GetKernel(textModel);

            var result = new KMAskResult();
            result.Question = question;

            var searchResult = await SearchAsync(knowledgeBaseId, question, retrievalType, minRelevance, limit);
            if (!searchResult.RelevantSources.Any())
            {
                result.Answer = "抱歉，我无法回答你的问题";
                return result;
            }

            result.RelevantSources = searchResult.RelevantSources;
            var context = BuildKnowledgeContext(searchResult, limit);

            var promptTemplate = promptTemplateService.LoadTemplate("RAGPrompt.txt");
            promptTemplate.AddVariable("name", "ChatGPT");
            promptTemplate.AddVariable("context", context);
            promptTemplate.AddVariable("question", question);
            promptTemplate.AddVariable("histories", string.Empty);
            promptTemplate.AddVariable("empty_answer", Common.Constants.DefaultEmptyAnswer);

            var executionSettings = new OpenAIPromptExecutionSettings() { Temperature = 0.75 };
            var chatResult = await promptTemplate.InvokeAsync<string>(kernel, executionSettings);

            result.Answer = chatResult;
            return result;
        }

        private async Task<KMAskResult> AskByVectorsAsync(long knowledgeBaseId, string question, double minRelevance = 0, int limit = 5)
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
                .Take(limit).ToList();
            }

            return askResult;
        }

        private string BuildKnowledgeContext(KMSearchResult searchResult, int limit)
        {
            var partitions = searchResult.RelevantSources.SelectMany(x => x.Partitions).ToList();
            var chunks = partitions.Select((x, i) => new LlmCitationModel
            {
                Index = i + 1,
                FileName = x.FileName,
                Relevance = x.Relevance,
                Text = $"[^{i + 1}]: {x.Text}",
                Url = $"/api/KnowledgeBase/{x.KnowledgeBaseId}/chunks/{x.FileId}/{x.PartId}"
            })
            .OrderByDescending(x => x.Relevance)
            .Take(limit)
            .ToList();

            var jsonFormatContext = JsonConvert.SerializeObject(chunks);
            return jsonFormatContext;
        }

        public async Task<bool> IsDocumentReady(long knowledgeBaseId, string fileName)
        {
            // 查询知识库
            var knowledgeBase = await GetKnowledgeBaseById(knowledgeBaseId);
            var memoryServerless = await _memoryService.CreateByKnowledgeBase(knowledgeBase);
            return (await memoryServerless.IsDocumentReadyAsync(fileName));
        }

        private async Task<KnowledgeBase> GetKnowledgeBaseById(long knowledgeBaseId)
        {
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            return knowledgeBase;
        }

        /// <summary>
        /// 重新导入知识
        /// </summary>
        /// <param name="knowledgeBaseId">知识库ID</param>
        /// <param name="fileName">文件名称</param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public Task ReImportKnowledges(long knowledgeBaseId, string fileName = null)
        {
            // Todo:
            throw new NotImplementedException();
        }


        /// <summary>
        /// 获取指定的文本块信息
        /// </summary>
        /// <param name="knowledgeBaseId"></param>
        /// <param name="fileId"></param>
        /// <param name="partId"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<KMPartition> GetKnowledgeBaseChunk(long knowledgeBaseId, string fileId, string partId)
        {
            // 组装 Kernel Memory 表名
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            var tablePrefixMapping = await _tablePrefixMappingRepository.FindAsync(x => x.FullName == knowledgeBase.EmbeddingModel);
            var tableName = $"sk-{tablePrefixMapping.ShortName.ToLower()}-default";

            using var connection = new NpgsqlConnection(_postgrelConnectionString);
            await connection.OpenAsync();

            var partitions = GetKnowledgeBaseChunkInternal(connection, tableName, knowledgeBaseId, fileId, partId);
            return partitions.FirstOrDefault();
        }

        private List<KMPartition> GetKnowledgeBaseChunkInternal(NpgsqlConnection connection, string tableName, long knowledgeBaseId, string fileId, string partId)
        {
            // 拼接 SQL 语句，按标签进行过滤
            var sqlText = $"""
                SELECT t.* FROM "{tableName}" t WHERE t.tags @> ARRAY['{KernelMemoryTags.KnowledgeBaseId}:{knowledgeBaseId}']
                    AND t.tags @> ARRAY['{KernelMemoryTags.FileId}:{fileId}'] AND t.tags @> ARRAY['{KernelMemoryTags.PartId}:{partId}']
            """;

            using var queryCommand = new NpgsqlCommand(sqlText, connection);
            using var reader = queryCommand.ExecuteReader();

            var partitions = new List<KMPartition>();
            while (reader.Read())
            {
                var partition = ParseAsKMPartition(reader);
                partitions.Add(partition);
            }

            return partitions;
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
                { KernelMemoryTags.FileName, ParseFromTags(reader, KernelMemoryTags.FileName) },
                { KernelMemoryTags.FileId, ParseFromTags(reader, KernelMemoryTags.FileId) },
                { KernelMemoryTags.PartId, ParseFromTags(reader, KernelMemoryTags.PartId) }
            };

            partion.Tags = tags;
            return new KMPartition(partion);
        }

        private string ParseFromTags(NpgsqlDataReader reader, string key)
        {
            var tags = ((string[])reader["tags"]);
            var tag = tags.FirstOrDefault(x => x.IndexOf(key) != -1);
            if (tag != null)
                return tag.Split(new Char[] { ':' }, 2)[1];

            return string.Empty;
        }
    }
}
