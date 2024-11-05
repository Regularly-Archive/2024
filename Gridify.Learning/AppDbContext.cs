using Gridify.Learning.Models;
using Microsoft.EntityFrameworkCore;
using System;

namespace Gridify.Learning
{
    public class AppDbContext : DbContext
    {
        public DbSet<Product> Products { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>().HasData(
                new Product { Id = 1, Sku = "X000006482043", Brand = "Nike1", ModelName = "Soria Tank1", Color = "Blue" },
                new Product { Id = 2, Sku = "X000006482044", Brand = "Nike2", ModelName = "Soria Tank2", Color = "Black" },
                new Product { Id = 3, Sku = "X000006482045", Brand = "Nike3", ModelName = "Soria Tank3", Color = "White" },
                new Product { Id = 4, Sku = "X000006482046", Brand = "Nike4", ModelName = "Soria Tank5", Color = "Red" }
            );
        }
    }
}
