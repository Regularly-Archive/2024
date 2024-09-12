namespace PostgreSQL.Embedding.Common.Models.Plugin
{
    public class LlmPluginModel
    {
        /// <summary>
        /// 插件Id
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 插件名称
        /// </summary>
        public string PluginName { get; set; }

        /// <summary>
        /// 插件说明
        /// </summary>
        public string PluginIntro {  get; set; }

        /// <summary>
        /// 类型名称
        /// </summary>
        public string TypeName { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// 版本号
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 函数列表
        /// </summary>
        public List<LlmPluginFunctionModel> Functions { get; set; } = new List<LlmPluginFunctionModel>();

        /// <summary>
        /// 参数列表
        /// </summary>
        public List<LlmPluginParameterModel> Parameters { get; set; } = new List<LlmPluginParameterModel>();
    }
}
