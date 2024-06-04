using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    public enum UrlType
    {
        [Description("通用")] Generic = 0,
        [Description("RSS/Atom")] RSS = 1,
        [Description("站点地图")] Sitemap = 2
    }
}
