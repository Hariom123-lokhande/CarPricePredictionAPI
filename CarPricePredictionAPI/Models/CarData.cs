using Microsoft.ML.Data;

namespace CarPricePredictionAPI.Models
{
    public class CarData
    {
        [LoadColumn(0)]
        public string Brand { get; set; } = string.Empty;

        [LoadColumn(1)]
        public float Year { get; set; }

        [LoadColumn(2)]
        public float Mileage { get; set; }

        [LoadColumn(3)]
        public string Fuel { get; set; } = string.Empty;

        [LoadColumn(4)]
        public string Transmission { get; set; } = string.Empty;

        [LoadColumn(5)]
        [ColumnName("Label")]
        public float Price { get; set; }
    }
}
