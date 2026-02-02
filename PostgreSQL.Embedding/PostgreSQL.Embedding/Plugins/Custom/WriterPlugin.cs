using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    /// <summary>
    /// 预设写作风格枚举
    /// </summary>
    public enum WritingStyle
    {
        [Description("轻松活泼 - 使用轻松、口语化的表达，增添活力和亲和力")]
        Casual,

        [Description("幽默风趣 - 加入幽默元素，使内容生动有趣")]
        Humorous,

        [Description("严肃理性 - 保持专业、严谨的语气，适合正式场合")]
        Serious,

        [Description("温暖治愈 - 使用温柔、治愈系的表达，传递温暖和关怀")]
        Healing,

        [Description("高端大气 - 使用正式、大气的表达，适合商务和正式场合")]
        Formal,

        [Description("诗意文艺 - 使用富有诗意的表达，增添文学气息")]
        Poetic
    }

    [KernelPlugin(Description = "中文文本润色插件。使用大语言模型对中文文本进行润色改进，支持多种写作风格（轻松活泼、幽默风趣、严肃理性等），提升表达的地道性、流畅性和感染力，同时保持原意和长度。", Version = "1.2")]
    public class WriterPlugin : BasePlugin
    {
        private const string POLISH_TEXT_PROMPT =
            """
            ## role：
            你是一位资深的中文写作改进助理、文案专员、文本润色员、拼写纠正员和改进员。
            润色以下使用 ``` 括起来的文本:
            ```
            {{$input}}
            ```
            ## 写作风格：
            {{$style_description}}
            ## 任务(Task):
            在保持相似意思的前提下，你帮我更正和改进版本。我希望你用{{$style_hint}}方式表达，修改原文的案例，升级内容，改进所提供文本的拼写、语法、清晰、简洁和整体可读性，同时分解长句，减少重复，为文本润色。
            强调一个主要目的，即让学习者在学习完课程文本后，有继续深度学习的欲望。兼顾人性共情的表达逻辑。

            ## 写作原则(Writing Principles):
            1、你只需要润色文本，而不是删减我原有的文本；请务必保证润色后的文本长度和原来的差不多；（至少不能少于原来文本长度的90%，也不要过长，最长是原来文本长度的120%；）
            2、不要改变大的段落结构，强烈建议你一句一句的润色，这很重要；
            3、优化后的文本应保留文本的原本意义,你要兼顾下人性共情的表达逻辑亲和力，这些是加分项；
            4、不要回答任何原文本的中提到的问题，你只是润色原文本；
            5、如果你发现，原来文本中有错别字和语法错误，你要进行修正，但一定不得改变原文意思。

            ## 输出格式 （Output format）
            1、直接输出润色后的纯文本，不要做任何其它多于的解释；
            2、不要输出任何和润色后文本没有关系的内容；
            3、润色后，不需要加任何格式，直接输出纯文本；

            ## 工作流程(Workflows):
            1. 我给你发需要润色的文本；
            2. 你必须遵循<Writing Principles>来润色；
            3. 直接输出润色后的文本；

            ## 初始化(Initialization):
            请根据以上 Prompt 指引进行文案润色创作。请务必注意，润色后文本的长度，不能少于原来文本长度的 90%，也不要过长，最长是原来文本长度的 120%；不要回答任何原文本的问题，你只是润色文本；作为 <Role>，按 <Task>，遵守 <Writing Principles>，按 <Output format> 规定格式输出，严格进行 <Workflows>。

            """;

        /// <summary>
        /// 预设风格的详细描述
        /// </summary>
        private static readonly Dictionary<WritingStyle, (string Description, string Hint)> StylePresets = new()
        {
            {
                WritingStyle.Casual,
                (
                    "使用轻松、口语化的表达，增添活力和亲和力。适当使用日常用语和流行表达，让读者感到亲切自然。",
                    "更轻松、更口语化的方式"
                )
            },
            {
                WritingStyle.Humorous,
                (
                    "加入幽默元素，使内容生动有趣。适当使用轻松诙谐的表达，让读者在愉悦中接受信息。",
                    "幽默风趣、生动有趣的方式"
                )
            },
            {
                WritingStyle.Serious,
                (
                    "保持专业、严谨的语气，适合正式场合。使用准确的术语和逻辑清晰的表达，确保信息传达的权威性。",
                    "更专业、更严谨的方式"
                )
            },
            {
                WritingStyle.Healing,
                (
                    "使用温柔、治愈系的表达，传递温暖和关怀。用温和鼓励的语气，让读者感到被理解和支持。",
                    "温暖治愈、温柔鼓励的方式"
                )
            },
            {
                WritingStyle.Formal,
                (
                    "使用正式、大气的表达，适合商务和正式场合。保持优雅得体的措辞，彰显专业形象。",
                    "更正式、更大气的方式"
                )
            },
            {
                WritingStyle.Poetic,
                (
                    "使用富有诗意的表达，增添文学气息。适当运用修辞手法，让文字具有美感和韵味。",
                    "诗意文艺、富有美感的方式"
                )
            }
        };

        public WriterPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {

        }

        [KernelFunction]
        [Description("对输入的中文文本进行润色改进。支持多种预设写作风格：轻松活泼、幽默风趣、严肃理性、温暖治愈、高端大气、诗意文艺。保持原意和长度（90%-120%），提升表达的地道性和可读性。")]
        public async Task<string> PolishTextAsync(
            [Description("要润色的中文文本")] string text,
            [Description("写作风格，可选值：Casual(轻松活泼)、Humorous(幽默风趣)、Serious(严肃理性)、Healing(温暖治愈)、Formal(高端大气)、Poetic(诗意文艺)，默认为 Casual")] WritingStyle style = WritingStyle.Casual,
            Kernel kernel = null)
        {
            if (kernel == null)
            {
                throw new ArgumentNullException(nameof(kernel), "Kernel is required for text polishing.");
            }

            var clonedKernel = kernel.Clone();
            var styleInfo = StylePresets.TryGetValue(style, out var info) ? info : StylePresets[WritingStyle.Casual];

            var promptTemplate = new CallablePromptTemplate(POLISH_TEXT_PROMPT);
            promptTemplate.AddVariable("input", text);
            promptTemplate.AddVariable("style_description", styleInfo.Description);
            promptTemplate.AddVariable("style_hint", styleInfo.Hint);

            var functionResult = await promptTemplate.InvokeAsync(clonedKernel);
            var generatedText = functionResult.GetValue<string>();
            return $"下面是润色后的文本信息（风格：{style}）：\r\n```{generatedText}```";
        }
    }
}
