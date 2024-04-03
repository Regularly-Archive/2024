﻿using JiebaNet.Segmenter;
using Microsoft.KernelMemory;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel;
using Npgsql;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Collections.Immutable;
using System.Text;
using static Microsoft.KernelMemory.Citation;

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

        public FullTextSearchService(
            IConfiguration configuration,
            IRepository<TablePrefixMapping> tablePrefixMappingRepository,
            IRepository<KnowledgeBase> knowledgeBaseRepository,
            IRepository<LlmModel> llmModelRepository,
            IKernelService kernelService
            )
        {
            _fullTextSearchLanguage = "english";
            _postgrelConnectionString = configuration["ConnectionStrings:Default"]!;
            _tablePrefixMappingRepository = tablePrefixMappingRepository;
            _knowledgeBaseRepository = knowledgeBaseRepository;
            _llmModelRepository = llmModelRepository;
            _kernelService = kernelService;
            _jiebaSegmenter = new JiebaSegmenter();
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

            var promptTemplate = LoadPromptTemplate("RAGPrompt.txt");

            var settings = new OpenAIPromptExecutionSettings () { Temperature = 0.75 };
            var func = kernel.CreateFunctionFromPrompt(promptTemplate, settings);
            var chatResult = await kernel.InvokeAsync<string>(
                function: func,
                arguments: new KernelArguments() { ["context"] = context, ["name"] = "ChatGPT", ["question"] = question }
            );

            result.Answer = chatResult;
            return result;
        }

        public async Task<KMSearchResult> SearchAsync(long knowledgeBaseId, string question, double? minRelevance, int? limit)
        {
            // 通过分词获得关键字
            var keywords = string.Join(" | ", _jiebaSegmenter.CutForSearch(question));

            // 组装 Kernel Memory 表名
            var knowledgeBase = await _knowledgeBaseRepository.GetAsync(knowledgeBaseId);
            var tablePrefixMapping = await _tablePrefixMappingRepository.SingleOrDefaultAsync(x => x.FullName == knowledgeBase.EmbeddingModel);
            var tableName = $"sk-{tablePrefixMapping.ShortName.ToLower()}-default";

            var fullTextSearchSql = $"""
                SELECT
                  t.*,
                  ts_rank(
                    to_tsvector('{_fullTextSearchLanguage}', t.content),
                    to_tsquery('{_fullTextSearchLanguage}', '{keywords}')
                  ) AS relevance
                FROM
                  "{tableName}" t
                WHERE
                  t.content @@ to_tsquery('{_fullTextSearchLanguage}', '{keywords}') AND t.tags @> ARRAY['{KernelMemoryTags.KnowledgeBaseId}:{knowledgeBaseId}']
                ORDER BY
                  relevance DESC;
            """;

            using var connection = new NpgsqlConnection(_postgrelConnectionString);
            using var command = new NpgsqlCommand(fullTextSearchSql, connection);

            await connection.OpenAsync();
            using var reader = command.ExecuteReader();

            var partitions = new List<KMPartition>();
            while (reader.Read())
            {
                var partition = ParseAsKMPartition(reader);
                if (minRelevance.HasValue && partition.Relevance < minRelevance.Value)
                    continue;

                partitions.Add(partition);
            }

            if (limit.HasValue)
                partitions = partitions.Take(limit.Value).ToList();

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
            var contextBuilder = new StringBuilder();
            foreach (var citation in searchResult.RelevantSources)
            {
                foreach (var part in citation.Partitions)
                {
                    contextBuilder.AppendLine($"fileName:{citation.SourceName}; Relevance:{(part.Relevance * 100).ToString("F2")}%; Content: {part.Text}");
                }
            }

            return contextBuilder.ToString();
        }

        private string LoadPromptTemplate(string fileName)
        {
            var promptDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Common/Prompts");
            var promptTemplate = Path.Combine(promptDirectory, fileName);
            if (!File.Exists(promptTemplate))
                throw new ArgumentException($"The prompt template file '{promptTemplate}' can not be found.");

            return File.ReadAllText(promptTemplate);
        }
    }
}