using Microsoft.ML;
using Microsoft.ML.Data;
using CarPricePredictionAPI.Models;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
namespace CarPricePredictionAPI.ML
{
    public class CarPriceEngine
    {
        private readonly MLContext _mlContext;
        private readonly string _modelPathSdca;
        private readonly string _modelPathFastTree;
        private readonly ILogger<CarPriceEngine> _logger;

        public CarPriceEngine(IConfiguration configuration, ILogger<CarPriceEngine> logger)
        {
            _mlContext = new MLContext(seed: 0);
            _logger = logger;
            string modelFolder = Path.Combine(AppContext.BaseDirectory, "Models");
            if (!Directory.Exists(modelFolder)) Directory.CreateDirectory(modelFolder);
            
            _modelPathSdca = Path.Combine(modelFolder, "car_price_sdca.zip");
            _modelPathFastTree = Path.Combine(modelFolder, "car_price_fasttree.zip");

            // Enforce clean slate on startup: Force user to train via UI by deleting old models
            if (File.Exists(_modelPathSdca)) File.Delete(_modelPathSdca);
            if (File.Exists(_modelPathFastTree)) File.Delete(_modelPathFastTree);
        }

        // Return model path for specified algorithm
        public string GetModelPath(string algorithm)
        {
            return algorithm?.ToUpper() == "FASTTREE" ? _modelPathFastTree : _modelPathSdca;
        }

        // Deletes any existing trained model files to force retraining when new data is uploaded
        public void InvalidateModels()
        {
            try
            {
                if (File.Exists(_modelPathSdca)) File.Delete(_modelPathSdca);
                if (File.Exists(_modelPathFastTree)) File.Delete(_modelPathFastTree);
                _logger.LogInformation("Existing model files deleted via InvalidateModels.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete existing model files during InvalidateModels.");
            }
        }

        // Loads CSV, removes rows with missing/invalid fields and duplicate rows, writes cleaned CSV to a temp file.
        // Returns true and cleanedPath when successful and there is at least one valid row.
        public bool TryCleanData(string dataPath, out string cleanedPath, out string message)
        {
            cleanedPath = string.Empty;
            message = string.Empty;
            try
            {
                var dataView = LoadData(dataPath);
                var rows = _mlContext.Data.CreateEnumerable<CarData>(dataView, reuseRowObject: false).ToList();

                var nowYear = DateTime.Now.Year;
                var filtered = new List<CarData>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var r in rows)
                {
                    if (r == null) continue;
                    // basic null/empty checks
                    if (string.IsNullOrWhiteSpace(r.Brand)) continue;
                    if (string.IsNullOrWhiteSpace(r.Fuel)) continue;
                    if (string.IsNullOrWhiteSpace(r.Transmission)) continue;

                    // numeric sanity checks
                    if (r.Year < 1900 || r.Year > nowYear) continue;
                    if (r.Mileage < 0 || r.Mileage > 200000) continue;
                    if (r.Price <= 0) continue;

                    // dedupe by composite key
                    var key = string.Join("|", new[] { r.Brand.Trim(), r.Year.ToString(CultureInfo.InvariantCulture), r.Mileage.ToString(CultureInfo.InvariantCulture), r.Fuel.Trim(), r.Transmission.Trim(), r.Price.ToString(CultureInfo.InvariantCulture) });
                    if (seen.Contains(key)) continue;
                    seen.Add(key);

                    filtered.Add(r);
                }

                if (!filtered.Any())
                {
                    message = $"No valid rows after cleaning CSV (removed nulls/invalid values/duplicates). OriginalRows={rows.Count},Kept=0";
                    return false;
                }

                // write cleaned CSV to temp file
                var temp = Path.Combine(Path.GetTempPath(), $"car_prices_cleaned_{Guid.NewGuid()}.csv");
                using (var sw = new StreamWriter(temp, false))
                {
                    sw.WriteLine("Brand,Year,Mileage,Fuel,Transmission,Price");
                    foreach (var r in filtered)
                    {
                        // ensure culture-invariant formatting for decimals
                        var year = r.Year.ToString(CultureInfo.InvariantCulture);
                        var mileage = r.Mileage.ToString(CultureInfo.InvariantCulture);
                        var price = r.Price.ToString(CultureInfo.InvariantCulture);
                        sw.WriteLine($"{EscapeCsv(r.Brand)},{year},{mileage},{EscapeCsv(r.Fuel)},{EscapeCsv(r.Transmission)},{price}");
                    }
                }

                cleanedPath = temp;
                message = $"OK|OriginalRows={rows.Count}|Kept={filtered.Count}";
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean data at {DataPath}", dataPath);
                message = ex.Message;
                return false;
            }
        }

