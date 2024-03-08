using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using PostgreSQL.Embedding.DataAccess.Entities;

namespace PostgreSQL.Embedding.DataAccess
{
    public class VectorsDbContext : DbContext
    {
        public DbSet<EmbeddingItem> Items { get; set; }

        public VectorsDbContext(DbContextOptions<VectorsDbContext> options) : base(options)
        {

        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresExtension("vector");
            modelBuilder.Entity<EmbeddingItem>(x =>
            {
                x.ToTable("text2vec_items");
                x.HasKey("Id");
                x.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                x.Property(e => e.Content).HasColumnName("content").IsRequired().HasMaxLength(1000);
                x.Property(e => e.Embedding).HasColumnName("embedding").IsRequired();

            });

        }

    }
}
