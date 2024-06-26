using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.LlmServices
{
    public class PromptTemplateService
    {
        private readonly KernelPromptTemplateFactory _templateFactory = new KernelPromptTemplateFactory();
        public PromptTemplateService() 
        { 
        }

        public CallablePromptTemplate LoadTemplate(string promptTemplateName)
        {
            var promptDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Common/Prompts");
            var promptTemplate = Path.Combine(promptDirectory, promptTemplateName);
            if (!File.Exists(promptTemplate))
                throw new ArgumentException($"The prompt template file '{promptTemplate}' can not be found.");

            return new CallablePromptTemplate(File.ReadAllText(promptTemplate));
        }

        public Task<string> RenderTemplateAsync(string promptTemplateName, Kernel kernel, KernelArguments arguments)
        {
            var promptDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Common/Prompts");
            var promptTemplate = Path.Combine(promptDirectory, promptTemplateName);
            if (!File.Exists(promptTemplate))
                throw new ArgumentException($"The prompt template file '{promptTemplate}' can not be found.");

            var promptTemplateConfig = new PromptTemplateConfig(File.ReadAllText(promptTemplate));
            var kernelPromptTemplate = _templateFactory.Create(promptTemplateConfig);
            return kernelPromptTemplate.RenderAsync(kernel, arguments);
        }
    }
}
