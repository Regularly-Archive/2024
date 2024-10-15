using JiebaNet.Segmenter;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.LlmServices.Abstration;
using Microsoft.KernelMemory;
using Npgsql;

namespace PostgreSQL.Embedding.LlmServices
{
    public class FullTextRetrievalService : IKnowledgeRetrievalService
    {
        private RetrievalType _retrievalType;
        public RetrievalType RetrievalType
        {
            get { return _retrievalType; }
        }

        private readonly string _fullTextSearchLanguage;
        private readonly string _postgrelConnectionString;
        private readonly IRepository<TablePrefixMapping> _tablePrefixMappingRepository;
        private readonly IRepository<KnowledgeBase> _knowledgeBaseRepository;
        private readonly IRepository<LlmModel> _llmModelRepository;
        private readonly IKernelService _kernelService;
        private readonly JiebaSegmenter _jiebaSegmenter;

        public FullTextRetrievalService(IConfiguration configuration,
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
            _retrievalType = RetrievalType.FullText;
        }

        public async Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double minRelevance = 0.5, int limit = 5)
        {
            // 通过分词获得关键字
            var segments = _jiebaSegmenter.CutForSearch(question).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var keywords = string.Join(" | ", segments);
            var sqlLike = string.Join(" OR ", segments.Select(x => $"t.content LIKE '%{x}%'"));

            // 组装 Kernel Memory 表名
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            var tablePrefixMapping = await _tablePrefixMappingRepository.FindAsync(x => x.FullName == knowledgeBase.EmbeddingModel);
            var tableName = $"sk-{tablePrefixMapping.ShortName.ToLower()}-default";

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
                ) AS t WHERE t.relevance > {minRelevance} ORDER BY t.relevance DESC LIMIT {limit}
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

        private async Task CreateFullTextSearchIndex(NpgsqlConnection connection, string tableName)
        {
            var createIndexSql = $"""CREATE INDEX IF NOT EXISTS idx_chinese_full_text_search ON "{tableName}" USING gin(to_tsvector('chinese', 'content'))""";
            using var command = new NpgsqlCommand(createIndexSql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
