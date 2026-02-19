using PostgreSQL.Embedding.Domain.Models.KernelMemory;
using PostgreSQL.Embedding.Domain.Models.Search;
using System.Net;

namespace PostgreSQL.Embedding.Domain.Models.RAG
{
    public class LlmCitationModel
    {
        public int Index { get; set; }
        public string FileName { get; set; }
        public float Relevance { get; set; }
        public string Text { get; set; }
        public string Url { get; set; }
        public string Type { get; set; }

        public static LlmCitationModel FromKnowledgeBase(int index, KMPartition partition)
        {
            return new LlmCitationModel()
            {
                Index = index,
                FileName = partition.FileName,
                Relevance = partition.Relevance,
                Text = $"[^{index}]: {partition.Text}",
                Url = $"/api/KnowledgeBase/{partition.KnowledgeBaseId}/chunks/{partition.FileId}/{partition.PartId}?relevance={partition.Relevance}",
                Type = "document"
            };
        }

        public static LlmCitationModel FromSearchEngine(int index, Entry entry)
        {
            return new LlmCitationModel()
            {
                Index = index,
                Text = entry.Snippet,
                Url = entry.Url,
                Relevance = entry.Relevance,
                Type = "website"
            };
        }
    }
}
