using JiebaNet.Segmenter;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Newtonsoft.Json;
using Npgsql;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;

namespace PostgreSQL.Embedding.LlmServices
{
    public class FullTextSearchService : IFullTextSearchService
    {
        private readonly string _fullTextSearchLanguage;
        private readonly string _postgrelConnectionString;
        private readonly IRepository<TablePrefixMapping> _tablePrefixMappingRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly IRepository<LlmModel> _llmModelRepository;
        private readonly IKernelService _kernelService;
        private readonly JiebaSegmenter _jiebaSegmenter;
        private readonly PromptTemplateService _promptTemplateService;

        public FullTextSearchService(
            IConfiguration configuration,
            IRepository<TablePrefixMapping> tablePrefixMappingRepository,
            IRepository<KnowledgeBase> knowledgeBaseRepository,
            IRepository<LlmModel> llmModelRepository,
            IKernelService kernelService,
            PromptTemplateService promptTemplateService
            )
        {
            _fullTextSearchLanguage = "chinese";
            _postgrelConnectionString = configuration["ConnectionStrings:Default"]!;
            _tablePrefixMappingRepository = tablePrefixMappingRepository;
            _knowledgeBaseRepository = knowledgeBaseRepository;
            _llmModelRepository = llmModelRepository;
            _kernelService = kernelService;
            _jiebaSegmenter = new JiebaSegmenter();
            _promptTemplateService = promptTemplateService;
        }

        public async Task<KMAskResult> AskAsync(long knowledgeBaseId, string question, double? minRelevance = 0.75)
        {
            var result = new KMAskResult();
            result.Question = question;

            // 构建 Kernel
            var textModel = await _llmModelRepository.SingleOrDefaultAsync(x => x.ModelType == (int)ModelType.TextGeneration && x.ModelName == "gpt-3.5-turbo");
            var kernel = await _kernelService.GetKernel(textModel);

            // 全文检索
            var searchResult = await SearchAsync(knowledgeBaseId, question, minRelevance, 5);
            if (!searchResult.RelevantSources.Any())
            {
                result.Answer = "抱歉，我无法回答你的问题";
                return result;
            }

            result.RelevantSources = searchResult.RelevantSources;
            var context = BuildKnowledgeContext(searchResult);

            var promptTemplate = _promptTemplateService.LoadTemplate("RAGPrompt.txt");
            promptTemplate.AddVariable("name", "ChatGPT");
            promptTemplate.AddVariable("context", context);
            promptTemplate.AddVariable("question", question);
            promptTemplate.AddVariable("empty_answer", Common.Constants.DefaultEmptyAnswer);


            var executionSettings = new OpenAIPromptExecutionSettings () { Temperature = 0.75 };
            var chatResult = await promptTemplate.InvokeAsync<string>(kernel, executionSettings);

            result.Answer = chatResult;
            return result;
        }

        public async Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double? minRelevance, int? limit)
        {
            // 通过分词获得关键字
            var segments = _jiebaSegmenter.CutForSearch(question).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var keywords = string.Join(" | ", segments);
            var sqlLike = string.Join(" OR ", segments.Select(x => $"t.content LIKE '%{x}%'"));

            // 组装 Kernel Memory 表名
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            var tablePrefixMapping = await _tablePrefixMappingRepository.SingleOrDefaultAsync(x => x.FullName == knowledgeBase.EmbeddingModel);
            var tableName = $"sk-{tablePrefixMapping.ShortName.ToLower()}-default";

            if (!minRelevance.HasValue) minRelevance = 0.5;
            if (!limit.HasValue) limit = 5;

            var fullTextSearchSql = $"""
                SELECT * FROM
                (
                    SELECT
                      t.*,
                      ts_rank_cd(
                        to_tsvector('{_fullTextSearchLanguage}', t.content),
                        to_tsquery('{_fullTextSearchLanguage}', '{keywords}')
                      ) AS relevance
                    FROM
                      "{tableName}" t
                    WHERE
                      (t.content @@ to_tsquery('{_fullTextSearchLanguage}', '{keywords}') OR {sqlLike}) AND t.tags @> ARRAY['{KernelMemoryTags.KnowledgeBaseId}:{knowledgeBaseId}']
                    ORDER BY
                      relevance DESC
                ) AS t WHERE t.relevance > {minRelevance.Value} ORDER BY t.relevance DESC LIMIT {limit.Value}
            """;

            using var connection = new NpgsqlConnection(_postgrelConnectionString);
            using var command = new NpgsqlCommand(fullTextSearchSql, connection);

            await connection.OpenAsync();
            await CreateFullTextSearchIndex(connection, tableName);
            using var reader = command.ExecuteReader();

            var partitions = new List<KMPartition>();
            while (reader.Read())
            {
                var partition = ParseAsKMPartition(reader);
                partitions.Add(partition);
            }

            var citations = partitions.GroupBy(x => x.FileName).Select(x => new KMCitation()
            {
                SourceName = x.Key,
                Partitions = x.ToList()
            })
            .ToList();

            return new KMSearchResult() { Question = question, RelevantSources = citations };
        }

        private KMPartition ParseAsKMPartition(NpgsqlDataReader reader)
        {
            var partion = new Microsoft.KernelMemory.Citation.Partition();
            partion.Text = reader["content"].ToString();
            partion.Relevance = float.Parse(reader["relevance"].ToString());
            partion.PartitionNumber = int.Parse(ParseFromTags(reader, KernelMemoryTags.PartitionNumber));
            partion.SectionNumber = int.Parse(ParseFromTags(reader, KernelMemoryTags.SectionNumber));

            var tags = new TagCollection();
            tags.Add(KernelMemoryTags.DocumentId, ParseFromTags(reader, KernelMemoryTags.DocumentId));
            tags.Add(KernelMemoryTags.TaskId, ParseFromTags(reader, KernelMemoryTags.TaskId));
            tags.Add(KernelMemoryTags.KnowledgeBaseId, ParseFromTags(reader, KernelMemoryTags.KnowledgeBaseId));
            tags.Add(KernelMemoryTags.FileName, ParseFromTags(reader, KernelMemoryTags.FileName));
            tags.Add(KernelMemoryTags.FileId, ParseFromTags(reader, KernelMemoryTags.FileId));
            tags.Add(KernelMemoryTags.PartId, ParseFromTags(reader, KernelMemoryTags.PartId));

            partion.Tags = tags;
            return new KMPartition(partion);
        }

        private string ParseFromTags(NpgsqlDataReader reader, string key)
        {
            var tags = ((string[])reader["tags"]);
            var tag = tags.FirstOrDefault(x => x.IndexOf(key) != -1);
            if (tag != null)
            {
                return tag.Split(new Char[] { ':' })[1];
            }

            return string.Empty;
        }

        private string BuildKnowledgeContext(KMSearchResult searchResult)
        {
            var partitions = searchResult.RelevantSources.SelectMany(x => x.Partitions).ToList();
            var chunks = partitions.Select(x => new
            {
                FileName = x.FileName,
                Relevance = x.Relevance,
                Text = x.Text
            })
            .ToList();

            var jsonFormatContext = JsonConvert.SerializeObject(chunks);
            return jsonFormatContext;
        }

        private async Task CreateFullTextSearchIndex(NpgsqlConnection connection, string tableName)
        {
            var createIndexSql = $"""CREATE INDEX IF NOT EXISTS idx_chinese_full_text_search ON "{tableName}" USING gin(to_tsvector('chinese', 'content'))""";
            using var command = new NpgsqlCommand(createIndexSql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
