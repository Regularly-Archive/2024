using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "使用自然语言查询 MongoDB 的插件")]
    public class Text2MongoDBPlugin
    {
        [KernelFunction]
        public Task<string> InvokeAsync(string text)
        {
            return Task.FromResult(text);
        }
    }
}
