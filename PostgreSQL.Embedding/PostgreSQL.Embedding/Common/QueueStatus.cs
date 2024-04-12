using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum QueueStatus
    {
        [Description("待解析")] Uploaded = 0,
        [Description("解析中")] Processing = 1,
        [Description("已解析")] Complete = 2,
    }
}
