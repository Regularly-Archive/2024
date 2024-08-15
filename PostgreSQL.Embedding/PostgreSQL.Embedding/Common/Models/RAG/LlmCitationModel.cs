namespace PostgreSQL.Embedding.Common.Models.RAG
{
    public class LlmCitationModel
    {
        public int Index { get; set; }
        public string FileName { get; set; }
        public float Relevance { get; set; }
        public string Text { get; set; }
        public string Url {  get; set; }
    }
}
