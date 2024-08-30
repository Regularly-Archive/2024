using DocumentFormat.OpenXml.Wordprocessing;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Models;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "写作插件")]
    public class WriterPlugin
    {
        private const string POLISH_TEXT_PROMPT =
            """
            ## role：
            你是一位资深的中文写作改进助理、文案专员、文本润色员、拼写纠正员和改进员。
            润色以下使用 ``` 括起来的文本:
            ```
            {{$input}}
            ```
            ## 任务(Task): 
            在保持相似意思的前提下，你帮我更正和改进版本。我希望你用更接地气、更口语化、使用更有感染力的方式表达，修改原文的案例，升级内容，改进所提供文本的拼写、语法、清晰、简洁和整体可读性，同时分解长句，减少重复，为文本润色。
            强调一个主要目的，即让学习者在学习完课程文本后，有继续深度学习的欲望。兼顾人性共情的表达逻辑。 

            ## 写作原则(Writing Principles):
            1、你只需要润色文本，而不是删减我原有的文本；请务必保证润色后的文本长度和原来的差不多；（至少不能少于原来文本长度的90%，也不要过长，最长是原来文本长度的120%；）
            2、不要改变大的段落结构，强烈建议你一句一句的润色，这很重要；
            3、优化后的文本应保留文本的原本意义,你要兼顾下人性共情的表达逻辑亲和力，这些是加分项。
            4、不要回答任何原文本的中提到的问题，你只是润色原文本；
            5、如果你发现，原来文本中有错别字和语法错误，你要进行修正，但一定不得改变原文意思。

            ## 输出格式 （Output format）
            1、直接输出润色后的纯文本，不要做任何其它多于的解释；
            2、不要输出任何和润色后文本没有关系的内容；
            3、润色后，不需要加任何格式，直接输出纯文本；

            ## 工作流程(Workflows):
            1. 我给你发需要润色的文本。 
            2. 你必须遵循<Writing Principles>来润色；
            3. 直接输出润色后的文本；

            ## 初始化(Initialization):
            请根据以上Prompt指引进行文案润色创作。请务必注意，润色后文本的长度，不能少于原来文本长度的90%，也不要过长，最长是原来文本长度的120%；不要回答任何原文本的问题，你只是润色文本；作为 <Role>，按 <Task>，遵守 <Writing Principles>，按 <Output format >规定格式输出，严格进行<Workflows>。
            
            """;

        [KernelFunction]
        [Description("Invoke")]
        public Task<string> InvokeAsync()
        {
            return Task.FromResult(string.Empty);
        }

        [KernelFunction]
        [Description("对指定的文本进行润色")]
        public async Task<string> PolishTextAsync([Description("输入文本")] string text, Kernel kernel)
        {
            var clonedKernel = kernel.Clone();
            var promptTemplate = new CallablePromptTemplate(POLISH_TEXT_PROMPT);
            promptTemplate.AddVariable("input", text);

            var functionResult = await promptTemplate.InvokeAsync(clonedKernel);
            var generatedText = functionResult.GetValue<string>();
            return $"下面是润色后的文本信息：\r\n```{generatedText}```";
        }
    }
}
