﻿using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models;
using PostgreSQL.Embedding.LlmServices.LLama;
using System.Text;
using System.Web;

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

        public static async Task WriteEmbedding(this Microsoft.AspNetCore.Http.HttpContext context, List<float> embedding)
        {
            var result = new OpenAICompatibleEmbeddingResult();
            result.Data.Add(new OpenAICompatibleEmbeddingDataModel() { Index = 0, Embedding = embedding });
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

        public static async Task WriteChatCompletion(this Microsoft.AspNetCore.Http.HttpContext context, string text)
        {
            var result = new OpenAIResult();
            result.choices = new List<ChoicesModel>()
            {
                new ChoicesModel() { index = 0, message = new OpenAIMessage() { role = "assistant", content = text } },
            };

            if (!context.Response.HasStarted) context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonConvert.SerializeObject(result));
            await context.Response.CompleteAsync();
        }

        public static async Task WriteStreamingChatCompletion(this Microsoft.AspNetCore.Http.HttpContext context, IAsyncEnumerable<string> text)
        {
            var result = new OpenAIStreamResult();
            result.choices = new List<StreamChoicesModel>() 
            {
                new StreamChoicesModel() { delta = new OpenAIMessage() { role = "assistant" } } 
            };

            await foreach (var r in text)
            {
                result.choices[0].delta.content = r == null ? string.Empty : Convert.ToString(r);
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
