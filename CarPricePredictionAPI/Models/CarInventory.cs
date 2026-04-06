using System.ComponentModel.DataAnnotations;

namespace CarPricePredictionAPI.Models
{
    public class CarInventory
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

        [Required]
        public float Price { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
