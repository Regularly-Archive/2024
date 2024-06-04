using Microsoft.KernelMemory;

namespace PostgreSQL.Embedding.Common.Models.KernelMemory
{
    public class KMPartition
    {
        public string Text { get; private set; }
        public float Relevance { get; private set; }
        public int PartitionNumber { get; private set; }
        public int SectionNumber { get; private set; }
        public string DocumentId { get; private set; }
        public string TaskId { get; private set; }
        public string FileName { get; private set; }
        public string KnowledgeBaseId { get; private set; }

        public KMPartition(Microsoft.KernelMemory.Citation.Partition partition)
        {
            this.Text = partition.Text;
            this.Relevance = partition.Relevance;
            this.PartitionNumber = partition.PartitionNumber;
            this.SectionNumber = partition.SectionNumber;
            this.DocumentId = GetTagValue(partition, KernelMemoryTags.DocumentId);
            this.FileName = GetTagValue(partition, KernelMemoryTags.FileName);
            this.TaskId = GetTagValue(partition, KernelMemoryTags.TaskId);
            this.KnowledgeBaseId = GetTagValue(partition, KernelMemoryTags.KnowledgeBaseId);
        }

        private string GetTagValue(Citation.Partition partition, string key)
        {
            if (partition.Tags == null) return null;
            if (!partition.Tags.ContainsKey(key)) return null;

            var val = partition.Tags[key];
            if (val == null || !val.Any()) return null;

            return val.FirstOrDefault();
        }

        public void SetRelevance(float relevance)
        {
            Relevance = relevance;
        }
    }
}
