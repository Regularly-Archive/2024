using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum LlmAppType
    {
        [Description("聊天")] Chat = 0,
        [Description("知识库")] Knowledge = 1,
    }
}
