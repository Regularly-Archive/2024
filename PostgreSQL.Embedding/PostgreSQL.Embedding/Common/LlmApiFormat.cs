 using System.ComponentModel;

namespace PostgreSQL.Embedding.Common
{
    /// <summary>
    /// LLM API 格式类型
    /// </summary>
    public enum LlmApiFormat
    {
        /// <summary>
        /// OpenAI 兼容格式（默认）
        /// </summary>
        [Description("OpenAI 兼容格式")]
        OpenAI = 0,

        /// <summary>
        /// Anthropic 格式
        /// </summary>
        [Description("Anthropic 格式")]
        Anthropic = 1,
    }
}
