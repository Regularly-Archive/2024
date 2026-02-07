using DocumentFormat.OpenXml.Wordprocessing;
using HandlebarsDotNet;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System;
using System.Reflection;

namespace PostgreSQL.Embedding.Domain.Models
{
    public class CallablePromptTemplate
    {
        public string Template { get; private set; }
        private Dictionary<string, object> _arguments = new Dictionary<string, object>();
        private readonly KernelPromptTemplateFactory _templateFactory = new KernelPromptTemplateFactory();
        public string FunctionName { get; set; }
        public string PluginName { get; set; }

        public CallablePromptTemplate(string template)
        {
            Template = template;
        }

        public void AddVariable(string key, object value) => _arguments[key] = value;
        public void AddEnvironmentVariable(string key) => _arguments[key] = Environment.GetEnvironmentVariable(key);

        public Task<FunctionResult> InvokeAsync(Kernel kernel, OpenAIPromptExecutionSettings executionSettings = null, CancellationToken cancellationToken = default)
        {
            var kernelFunction = kernel.CreateFunctionFromPrompt(Template, executionSettings, functionName: FunctionName);
            return kernel.InvokeAsync(kernelFunction, new KernelArguments(_arguments), cancellationToken);
        }

        public Task<T> InvokeAsync<T>(Kernel kernel, OpenAIPromptExecutionSettings executionSettings = null, CancellationToken cancellationToken = default)
        {
            var kernelFunction = kernel.CreateFunctionFromPrompt(Template, executionSettings, functionName: FunctionName);
            SetPluginName(kernelFunction, PluginName);
            return kernel.InvokeAsync<T>(kernelFunction, new KernelArguments(_arguments), cancellationToken);
        }

        public IAsyncEnumerable<StreamingChatMessageContent> InvokeStreamingAsync(Kernel kernel, OpenAIPromptExecutionSettings executionSettings = null, CancellationToken cancellationToken = default)
        {
            var kernelFunction = kernel.CreateFunctionFromPrompt(Template, executionSettings, functionName: FunctionName);
            SetPluginName(kernelFunction, PluginName);
            return kernel.InvokeStreamingAsync<StreamingChatMessageContent>(kernelFunction, new KernelArguments(_arguments), cancellationToken);
        }

        public IAsyncEnumerable<T> InvokeStreamingAsync<T>(Kernel kernel, OpenAIPromptExecutionSettings executionSettings = null, CancellationToken cancellationToken = default)
        {
            var kernelFunction = kernel.CreateFunctionFromPrompt(Template, executionSettings, functionName: FunctionName);
            SetPluginName(kernelFunction, PluginName);
            return kernel.InvokeStreamingAsync<T>(kernelFunction, new KernelArguments(_arguments), cancellationToken);
        }

        public Task<string> RenderTemplateAsync(Kernel kernel, OpenAIPromptExecutionSettings executionSettings = null, CancellationToken cancellationToken = default)
        {
            var promptTemplateConfig = new PromptTemplateConfig(Template);
            var kernelPromptTemplate = _templateFactory.Create(promptTemplateConfig);
            return kernelPromptTemplate.RenderAsync(kernel, new KernelArguments(_arguments), cancellationToken);
        }

        private void SetPluginName(KernelFunction kernelFunction, string pluginName)
        {
            var kernelFunctionMetadata = kernelFunction.Metadata;
            kernelFunctionMetadata.PluginName = pluginName;

            SetInitProperty(kernelFunction, nameof(kernelFunction.Metadata), kernelFunctionMetadata);
        }

        private void SetInitProperty(object obj, string propertyName, object value)
        {
            var type = obj.GetType();

            var setter = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name.Contains($"<{propertyName}>i__set"));

            if (setter == null)
            {
                setter = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name.StartsWith($"<{propertyName}>"));
            }

            if (setter == null)
            {
                var property = type.GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                setter = property?.GetSetMethod(nonPublic: true);
            }

            setter?.Invoke(obj, new[] { value });
        }
    }
}
