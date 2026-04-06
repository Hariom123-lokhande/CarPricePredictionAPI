using System;
using System.ComponentModel.DataAnnotations;

namespace CarPricePredictionAPI.Models
{
    public class PredictionHistory
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Brand { get; set; } = string.Empty;

        [Required]
        public int Year { get; set; }

        [Required]
        public float Mileage { get; set; }

        [Required]
        public string Fuel { get; set; } = string.Empty;

        [Required]
        public string Transmission { get; set; } = string.Empty;

        public float PriceSDCA { get; set; }
        public float PriceFastTree { get; set; }

        public DateTime PredictedAt { get; set; } = DateTime.UtcNow;
        
        // Optional: link to User
        public string? UserId { get; set; }
    }
}
