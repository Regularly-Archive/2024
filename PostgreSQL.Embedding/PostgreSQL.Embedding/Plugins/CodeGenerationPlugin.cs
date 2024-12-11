using Microsoft.SemanticKernel;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "一个用于生成前端代码的插件")]
    public class CodeGenerationPlugin : BasePlugin
    {
        private PromptTemplateService _promptTemplateService;
        public CodeGenerationPlugin(IServiceProvider serviceProvider) 
            : base(serviceProvider)
        {
            _promptTemplateService = serviceProvider.GetService<PromptTemplateService>();
        }

        [KernelFunction]
        [Description("生成由 HTML、JavaScript 和 CSS 组成的静态页面")]
        public async Task<string> GenerateStaticPage([Description("用户请求")] string query, Kernel kernel)
        {
            var clonedKernel = kernel.Clone();
            var promptTemplate = _promptTemplateService.LoadTemplate("StaticPages.txt");
            promptTemplate.AddVariable("query", query);

            var code = await promptTemplate.InvokeAsync<string>(clonedKernel);
            code = code.Replace("```html", "").Replace("```", "").Trim();
            await SendArtifacts(code, "vanilla");
            return code;
        }

        [KernelFunction]
        [Description("生成 Vue 单文件组件")]
        public async Task<string> GenerateVueComponent([Description("用户请求")] string query, Kernel kernel)
        {
            var clonedKernel = kernel.Clone();
            var promptTemplate = _promptTemplateService.LoadTemplate("VueComponent.txt");
            promptTemplate.AddVariable("query", query);

            var code = await promptTemplate.InvokeAsync<string>(clonedKernel);
            code = code.Replace("```vue", "").Replace("```", "").Trim();
            await SendArtifacts(code, "vue");
            return code;
        }

        private async Task SendArtifacts(string code, string renderer)
        {
            var payload = new { sourceCode = code, renderer = renderer };
            var artifacts = new LlmArtifactResponseModel("代码预览", ArtifactType.CodePreview);
            artifacts.SetData(payload);
            await EmitArtifactsAsync(artifacts);
        }
    }

}
