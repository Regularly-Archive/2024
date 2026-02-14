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
        public async Task<string> GenerateStaticPage(
             Kernel kernel,
            [Description("用户对页面的功能、样式和内容需求描述")] string query,
            [Description("风格预设，可取值：minimalist, glassmorphism, corporate, playful, darkmode, retro, cyberpunk, nature")] string style = "minimalist",
            [Description("颜色预设，可取值：ocean, forest, sunset, monochrome, berry, random")] string color = "random",
            [Description("用途预设，可取值：landing, dashboard, form, portfolio, documentation, tool, blog, ecommerce, other")] string purpose = "other",
            [Description("交互级别，可取值：static, dynamic, rich")] string interactionLevel = "static",
            [Description("技术约束，可取值：vanilla-only, include-icons, include-charts, include-maps, pwa-ready, seo-optimized, i18n-ready")] string techConstraints = "vanilla-only"
            )
        {
            var clonedKernel = kernel.Clone();
            var promptTemplate = _promptTemplateService.LoadTemplate("StaticPages.txt");
            promptTemplate.AddVariable("query", query);
            promptTemplate.AddVariable("style_preset", style);
            promptTemplate.AddVariable("color_scheme", color);
            promptTemplate.AddVariable("page_purpose", purpose);
            promptTemplate.AddVariable("interaction_level", interactionLevel);
            promptTemplate.AddVariable("tech_constraints", techConstraints);

            var code = await promptTemplate.InvokeAsync<string>(clonedKernel);
            code = code.Replace("```html", "").Replace("```", "").Trim();
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
            return code;
        }
    }

}
