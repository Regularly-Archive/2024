namespace PostgreSQL.Embedding.Domain.Models.File
{
    public class GetFileStorageResult
    {
        public Stream Content { get; set; }
        public string ContentType { get; set; }
        public string FileName { get; set; }
    }
}
