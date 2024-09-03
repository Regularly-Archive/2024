namespace PostgreSQL.Embedding.Common.Models.Plugin
{
    public class LlmPluginQueryModel
    {
        public string Id { get; set; }
        public string PluginName { get; set; }
        public string PluginIntro {  get; set; }
        public string TypeName { get; set; }
    }
}
