using DocumentFormat.OpenXml.Math;
using JiebaNet.Segmenter;
using LLama.Common;
using Microsoft.SemanticKernel;
using Npgsql;
using PostgreSQL.Embedding.Common.Models.KernelMemory;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;
using System.Reflection;
using SqlSugar;

namespace PostgreSQL.Embedding.LlmServices
{
    public class ChatHistoriesService : IChatHistoriesService
    {
        private readonly IRepository<ChatMessage> _chatMessageRepository;
        private readonly IRepository<AppConversation> _appConversationRepository;
        private readonly JiebaSegmenter _jiebaSegmenter;
        private readonly IConfiguration _configuration;
        private readonly string _fullTextSearchLanguage = "chinese";
        private readonly string _postgrelConnectionString;
        public ChatHistoriesService(IRepository<ChatMessage> chatMessageRepository, IRepository<AppConversation> appConversationRepository, IConfiguration configuration)
        {
            _chatMessageRepository = chatMessageRepository;
            _appConversationRepository = appConversationRepository;
            _jiebaSegmenter = new JiebaSegmenter();
            _configuration = configuration;
            _postgrelConnectionString = configuration["ConnectionStrings:Default"]!;
        }

        /// <summary>
        /// 添加系统消息
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="conversationId"></param>
        /// <param name="content"></param>
        /// <returns></returns>
        public async Task<long> AddSystemMessageAsync(long appId, string conversationId, string content)
        {
            var message = await _chatMessageRepository.AddAsync(new ChatMessage()
            {
                AppId = appId,
                ConversationId = conversationId,
                Content = content,
                IsUserMessage = false
            });

            return message.Id;
        }

        /// <summary>
        /// 添加用户消息
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="conversationId"></param>
        /// <param name="content"></param>
        /// <returns></returns>
        public async Task<long> AddUserMessageAsync(long appId, string conversationId, string content)
        {
            var message = await _chatMessageRepository.AddAsync(new ChatMessage()
            {
                AppId = appId,
                ConversationId = conversationId,
                Content = content,
                IsUserMessage = true
            });

            return message.Id;
        }

        /// <summary>
        /// 获取指定应用下的会话列表
        /// </summary>
        /// <param name="appId"></param>
        /// <returns></returns>
        public async Task<List<AppConversation>> GetAppConversationsAsync(long appId)
        {
            var list = await _appConversationRepository.FindAsync(x => x.AppId == appId);
            return list.OrderByDescending(x => x.CreatedAt).ToList();
        }

        /// <summary>
        /// 获取指定应用、指定会话下的消息
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="conversationId"></param>
        /// <returns></returns>
        public async Task<List<ChatMessage>> GetConversationMessagesAsync(long appId, string conversationId)
        {
            var messages = await _chatMessageRepository.FindAsync(x => x.AppId == appId && x.ConversationId == conversationId);
            messages = messages.OrderBy(x => x.CreatedAt).ToList();
            return messages;
        }

        /// <summary>
        /// 新建会话
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="conversationId"></param>
        /// <param name="conversationName"></param>
        /// <returns></returns>
        public async Task AddConversationAsync(long appId, string conversationId, string conversationName)
        {
            var conversation = await _appConversationRepository.SingleOrDefaultAsync(x => x.AppId == appId && x.ConversationId == conversationId);
            if (conversation != null) return;

            await _appConversationRepository.AddAsync(new AppConversation()
            {
                AppId = appId,
                ConversationId = conversationId,
                Summary = conversationName,
            });
        }

        /// <summary>
        /// 删除会话
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="conversationId"></param>
        /// <returns></returns>
        public async Task DeleteConversationAsync(long appId, string conversationId)
        {
            await _appConversationRepository.DeleteAsync(x => x.AppId == appId && x.ConversationId == conversationId);
            await _chatMessageRepository.DeleteAsync(x => x.AppId == appId && x.ConversationId == conversationId);
        }

        /// <summary>
        /// 更新会话
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="conversationId"></param>
        /// <param name="summary"></param>
        /// <returns></returns>
        public async Task UpdateConversationAsync(long appId, string conversationId, string summary)
        {
            var conversation = await _appConversationRepository.SingleOrDefaultAsync(x => x.AppId == appId && x.ConversationId == conversationId);
            if (conversation == null) return;

            conversation.Summary = summary;
            await _appConversationRepository.UpdateAsync(conversation);
        }
        
        /// <summary>
        /// 删除会话
        /// </summary>
        /// <param name="messageId"></param>
        /// <returns></returns>
        public Task DeleteConversationMessageAsync(long messageId)
        {
            return _chatMessageRepository.DeleteAsync(messageId);
        }

        /// <summary>
        /// 检索指定应用、指定会话内消息
        /// </summary>
        /// <param name="appId"></param>
        /// <param name="conversationId"></param>
        /// <param name="query"></param>
        /// <param name="minRelevance"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<List<ChatMessage>> SearchConversationMessagesAsync(long appId, string conversationId, string query, double? minRelevance = 0.5, int? limit = 5)
        {
            var segments = _jiebaSegmenter.CutForSearch(query).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            var keywords = string.Join(" | ", segments);
            var sqlLike = string.Join(" OR ", segments.Select(x => $"t.content LIKE '%{x}%'"));

            var tableName = typeof(ChatMessage).GetCustomAttribute<SugarTable>()?.TableName ?? nameof(ChatMessage);

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
                      (t.content @@ to_tsquery('{_fullTextSearchLanguage}', '{keywords}') OR {sqlLike}) AND t.app_id = '{appId}' AND t.conversation_id = '{conversationId}'
                    ORDER BY
                      relevance DESC
                ) AS t WHERE t.relevance > {minRelevance.Value} ORDER BY t.created_at ASC LIMIT {limit.Value}
            """;

            using var connection = new NpgsqlConnection(_postgrelConnectionString);
            using var command = new NpgsqlCommand(fullTextSearchSql, connection);

            await connection.OpenAsync();
            await CreateFullTextSearchIndex(connection, tableName);
            using var reader = command.ExecuteReader();

            var chatMessages = new List<ChatMessage>();
            while (reader.Read())
            {
                var chatMessage = ParseAsChatMessage(reader);
                chatMessages.Add(chatMessage);
            }

            return chatMessages;
        }

        private async Task CreateFullTextSearchIndex(NpgsqlConnection connection, string tableName)
        {
            var createIndexSql = $"""CREATE INDEX IF NOT EXISTS idx_chinese_full_text_search ON "{tableName}" USING gin(to_tsvector('chinese', 'content'))""";
            using var command = new NpgsqlCommand(createIndexSql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private ChatMessage ParseAsChatMessage(NpgsqlDataReader reader)
        {
            var chatMessage = new ChatMessage();
            chatMessage.Id = long.Parse(reader["id"].ToString());
            chatMessage.AppId = long.Parse(reader["app_id"].ToString());
            chatMessage.ConversationId = reader["conversation_id"].ToString();
            chatMessage.Content = reader["content"].ToString();
            chatMessage.IsUserMessage = bool.Parse(reader["is_user_message"].ToString());
            chatMessage.CreatedAt = DateTime.Parse(reader["created_at"].ToString());
            chatMessage.CreatedBy = reader["created_by"].ToString();
            chatMessage.UpdatedAt = DateTime.Parse(reader["updated_at"].ToString());
            chatMessage.UpdatedBy = reader["updated_by"].ToString();
            return chatMessage;
        }
    }
}
