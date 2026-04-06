using Microsoft.ML.Data;

namespace CarPricePredictionAPI.Models
{
    public class CarPrediction
    {
        [ColumnName("Score")]
        public float PredictedPrice { get; set; }
    }
}
