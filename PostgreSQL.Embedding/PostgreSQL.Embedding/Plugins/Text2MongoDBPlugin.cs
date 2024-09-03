using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Plugins.Abstration;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "使用自然语言查询 MongoDB 的插件")]
    public class Text2MongoDBPlugin : BasePlugin
    {
        public Text2MongoDBPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {

        }

        [KernelFunction]
        public Task<string> InvokeAsync(string text)
        {
            return Task.FromResult(text);
        }
    }
}
