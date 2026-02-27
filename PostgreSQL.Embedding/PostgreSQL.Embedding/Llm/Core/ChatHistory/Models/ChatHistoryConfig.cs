namespace PostgreSQL.Embedding.Llm.Core.ChatHistory.Models
{
    /// <summary>
    /// 聊天历史配置
    /// </summary>
    public class ChatHistoryConfig
    {
        /// <summary>
        /// 活跃轮数（达到此值触发压缩）
        /// </summary>
        public int ActiveRounds { get; set; } = 5;

        /// <summary>
        /// 缓冲轮数（保留不压缩）
        /// </summary>
        public int BufferRounds { get; set; } = 3;

        /// <summary>
        /// 单条消息最大长度
        /// </summary>
        public int MaxMessageLength { get; set; } = 2000;
    }
}
