using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.Utils
{
    public static class AsyncEnumerableExtensions
    {
        public static IAsyncEnumerable<StreamingChatMessageContent> AsStreamming(this string content)
        {
            var streamingChatContents = content.ToArray().Select(x => new StreamingChatMessageContent(AuthorRole.Assistant, x.ToString())).ToList();
            return new AsyncEnumerable<StreamingChatMessageContent>(streamingChatContents);
        }

        public static FunctionResult AsFunctionResult(this string content)
        {
            return new FunctionResult(null, content);
        }
    }
}
