using System;
using System.ComponentModel.DataAnnotations;

namespace CarPricePredictionAPI.Models
{
    public class ModelMetadata
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Algorithm { get; set; } = string.Empty;

        public float RSquared { get; set; }

        public float RMSE { get; set; }

        public DateTime TrainedAt { get; set; } = DateTime.UtcNow;

        public long TrainingTimeMs { get; set; }

        public string DatasetUsed { get; set; } = "car_prices.csv";

        public string Status { get; set; } = "TRAINED";
    }
}
