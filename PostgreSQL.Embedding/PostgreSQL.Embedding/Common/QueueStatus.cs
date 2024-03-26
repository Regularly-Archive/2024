using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum QueueStatus
    {
        [Description("已上传")] Uploaded = 0,
        [Description("处理中")] Processing = 1,
        [Description("已完成")] Complete = 2,
    }
}
