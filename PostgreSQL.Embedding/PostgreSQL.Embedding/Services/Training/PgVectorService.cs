using Azure.Storage.Blobs.Models;
using LLama.Common;
using LLama;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using Microsoft.KernelMemory;
using Pgvector.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Numerics;

namespace PostgreSQL.Embedding.Services.Training
{
    public class PgVectorService
    {
        private readonly VectorsDbContext _dbContext;
        private readonly LLamaEmbedder _embedder;
        private readonly Dictionary<string, Func<Pgvector.Vector, Pgvector.Vector, double>> _methodsRouter
            = new Dictionary<string, Func<Pgvector.Vector, Pgvector.Vector, double>>
            {
                { "L2Distance", (x, y) => x.L2Distance(y) },
                { "MaxInnerProduct", (x, y) => x.MaxInnerProduct(y) },
                { "CosineDistance", (x, y) => x.CosineDistance(y) }
            };

        public PgVectorService(VectorsDbContext dbContext, LLamaEmbedder embedder)
        {
            _dbContext = dbContext;
            _embedder = embedder;
        }

        public async Task AddEmbedding(string text)
        {
            var embeddings = await _embedder.GetEmbeddings(text);
            var item = new EmbeddingItem()
            {
                Content = text,
                Embedding = new Pgvector.Vector(embeddings)
            };
            _dbContext.Items.Add(item);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<List<EmbeddingItem>> SimilaritySearch(string text, string method = "L2Distance", int topK = 5)
        {
            var compareMathod = _methodsRouter[method];

            var embeddings = await _embedder.GetEmbeddings(text);
            var items = _dbContext.Items
                .OrderBy(x => x.Embedding!.L2Distance(new Pgvector.Vector(embeddings)))
                .Take(topK)
                .ToList();

            return items;
        }
    }
}
