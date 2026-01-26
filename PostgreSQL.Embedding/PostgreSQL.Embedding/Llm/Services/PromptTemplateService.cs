using System.Reflection;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Domain.Models;


namespace PostgreSQL.Embedding.Llm.Services
{
    public class PromptTemplateService
    {
        private readonly KernelPromptTemplateFactory _templateFactory = new();
        private readonly Assembly _assembly = Assembly.GetExecutingAssembly();
        private const string ResourceBase = "PostgreSQL.Embedding.Common.Prompts.";

        public PromptTemplateService()
        {
        }

        private string GetResourceName(string promptTemplateName)
        {
            return $"{ResourceBase}{promptTemplateName}";
        }

        public CallablePromptTemplate LoadTemplate(string promptTemplateName)
        {
            var resourceName = GetResourceName(promptTemplateName);
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new ArgumentException($"The prompt template file '{promptTemplateName}' can not be found.");

            using var reader = new StreamReader(stream);
            return new CallablePromptTemplate(reader.ReadToEnd());
        }

        public Task<string> RenderTemplateAsync(string promptTemplateName, Kernel kernel, KernelArguments arguments)
        {
            var resourceName = GetResourceName(promptTemplateName);
            using var stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new ArgumentException($"The prompt template file '{promptTemplateName}' can not be found.");

            using var reader = new StreamReader(stream);
            var promptContent = reader.ReadToEnd();
            var promptTemplateConfig = new PromptTemplateConfig(promptContent);
            var kernelPromptTemplate = _templateFactory.Create(promptTemplateConfig);
            return kernelPromptTemplate.RenderAsync(kernel, arguments);
        }
    }
}
