using PostgreSQL.Embedding.Common.Models.KernelMemory;

namespace PostgreSQL.Embedding.Common.Models.RAG
{
    public class LlmCitationModel
    {
        public int Index { get; set; }
        public string FileName { get; set; }
        public float Relevance { get; set; }
        public string Text { get; set; }
        public string Url {  get; set; }

        public static LlmCitationModel FromKnowledgeBase(int index, KMPartition partition)
        {
            return new LlmCitationModel()
            {
                Index = index,
                FileName = partition.FileName,
                Relevance = partition.Relevance,
                Text = $"[^{index}]: {partition.Text}",
                Url = $"/api/KnowledgeBase/{partition.KnowledgeBaseId}/chunks/{partition.FileId}/{partition.PartId}"
            };
        }

        public static LlmCitationModel FromSearchEngine(int index, string url, string title, string description)
        {
            return new LlmCitationModel()
            {
                Index = index,
                Text = description,
                Url = url,
            };
        }
    }
}
