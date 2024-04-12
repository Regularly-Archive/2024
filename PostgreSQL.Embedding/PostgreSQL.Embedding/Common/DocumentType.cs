using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum DocumentType
    {
        [Description("文件")] File = 0,
        [Description("文本")] Text = 1,
        [Description("网址")] Url = 2,
    }
}
