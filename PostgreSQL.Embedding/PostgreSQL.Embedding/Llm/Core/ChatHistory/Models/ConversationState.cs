using PostgreSQL.Embedding.Domain.Entities;
using System.Text.Json;

namespace PostgreSQL.Embedding.Llm.Core.ChatHistory.Models
{
    /// <summary>
    /// 会话状态（内存中）
    /// </summary>
    public class ConversationState
    {
        /// <summary>
        /// 当前压缩块（只有一个）
        /// </summary>
        public CompressedBlock? CompressedBlock { get; set; }

        /// <summary>
        /// 压缩块覆盖的消息数量
        /// </summary>
        public int CompressedMessageCount { get; set; }

        /// <summary>
        /// 获取活跃消息起始索引位置
        /// </summary>
        public int GetActiveStartIndex()
        {
            return CompressedMessageCount;
        }

        /// <summary>
        /// 从数据库实体加载
        /// </summary>
        public static ConversationState FromEntity(AppConversationState? entity)
        {
            if (entity == null)
                return new ConversationState();

            CompressedBlock? block = null;
            if (entity.BlockStartMsgId.HasValue && entity.BlockEndMsgId.HasValue)
            {
                block = new CompressedBlock
                {
                    StartMsgId = entity.BlockStartMsgId.Value,
                    EndMsgId = entity.BlockEndMsgId.Value,
                    Topic = entity.BlockTopic ?? "",
                    Summary = entity.BlockSummary ?? "",
                    Hint = entity.BlockHint ?? "",
                    KeyPoints = entity.BlockKeyPoints ?? new List<string>(),
                    CompressedAt = entity.BlockCompressedAt ?? DateTime.UtcNow
                };
            }

            return new ConversationState
            {
                CompressedBlock = block,
                CompressedMessageCount = entity?.CompressedMessageCount ?? 0
            };
        }

        /// <summary>
        /// 转换为数据库实体
        /// </summary>
        public AppConversationState ToEntity(string conversationId)
        {
            return new AppConversationState
            {
                ConversationId = conversationId,
                BlockTopic = CompressedBlock?.Topic,
                BlockSummary = CompressedBlock?.Summary,
                BlockKeyPoints = CompressedBlock?.KeyPoints,
                BlockHint = CompressedBlock?.Hint,
                BlockStartMsgId = CompressedBlock?.StartMsgId,
                BlockEndMsgId = CompressedBlock?.EndMsgId,
                BlockCompressedAt = CompressedBlock?.CompressedAt,
                CompressedMessageCount = CompressedMessageCount,
                UpdatedAt = DateTime.UtcNow
            };
        }
    }
}
