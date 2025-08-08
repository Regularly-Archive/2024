using DocumentFormat.OpenXml.Office.CustomUI;

namespace PostgreSQL.Embedding.Utils
{
    public static class EnumerableExtensions
    {
        public static void ForEach<T>(this IEnumerable<T> source, Action<T, int> action)
        {
            foreach (var (item, index) in source.Select((item, index) => (item, index)))
            {
                action?.Invoke(item, index);
            }
        }
    }
}
