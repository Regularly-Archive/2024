using System.Text.Json.Serialization;

namespace PostgreSQL.Embedding.Llm.Core.ChatHistory.Models
{
    /// <summary>
    /// 压缩块
    /// </summary>
    public class CompressedBlock
    {
        /// <summary>
        /// 起始消息ID
        /// </summary>
        public int StartMsgId { get; set; }

        /// <summary>
        /// 结束消息ID
        /// </summary>
        public int EndMsgId { get; set; }

        /// <summary>
        /// 主题
        /// </summary>
        public string Topic { get; set; } = "";

        /// <summary>
        /// 摘要
        /// </summary>
        public string Summary { get; set; } = "";

        /// <summary>
        /// 关键点
        /// </summary>
        public List<string> KeyPoints { get; set; } = new();

        /// <summary>
        /// 召回提示
        /// </summary>
        public string Hint { get; set; } = "";

        /// <summary>
        /// 压缩时间
        /// </summary>
        public DateTime CompressedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 转换为 XML 格式
        /// </summary>
        public string ToXml()
        {
            var keyPointsXml = string.Join("\n", KeyPoints.Select(k => $"    <item>{k}</item>"));

            return $@"<compressed_history range='{StartMsgId}-{EndMsgId}'>
  <topic>{Topic}</topic>
  <summary>{Summary}</summary>
  <key_points>
{keyPointsXml}
  </key_points>
  <hint>{Hint}</hint>
</compressed_history>";
        }
    }
}
