namespace PostgreSQL.Embedding.Common.Models.Plugin
{
    public class LlmPluginFunctionArgumentModel
    {
        public string ArgumentName {  get; set; }
        public string ArgumentIntro {  get; set; }
        public string ArgumentValue { get; set; }
        public string DefaultValue {  get; set; }
        public string ArgumentType {  get; set; }
    }
}
