namespace PostgreSQL.Embedding.DataAccess.Entities
{
    public class BaseEntity
    {
        public virtual long Id { get; set; }
        public virtual DateTime CratedAt { get; set; }

        public virtual string CratedBy { get; set; }

        public virtual DateTime UpdatedAt { get; set; }

        public virtual string UpdatedBy { get; set; }
    }
}
