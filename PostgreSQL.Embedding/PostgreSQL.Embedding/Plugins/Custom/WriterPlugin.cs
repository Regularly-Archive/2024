using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    /// <summary>
    /// 预设写作风格
    /// </summary>
    public static class WritingStyles
    {
        public const string Casual = "Casual";
        public const string Humorous = "Humorous";
        public const string Serious = "Serious";
        public const string Healing = "Healing";
        public const string Formal = "Formal";
        public const string Poetic = "Poetic";

        private static readonly Dictionary<string, (string Description, string Hint)> StylePresets = new()
        {
            { Casual, ("使用轻松、口语化的表达，增添活力和亲和力。适当使用日常用语和流行表达，让读者感到亲切自然。", "更轻松、更口语化的方式") },
            { Humorous, ("加入幽默元素，使内容生动有趣。适当使用轻松诙谐的表达，让读者在愉悦中接受信息。", "幽默风趣、生动有趣的方式") },
            { Serious, ("保持专业、严谨的语气，适合正式场合。使用准确的术语和逻辑清晰的表达，确保信息传达的权威性。", "更专业、更严谨的方式") },
            { Healing, ("使用温柔、治愈系的表达，传递温暖和关怀。用温和鼓励的语气，让读者感到被理解和支持。", "温暖治愈、温柔鼓励的方式") },
            { Formal, ("使用正式、大气的表达，适合商务和正式场合。保持优雅得体的措辞，彰显专业形象。", "更正式、更大气的方式") },
            { Poetic, ("使用富有诗意的表达，增添文学气息。适当运用修辞手法，让文字具有美感和韵味。", "诗意文艺、富有美感的方式") }
        };

        public static bool TryGetStyleInfo(string style, out (string Description, string Hint) info)
        {
            if (string.IsNullOrEmpty(style))
            {
                style = Casual;
            }
            return StylePresets.TryGetValue(style, out info);
        }
    }

    [KernelPlugin(Description = "中文文本润色插件。使用大语言模型对中文文本进行润色改进，支持多种写作风格（轻松活泼、幽默风趣、严肃理性等），提升表达的地道性、流畅性和感染力，同时保持原意和长度。", Version = "1.3")]
    public class WriterPlugin : BasePlugin
    {
        private readonly PromptTemplateService _promptTemplateService;

        public WriterPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _promptTemplateService = serviceProvider.GetService<PromptTemplateService>();
        }

        [KernelFunction]
        [Description("对输入的中文文本进行润色改进。支持多种预设写作风格：Casual(轻松活泼)、Humorous(幽默风趣)、Serious(严肃理性)、Healing(温暖治愈)、Formal(高端大气)、Poetic(诗意文艺)，默认为 Casual。保持原意和长度（90%-120%），提升表达的地道性和可读性。")]
        public async Task<string> PolishTextAsync(
            [Description("要润色的中文文本")] string text,
            [Description("写作风格，可选值：Casual、Humorous、Serious、Healing、Formal、Poetic，默认为 Casual")] string style = "Casual",
            Kernel kernel = null)
        {
            if (kernel == null)
            {
                throw new ArgumentNullException(nameof(kernel), "Kernel is required for text polishing.");
            }

            var clonedKernel = kernel.Clone();

            if (string.IsNullOrEmpty(style))
            {
                style = WritingStyles.Casual;
            }

            WritingStyles.TryGetStyleInfo(style, out var styleInfo);

            var promptTemplate = _promptTemplateService.LoadTemplate("WriterPolish.txt");
            promptTemplate.AddVariable("input", text);
            promptTemplate.AddVariable("style_description", styleInfo.Description);
            promptTemplate.AddVariable("style_hint", styleInfo.Hint);
            promptTemplate.PluginName = nameof(WriterPlugin);
            promptTemplate.FunctionName = nameof(PolishTextAsync);

            var functionResult = await promptTemplate.InvokeAsync(clonedKernel);
            
            var generatedText = functionResult.GetValue<string>();
            return $"下面是润色后的文本信息（风格：{style}）：\r\n```{generatedText}```";
        }
    }
}
