using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    [KernelPlugin(Description = "根据自然语言描述生成前端代码（HTML、CSS、JavaScript 或 Vue 组件）。生成的代码会通过 Artifacts 事件发送以便在前端预览。", Version = "1.1")]
    public class CodeGenerationPlugin : BasePlugin
    {
        private PromptTemplateService _promptTemplateService;
        public CodeGenerationPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _promptTemplateService = serviceProvider.GetService<PromptTemplateService>();
        }

        [KernelFunction]
        [Description("根据用户需求生成包含 HTML、CSS 和 JavaScript 的完整静态网页代码")]
        public async Task<string> GenerateStaticPage([Description("用户对页面的功能、样式和内容需求描述")] string query, Kernel kernel)
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
        [Description("根据用户需求生成 Vue 3 单文件组件（.vue 格式），包含模板、脚本和样式")]
        public async Task<string> GenerateVueComponent([Description("用户对组件的功能、UI 和交互需求描述")] string query, Kernel kernel)
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
