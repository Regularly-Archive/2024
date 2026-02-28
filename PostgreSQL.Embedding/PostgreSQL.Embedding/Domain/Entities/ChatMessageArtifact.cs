using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities
{
    /// <summary>
    /// 产物
    /// </summary>
    [SugarTable("chat_message_artifacts")]
    public class ChatMessageArtifact : BaseEntity
    {
        /// <summary>
        /// 运行ID
        /// </summary>
        [SugarColumn(ColumnName = "run_id")]
        public string RunId { get; set; } = "";

        /// <summary>
        /// 消息ID
        /// </summary>
        [SugarColumn(ColumnName = "message_id")]
        public long MessageId { get; set; }

        /// <summary>
        /// 产物ID
        /// </summary>
        [SugarColumn(ColumnName = "file_id")]
        public string ArtifactId { get; set; } = "";

        /// <summary>
        /// 文件名
        /// </summary>
        [SugarColumn(ColumnName = "file_name")]
        public string FileName { get; set; } = "";

        /// <summary>
        /// 文件类型（code/image/document/audio/video）
        /// </summary>
        [SugarColumn(ColumnName = "file_type")]
        public int ArtifactType { get; set; }

        /// <summary>
        /// 访问URL
        /// </summary>
        [SugarColumn(ColumnName = "url", IsNullable = true)]
        public string? Url { get; set; }

        /// <summary>
        /// 是否可预览
        /// </summary>
        [SugarColumn(ColumnName = "can_preview")]
        public bool CanPreview { get; set; }

        /// <summary>
        /// 是否可下载
        /// </summary>
        [SugarColumn(ColumnName = "can_download")]
        public bool CanDownload { get; set; }

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        [SugarColumn(ColumnName = "file_size", IsNullable = true)]
        public long? FileSize { get; set; }
    }
}
