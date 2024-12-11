using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.LLama;
using System.Text;
using System.Web;
using static LLama.Common.ChatHistory;

namespace PostgreSQL.Embedding.LLmServices.Extensions
{
    public static class HttpContextExtensions
    {
        public static string GetOrCreateConversationId(this Microsoft.AspNetCore.Http.HttpContext context)
        {
            var conversationId = context.Request.Headers[Constants.HttpRequestHeader_ConversationId].FirstOrDefault();
            if (string.IsNullOrEmpty(conversationId)) conversationId = Guid.NewGuid().ToString();

            return conversationId;
        }

        public static string GetConversationName(this Microsoft.AspNetCore.Http.HttpContext context)
        {
            var conversationName = context.Request.Headers[Constants.HttpRequestHeader_ConversationName].FirstOrDefault();
            if (string.IsNullOrEmpty(conversationName)) return "未命名会话";

            return HttpUtility.UrlDecode(conversationName);
        }

        public static bool GetConversationFlag(this Microsoft.AspNetCore.Http.HttpContext context)
        {
            var conversationFlag = context.Request.Headers[Constants.HttpRequestHeader_ConversationFlag].FirstOrDefault();
            if (string.IsNullOrEmpty(conversationFlag)) return false;

            return conversationFlag == "Y";
        }

        public static async Task WriteEmbedding(this Microsoft.AspNetCore.Http.HttpContext context, List<float> embedding)
        {
            var result = new OpenAICompatibleEmbeddingResult();
            result.Data = new List<OpenAICompatibleEmbeddingDataModel>()
            {
                new OpenAICompatibleEmbeddingDataModel() { Index = 0, Embedding = embedding }
            };
            if (!context.Response.HasStarted) context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonConvert.SerializeObject(result));
            await context.Response.CompleteAsync();
        }

        public static async Task WriteCompletion(this Microsoft.AspNetCore.Http.HttpContext context, string text)
        {
            var result = new OpenAICompatibleCompletionResult();
            result.Choices = new List<OpenAICompatibleCompletionChoiceModel>()
            {
                new OpenAICompatibleCompletionChoiceModel() { Index = 0, Text = text }
            };

            if (!context.Response.HasStarted) context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonConvert.SerializeObject(result));
            await context.Response.CompleteAsync();
        }

        public static async Task WriteChatCompletion(this Microsoft.AspNetCore.Http.HttpContext context, string text, long? mesageId = null)
        {
            var result = new OpenAICompatibleResult() { id = mesageId.HasValue ? mesageId.ToString() : Guid.NewGuid().ToString() };
            result.Choices = new List<OpenAICompatibleChoicesModel>()
            {
                new OpenAICompatibleChoicesModel() { index = 0, message = new OpenAIMessage() { role = "assistant", content = text } },
            };

            if (!context.Response.HasStarted) context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonConvert.SerializeObject(result));
            await context.Response.CompleteAsync();
        }

        public static async Task WriteStreamingChatCompletion(this Microsoft.AspNetCore.Http.HttpContext context, IAsyncEnumerable<string> texts, long? mesageId = null, CancellationToken cancellationToken = default)
        {
            var result = new OpenAIStreamResult() { id = mesageId.HasValue ? mesageId.ToString() : Guid.NewGuid().ToString() };
            result.choices = new List<StreamChoicesModel>()
            {
                new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } }
            };

            await foreach (var text in texts)
            {
                if (cancellationToken.IsCancellationRequested) { return; }

                await Task.Delay(100);

                result.created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                result.choices[0].delta.content = text == null ? string.Empty : Convert.ToString(text);
                string message = $"data: {JsonConvert.SerializeObject(result)}\n\n";
                await context.Response.WriteAsync(message, Encoding.UTF8, cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
            await context.Response.WriteAsync("data: [DONE]", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
            await context.Response.CompleteAsync();
        }

        public static async Task WriteStreamingChatCompletion(this Microsoft.AspNetCore.Http.HttpContext context, IAsyncEnumerable<StreamingChatMessageContent> texts, long? mesageId = null, CancellationToken cancellationToken = default)
        {
            var result = new OpenAIStreamResult() { id = mesageId.HasValue ? mesageId.ToString() : Guid.NewGuid().ToString() };
            result.choices = new List<StreamChoicesModel>()
            {
                new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } }
            };

            await foreach (var text in texts)
            {
                if (cancellationToken.IsCancellationRequested) { return; }

                await Task.Delay(100);

                result.created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                result.choices[0].delta.content = text.Content ?? string.Empty;
                string message = $"data: {JsonConvert.SerializeObject(result)}\n\n";
                await context.Response.WriteAsync(message, Encoding.UTF8, cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
            await context.Response.WriteAsync("data: [DONE]", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
            await context.Response.CompleteAsync();
        }

        public static async Task WriteStreamingChatCompletion(this Microsoft.AspNetCore.Http.HttpContext context, string data, long? mesageId = null, CancellationToken cancellationToken = default)
        {
            var texts = data.ToArray().Select(x => x.ToString()).ToAsyncEnumerable();

            var result = new OpenAIStreamResult() { id = mesageId.HasValue ? mesageId.ToString() : Guid.NewGuid().ToString() };
            result.choices = new List<StreamChoicesModel>()
            {
                new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } }
            };

            await foreach (var text in texts)
            {
                if (cancellationToken.IsCancellationRequested) { return; }

                await Task.Delay(100);

                result.choices[0].delta.content = text == null ? string.Empty : Convert.ToString(text);
                result.created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string message = $"data: {JsonConvert.SerializeObject(result)}\n\n";
                await context.Response.WriteAsync(message, Encoding.UTF8);
                await context.Response.Body.FlushAsync();
            }
            await context.Response.WriteAsync("data: [DONE]");
            await context.Response.Body.FlushAsync();
            await context.Response.CompleteAsync();
        }
    }
}
