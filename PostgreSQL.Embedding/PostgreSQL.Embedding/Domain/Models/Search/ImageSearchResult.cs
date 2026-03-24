namespace PostgreSQL.Embedding.Domain.Models.Search
{
    public class ImageSearchResult
    {
        public string Keyword { get; set; }
        public List<ImageEntry> Entries { get; set; } = new List<ImageEntry>();
    }

    public class ImageEntry
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
    }
}
