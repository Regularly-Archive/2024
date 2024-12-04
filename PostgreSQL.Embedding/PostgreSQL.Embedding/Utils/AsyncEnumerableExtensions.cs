using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using PostgreSQL.Embedding.Common.Models;

namespace PostgreSQL.Embedding.Utils
{
    public static class AsyncEnumerableExtensions
    {
        public static async IAsyncEnumerable<StreamingChatMessageContent> AsStreaming(this string content, int minLength = 1, int maxLength = 5)
        {
            var streamingChatContents = SplitString(content, minLength, maxLength).Select(x => new StreamingChatMessageContent(AuthorRole.Assistant, x)).ToList();
            foreach (var chatContent in streamingChatContents)
            {
                yield return chatContent;
            }
        }

        public static async IAsyncEnumerable<string> AsStreamingTexts(this string content, int minLength = 1, int maxLength = 5)
        {
            var chatContents = SplitString(content, minLength, maxLength).ToList();
            foreach (var chatContent in chatContents)
            {
                yield return chatContent;
            }
        }

        public static FunctionResult AsFunctionResult(this string content) => new FunctionResult(null, content);

        private static string[] SplitString(string s, int minLength, int MaxLength)
        {
            var rand = new Random();

            if (string.IsNullOrEmpty(s))
                return new string[] { s }; 

            string[] subStrings = new string[0];
            int start = 0;

            while (start < s.Length)
            {
                int end = start + rand.Next(minLength, Math.Min(MaxLength, s.Length - start) + 1);
                subStrings = AddToSubStringsArray(subStrings, s.Substring(start, end - start));
                start = end;
            }

            return subStrings;
        }

        private static string[] AddToSubStringsArray(string[] subStrings, string newSubString)
        {
            string[] temp = new string[subStrings.Length + 1];
            subStrings.CopyTo(temp, 0);
            temp[temp.Length - 1] = newSubString;
            return temp;
        }
    }
}
