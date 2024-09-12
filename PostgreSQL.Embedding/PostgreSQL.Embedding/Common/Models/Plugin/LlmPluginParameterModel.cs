namespace PostgreSQL.Embedding.Common.Models.Plugin
{
    public class LlmPluginParameterModel
    {
        public string ParameterName { get; set; }
        public string ParameterType {  get; set; }
        public string ParameterIntro { get; set; }
        public bool IsRequired { get; set; }
        public string DefaultValue { get; set; }
        public string ParameterValue { get; set; }
    }
}
