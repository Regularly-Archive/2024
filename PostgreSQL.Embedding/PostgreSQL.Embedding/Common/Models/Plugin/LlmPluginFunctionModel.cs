namespace PostgreSQL.Embedding.Common.Models.Plugin
{
    public class LlmPluginFunctionModel
    {
        public string FunctionName { get; set; }
        public string FunctionIntro {  get; set; }
        public List<LlmPluginFunctionArgumentModel> Arguments { get; set; }
    }
}
