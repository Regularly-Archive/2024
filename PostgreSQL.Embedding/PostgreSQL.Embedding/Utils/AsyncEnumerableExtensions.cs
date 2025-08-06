using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

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

        public static string[] SplitString(this string s, int minLength, int maxLength)
        {
            var rand = new Random();

            if (string.IsNullOrEmpty(s))
                return new string[] { s };

            var subStrings = new List<string>();
            var start = 0;

            while (start < s.Length)
            {
                int baseEnd = start + rand.Next(minLength, Math.Min(maxLength, s.Length - start) + 1);
                int end = baseEnd;

                if (end < s.Length && char.IsHighSurrogate(s[end - 1]))
                {
                    end++;
                }

                while (end < s.Length && char.IsLowSurrogate(s[end]) ||
                      (end < s.Length && char.GetUnicodeCategory(s[end]) == System.Globalization.UnicodeCategory.NonSpacingMark))
                {
                    end++;
                }

                subStrings.Add(s.Substring(start, end - start));
                start = end;
            }

            return subStrings.ToArray();
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
