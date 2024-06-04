using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum GenderType
    {
        [Description("男")] Male = 1,
        [Description("女")] Female = 2,
        [Description("保密")] Secrecy = 3
    }
}
