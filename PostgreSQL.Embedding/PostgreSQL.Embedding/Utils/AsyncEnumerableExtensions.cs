using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.Utils
{
    public static class AsyncEnumerableExtensions
    {
        public static async IAsyncEnumerable<StreamingChatMessageContent> AsStreamming(this string content)
        {
            var streamingChatContents = content.ToArray().Select(x => new StreamingChatMessageContent(AuthorRole.Assistant, x.ToString())).ToList();
            foreach(var chatContent in streamingChatContents)
            {
                yield return chatContent;
            }
        }

        public static FunctionResult AsFunctionResult(this string content) => new FunctionResult(null, content);
    }
}
