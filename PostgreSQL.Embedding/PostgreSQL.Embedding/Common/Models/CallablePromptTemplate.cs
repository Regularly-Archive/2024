using DocumentFormat.OpenXml.Wordprocessing;
using HandlebarsDotNet;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System;

namespace PostgreSQL.Embedding.Common.Models
{
    public class CallablePromptTemplate
    {
        public string Template { get; private set; }
        private Dictionary<string, object> _arguments = new Dictionary<string, object>();

        public CallablePromptTemplate(string template)
        {
            Template = template;
        }

        public void AddVariable(string key, object value) => _arguments[key] = value;
        public void AddEnvironmentVariable(string key) => _arguments[key] = Environment.GetEnvironmentVariable(key);

        public Task<FunctionResult> InvokeAsync(Kernel kernel, OpenAIPromptExecutionSettings executionSettings = null)
        {
            var kernelFunction = kernel.CreateFunctionFromPrompt(Template, executionSettings);
            return kernel.InvokeAsync(kernelFunction, new KernelArguments(_arguments));
        }

        public Task<T> InvokeAsync<T>(Kernel kernel, OpenAIPromptExecutionSettings executionSettings = null)
        {
            var kernelFunction = kernel.CreateFunctionFromPrompt(Template, executionSettings);
            return kernel.InvokeAsync<T>(kernelFunction, new KernelArguments(_arguments));
        }

        public IAsyncEnumerable<StreamingChatMessageContent> InvokeStreamingAsync(Kernel kernel, OpenAIPromptExecutionSettings executionSettings = null)
        {
            var kernelFunction = kernel.CreateFunctionFromPrompt(Template, executionSettings);
            return kernel.InvokeStreamingAsync<StreamingChatMessageContent>(kernelFunction, new KernelArguments(_arguments));
        }

        public IAsyncEnumerable<T> InvokeStreamingAsync<T>(Kernel kernel, OpenAIPromptExecutionSettings executionSettings = null)
        {
            var kernelFunction = kernel.CreateFunctionFromPrompt(Template, executionSettings);
            return kernel.InvokeStreamingAsync<T>(kernelFunction, new KernelArguments(_arguments));
        }
    }
}