        private static string EscapeCsv(string input)
        {
            if (input == null) return string.Empty;
            if (input.Contains(',') || input.Contains('"') || input.Contains('\n'))
            {
                return '"' + input.Replace("\"", "\"\"") + '"';
            }
            return input;
        }

        // Attempts to load and validate the CSV data. Returns true when data can be loaded and has at least one row.
        public bool TryValidateData(string dataPath, out string message)
        {
            message = string.Empty;
            try
            {
                var dataView = LoadData(dataPath);
                // Ensure there's at least one data row
                var enumerable = _mlContext.Data.CreateEnumerable<CarData>(dataView, reuseRowObject: false).Take(1).ToList();
                if (!enumerable.Any())
                {
                    message = "CSV contains no data rows.";
                    return false;
                }

                message = "OK";
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data validation failed for {DataPath}", dataPath);
                message = ex.Message;
                return false;
            }
        }

        // Returns true if any trained model file exists on disk
        public bool IsAnyModelTrained()
        {
            return File.Exists(_modelPathSdca) || File.Exists(_modelPathFastTree);
        }

        public (RegressionMetrics metrics, long trainingTimeMs) TrainSdca(string dataPath)//sdca start
        {
            _logger.LogInformation("Starting SDCA training with data: {DataPath}", dataPath);
            var dataView = LoadData(dataPath);
            
            var pipeline = GetBasePipeline()
                .Append(_mlContext.Regression.Trainers.Sdca(labelColumnName: "Label", maximumNumberOfIterations: 200));

            var sw = Stopwatch.StartNew();
            var model = pipeline.Fit(dataView);
            sw.Stop();

            SaveModel(model, dataView.Schema, _modelPathSdca);
            var metrics = Evaluate(model, dataView);
            
            return (metrics, sw.ElapsedMilliseconds);
        }

        public (RegressionMetrics metrics, long trainingTimeMs) TrainFastTree(string dataPath)//fasttree start
        {
            _logger.LogInformation("Starting FastTree training with data: {DataPath}", dataPath);
            var dataView = LoadData(dataPath);
            
            var pipeline = GetBasePipeline()
                .Append(_mlContext.Regression.Trainers.FastTree(labelColumnName: "Label", numberOfLeaves: 20, numberOfTrees: 100, minimumExampleCountPerLeaf: 1));

            var sw = Stopwatch.StartNew();
            var model = pipeline.Fit(dataView);
            sw.Stop();

            SaveModel(model, dataView.Schema, _modelPathFastTree);
            var metrics = Evaluate(model, dataView);
            
            return (metrics, sw.ElapsedMilliseconds);
        }

        private IDataView LoadData(string dataPath)
        {
            try {
                return _mlContext.Data.LoadFromTextFile<CarData>(dataPath, hasHeader: true, separatorChar: ',');
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to load CSV at {DataPath}", dataPath);
                throw;
            }
        }

        private IEstimator<ITransformer> GetBasePipeline()
        {
            return _mlContext.Transforms.Categorical.OneHotEncoding(new[] 
                { 
                    new InputOutputColumnPair("BrandEncoded", "Brand"),
                    new InputOutputColumnPair("FuelEncoded", "Fuel"),
                    new InputOutputColumnPair("TransmissionEncoded", "Transmission")
                })
                .Append(_mlContext.Transforms.Concatenate("Features", "Year", "Mileage", "BrandEncoded", "FuelEncoded", "TransmissionEncoded"))
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"));
        }

        private void SaveModel(ITransformer model, DataViewSchema schema, string path)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                _mlContext.Model.Save(model, schema, stream);
            }
            _logger.LogInformation("Model saved to {Path}", path);
        }

        private RegressionMetrics Evaluate(ITransformer model, IDataView data)
        {
            var predictions = model.Transform(data);
            return _mlContext.Regression.Evaluate(predictions, "Label", "Score");
        }

        public CarPrediction Predict(CarData input, string algorithm = "SDCA")
        {
            string path = algorithm.ToUpper() == "FASTTREE" ? _modelPathFastTree : _modelPathSdca;
            if (!File.Exists(path)) throw new FileNotFoundException("Model not trained yet.");

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var model = _mlContext.Model.Load(stream, out var _);
                var engine = _mlContext.Model.CreatePredictionEngine<CarData, CarPrediction>(model);
                return engine.Predict(input);
            }
        }
    }
}
