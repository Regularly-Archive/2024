using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.LlmServices
{
    public class PromptTemplateService
    {
        public CallablePromptTemplate LoadPromptTemplate(string promptTemplateName)
        {
            var promptDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Common/Prompts");
            var promptTemplate = Path.Combine(promptDirectory, promptTemplateName);
            if (!File.Exists(promptTemplate))
                throw new ArgumentException($"The prompt template file '{promptTemplate}' can not be found.");

            return new CallablePromptTemplate(File.ReadAllText(promptTemplate));
        }
    }
}
