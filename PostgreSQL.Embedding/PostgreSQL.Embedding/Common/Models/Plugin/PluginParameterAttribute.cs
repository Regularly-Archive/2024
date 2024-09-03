namespace PostgreSQL.Embedding.Common.Models.Plugin
{
    public class PluginParameterAttribute : Attribute
    {
        public bool Required { get; set; } = false;
        public string Description { get; set; }
    }
}
