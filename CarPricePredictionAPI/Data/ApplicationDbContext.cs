using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using CarPricePredictionAPI.Models;

namespace CarPricePredictionAPI.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<PredictionHistory> PredictionHistories { get; set; } = null!;
        public DbSet<ModelMetadata> ModelMetadatas { get; set; } = null!;
        public DbSet<CarInventory> CarInventories { get; set; } = null!;
    }
}
