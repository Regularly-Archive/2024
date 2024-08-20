using System.Text;

namespace PostgreSQL.Embedding.Common.Models.Search
{
    public class SearchResult
    {
        public List<Entry> Entries { get; set; } = new List<Entry>();
        public string Query { get; set; }

        public override string ToString()
        {
            var stringBuilder = new StringBuilder();

            foreach (var entry in Entries)
            {
                stringBuilder.AppendLine($"Url: {entry.Url}");
                stringBuilder.AppendLine($"Title: {entry.Title}");
                stringBuilder.AppendLine($"Description: {entry.Description}");
                stringBuilder.AppendLine();
            }

            return stringBuilder.ToString();
        }
    }

    public class Entry
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
    }
}
