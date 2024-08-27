using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "写作插件")]
    public class WriterPlugin
    {
        [KernelFunction]
        [Description("Invoke")]
        public Task<string> InvokeAsync()
        {
            return Task.FromResult(string.Empty);
        }
    }
}
