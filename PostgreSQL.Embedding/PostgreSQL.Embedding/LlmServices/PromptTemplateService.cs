using Microsoft.SemanticKernel;

namespace PostgreSQL.Embedding.LlmServices
{
    public class PromptTemplateService
    {
        public string LoadPromptTemplate(string promptTemplateName)
        {
            var promptDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Common/Prompts");
            var promptTemplate = Path.Combine(promptDirectory, promptTemplateName);
            if (!File.Exists(promptTemplate))
                throw new ArgumentException($"The prompt template file '{promptTemplate}' can not be found.");

            return File.ReadAllText(promptTemplate);
        }
    }
}
