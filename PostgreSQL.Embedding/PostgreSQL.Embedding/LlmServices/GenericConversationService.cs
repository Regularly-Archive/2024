using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using System.Text;

namespace PostgreSQL.Embedding.LlmServices
{
    public class GenericConversationService
    {
        private readonly Kernel _kernel;
        private readonly LlmApp _app;
        public GenericConversationService(Kernel kernel, LlmApp app)
        {
            _kernel = kernel;
            _app = app;
        }

        public async Task HandleChat(OpenAIModel model, HttpContext HttpContext, string input)
        {
            if (model.stream)
            {
                var stramingResult = new OpenAIStreamResult();
                stramingResult.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                stramingResult.choices = new List<StreamChoicesModel>() { new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } } };
                await HandleStramingChat(HttpContext, stramingResult, input);
                HttpContext.Response.ContentType = "application/json";
                await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(stramingResult));
                await HttpContext.Response.CompleteAsync();
                return;
            }
            else
            {
                var result = new OpenAIResult();
                result.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                result.choices = new List<ChoicesModel>() { new ChoicesModel() { message = new OpenAIMessage() { role = "assistant" } } };
                result.choices[0].message.content = await HandleNormalChat(input);
                HttpContext.Response.ContentType = "application/json";
                await HttpContext.Response.WriteAsync(JsonConvert.SerializeObject(result));
                await HttpContext.Response.CompleteAsync();
            }
        }

        private async Task HandleStramingChat(HttpContext HttpContext, OpenAIStreamResult result, string input)
        {
            HttpContext.Response.Headers.Add("Content-Type", "text/event-stream");

            if (string.IsNullOrEmpty(_app.Prompt) || !_app.Prompt.Contains("{{$input}}"))
            {
                _app.Prompt = _app.Prompt + "{{$input}}";
            }

            var temperature = _app.Temperature / 100;
            OpenAIPromptExecutionSettings settings = new() { Temperature = (double)temperature };
            var func = _kernel.CreateFunctionFromPrompt(_app.Prompt, settings);
            var chatResult = _kernel.InvokeStreamingAsync<StreamingChatMessageContent>(function: func, arguments: new KernelArguments() { ["input"] = input });

            await foreach (var content in chatResult)
            {
                result.choices[0].delta.content = content.Content ?? string.Empty;
                string message = $"data: {JsonConvert.SerializeObject(result)}\n\n";
                await HttpContext.Response.WriteAsync(message, Encoding.UTF8);
                await HttpContext.Response.Body.FlushAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            await HttpContext.Response.WriteAsync("data: [DONE]");
            await HttpContext.Response.Body.FlushAsync();

            await HttpContext.Response.CompleteAsync();
        }

        private async Task<string> HandleNormalChat(string input)
        {
            string result = "";
            if (string.IsNullOrEmpty(_app.Prompt) || !_app.Prompt.Contains("{{$input}}"))
            {
                _app.Prompt = _app.Prompt + "{{$input}}";
            }

            var temperature = _app.Temperature / 100;
            var settings = new OpenAIPromptExecutionSettings() { Temperature = (double)temperature };
            var func = _kernel.CreateFunctionFromPrompt(_app.Prompt, settings);
            var chatResult = await _kernel.InvokeAsync(function: func, arguments: new KernelArguments() { ["input"] = input });
            var answers = chatResult.GetValue<string>();
            if (!string.IsNullOrEmpty(answers)) return answers;
            return result;
        }
    }
}
