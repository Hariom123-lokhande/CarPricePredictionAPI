using Microsoft.AspNetCore.Mvc;
using CarPricePredictionAPI.Models;
using CarPricePredictionAPI.ML;
using CarPricePredictionAPI.Data;
using Microsoft.AspNetCore.Authorization;
using System.IO;
using System.Security.Claims;

namespace CarPricePredictionAPI.Controllers
{
    [Authorize]
    public class CarPriceController : Controller
    {
        private readonly CarPriceEngine _engine;
        private readonly ILogger<CarPriceController> _logger;
        private readonly ApplicationDbContext _db;
        private readonly string _dataPath;
        private readonly string _uploadedMarkerPath;
        private readonly string _trainedMarkerPath;

        public CarPriceController(CarPriceEngine engine, ILogger<CarPriceController> logger, ApplicationDbContext db)
        {
            _engine = engine;
            _logger = logger;
            _db = db;
            _dataPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "car_prices.csv");
            _uploadedMarkerPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "car_prices.uploaded");
            _trainedMarkerPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "car_prices.trained.json");
            var dir = Path.GetDirectoryName(_dataPath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

        }


        [HttpGet("api/carprice/status")]
        public IActionResult Status()
        {
            try
            {
                var uploaded = System.IO.File.Exists(_uploadedMarkerPath)
                    ? System.Text.Json.JsonSerializer.Deserialize<object>(System.IO.File.ReadAllText(_uploadedMarkerPath))
                    : (object?)null;

                var lastTrained = (object?)_db.ModelMetadatas
                    .OrderByDescending(m => m.TrainedAt)
                    .FirstOrDefault();

                return Ok(new { upload = uploaded, trained = lastTrained });
            }
            catch
            {
                return Ok(new { upload = (object?)null, trained = (object?)null });
            }
        }

        [HttpGet("Dashboard")]
        [ApiExplorerSettings(IgnoreApi = true)]
        public IActionResult Dashboard()
        {
            return View();
        }

        [HttpGet("api/carprice/history")]
        public IActionResult GetHistory()
        {
            var history = _db.PredictionHistories
                .OrderByDescending(h => h.PredictedAt)
                .Take(10) // Only show last 10 entries
                .ToList();
            return Ok(history);
        }

        [HttpPost("api/carprice/upload")]
        public async Task<IActionResult> UploadCsv(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("File not uploaded");

            // Read uploaded file into memory to validate header/format before saving to disk
            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                if (ms.Length == 0) return BadRequest("Uploaded file is empty.");

                ms.Position = 0;
                using var reader = new StreamReader(ms, leaveOpen: true);
                var firstLine = await reader.ReadLineAsync();
                var secondLine = await reader.ReadLineAsync();

                if (string.IsNullOrWhiteSpace(firstLine))
                    return BadRequest("CSV file appears empty or malformed.");

                // Determine column count and check for expected columns
                int colsFirst = firstLine.Split(',').Length;
                int colsSecond = secondLine != null ? secondLine.Split(',').Length : colsFirst;
                int colCount = Math.Max(colsFirst, colsSecond);

                if (colCount < 6)
                    return BadRequest("CSV must have at least 6 columns: Brand,Year,Mileage,Fuel,Transmission,Price");

                var expectedHeaders = new[] { "Brand", "Year", "Mileage", "Fuel", "Transmission", "Price" };
                bool headerLooksLikeNames = expectedHeaders.All(h => firstLine.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0);

                // If header not present, make sure the first data row contains parsable numeric values where expected
                if (!headerLooksLikeNames)
                {
                    if (secondLine == null)
                        return BadRequest("CSV appears to be missing header and has no data rows.");

                    var sample = secondLine.Split(',');
                    if (sample.Length < 6)
                        return BadRequest("CSV data rows do not contain expected number of columns.");

                    bool yearOk = float.TryParse(sample[1], out _);
                    bool mileageOk = float.TryParse(sample[2], out _);
                    bool priceOk = float.TryParse(sample[5], out _);
                    if (!yearOk || !mileageOk || !priceOk)
                        return BadRequest("CSV data columns do not match expected types. Ensure columns are: Brand, Year (number), Mileage (number), Fuel, Transmission, Price (number) and include a header row if possible.");
                }

                // Passed validation - Append to existing file AND Save to Database
                bool masterExists = System.IO.File.Exists(_dataPath);
                ms.Position = 0;
                using (var uploadReader = new StreamReader(ms, leaveOpen: true))
                {
                    using (var fs = new FileStream(_dataPath, masterExists ? FileMode.Append : FileMode.Create, FileAccess.Write))
                    using (var writer = new StreamWriter(fs))
                    {
                        if (masterExists && headerLooksLikeNames)
                        {
                            await uploadReader.ReadLineAsync(); // Skip header
                        }

                        string? line;
                        while ((line = await uploadReader.ReadLineAsync()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            // 1. Write to CSV File
                            await writer.WriteLineAsync(line);

                            // 2. Parsed for Database (SQL) saving
                            try
                            {
                                var parts = line.Split(',');
                                if (parts.Length >= 6)
                                {
                                    var inv = new CarInventory
                                    {
                                        Brand = parts[0].Trim(),
                                        Year = int.Parse(parts[1].Trim()),
                                        Mileage = float.Parse(parts[2].Trim()),
                                        Fuel = parts[3].Trim(),
                                        Transmission = parts[4].Trim(),
                                        Price = float.Parse(parts[5].Trim()),
                                        UploadedAt = DateTime.UtcNow
                                    };
                                    _db.CarInventories.Add(inv);
                                }
                            }
                            catch { /* skip malformed line for DB */ }
                        }
                    }
                    await _db.SaveChangesAsync();
                }

                // Invalidate any existing trained models so the app doesn't keep using older models
                try
                {
                    _engine.InvalidateModels();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to invalidate previous models after upload.");
                }

                // Create an uploaded marker so training cannot run unless an explicit upload occurred
                try
                {
                    var uploadInfo = new { fileName = file.FileName, uploadedAt = DateTime.UtcNow };
                    System.IO.File.WriteAllText(_uploadedMarkerPath, System.Text.Json.JsonSerializer.Serialize(uploadInfo));
                }
                catch { /* non-fatal */ }

                _logger.LogInformation("CSV uploaded and saved to DB & Disk");
                return Ok(new { message = "Data saved to Database and Disk successfully.", fileName = file.FileName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating or saving uploaded CSV");
                return BadRequest(new { error = "Failed to process CSV: " + ex.Message });
            }
        }

        [HttpGet("api/carprice/inventory")]
        public IActionResult GetInventory()
        {
            var data = _db.CarInventories
                .OrderByDescending(i => i.UploadedAt)
                .Take(10) // Show last 10 
                .ToList();
            return Ok(data);
        }

        [HttpPost("api/carprice/train")]
        public IActionResult Train([FromQuery] string algorithm)
        {
            if (!System.IO.File.Exists(_dataPath)) return BadRequest("CSV file not found. Please upload it first.");

            // Ensure upload action happened (prevents training if model files were deleted but no new upload)
            if (!System.IO.File.Exists(_uploadedMarkerPath))
            {
                return BadRequest(new { error = "No recent CSV upload found. Please upload the CSV via the dashboard before training." });
            }

            if (!_engine.TryValidateData(_dataPath, out var validationMessage))
            {
                _logger.LogWarning("CSV validation failed: {Message}", validationMessage);
                return BadRequest(new { error = "CSV invalid: " + validationMessage });
            }

            // Ensure old models are removed before training so new training produces fresh model files
            try
            {
                _engine.InvalidateModels();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to invalidate models before training.");
            }

            string? cleanedPath = null;
            try
            {
                // Clean the uploaded CSV to remove bad rows and duplicates, train on the cleaned file
                if (!_engine.TryCleanData(_dataPath, out cleanedPath, out var cleanMessage))
                {
                    _logger.LogWarning("CSV cleaning failed: {Message}", cleanMessage);
                    return BadRequest(new { error = "CSV cleaning failed: " + cleanMessage });
                }

                if (algorithm?.ToUpper() == "COMPARE")
                {
                    var sdcaRes = _engine.TrainSdca(cleanedPath);
                    var ftRes = _engine.TrainFastTree(cleanedPath);

                    // write trained metadata
                    try
                    {
                        var trainedInfo = new
                        {
                            algorithm = "COMPARE",
                            trainedAt = DateTime.UtcNow,
                            sdca = new { rSquared = sdcaRes.metrics.RSquared, rmse = sdcaRes.metrics.RootMeanSquaredError, timeMs = sdcaRes.trainingTimeMs, modelPath = _engine.GetModelPath("SDCA") },
                            fastTree = new { rSquared = ftRes.metrics.RSquared, rmse = ftRes.metrics.RootMeanSquaredError, timeMs = ftRes.trainingTimeMs, modelPath = _engine.GetModelPath("FASTTREE") },
                            cleanInfo = cleanMessage
                        };
                        System.IO.File.WriteAllText(_trainedMarkerPath, System.Text.Json.JsonSerializer.Serialize(trainedInfo));

                        // Save to database
                        _db.ModelMetadatas.Add(new ModelMetadata
                        {
                            Algorithm = "SDCA",
                            RSquared = (float)sdcaRes.metrics.RSquared,
                            RMSE = (float)sdcaRes.metrics.RootMeanSquaredError,
                            TrainedAt = DateTime.UtcNow,
                            TrainingTimeMs = sdcaRes.trainingTimeMs
                        });
                        _db.ModelMetadatas.Add(new ModelMetadata
                        {
                            Algorithm = "FastTree",
                            RSquared = (float)ftRes.metrics.RSquared,
                            RMSE = (float)ftRes.metrics.RootMeanSquaredError,
                            TrainedAt = DateTime.UtcNow,
                            TrainingTimeMs = ftRes.trainingTimeMs
                        });
                        _db.SaveChanges();
                    }
                    catch { }

                    return Ok(new
                    {
                        sdca = new { rSquared = sdcaRes.metrics.RSquared, time = sdcaRes.trainingTimeMs + "ms" },
                        fastTree = new { rSquared = ftRes.metrics.RSquared, time = ftRes.trainingTimeMs + "ms" }
                    });
                }
                else if (algorithm?.ToUpper() == "FASTTREE")
                {
                    var result = _engine.TrainFastTree(cleanedPath);
                    try
                    {
                        var trainedInfo = new
                        {
                            algorithm = "FASTTREE",
                            trainedAt = DateTime.UtcNow,
                            metrics = new { rSquared = result.metrics.RSquared, rmse = result.metrics.RootMeanSquaredError, timeMs = result.trainingTimeMs },
                            modelPath = _engine.GetModelPath("FASTTREE"),
                            cleanInfo = cleanMessage
                        };
                        System.IO.File.WriteAllText(_trainedMarkerPath, System.Text.Json.JsonSerializer.Serialize(trainedInfo));

                        // Save to database
                        _db.ModelMetadatas.Add(new ModelMetadata
                        {
                            Algorithm = "FastTree",
                            RSquared = (float)result.metrics.RSquared,
                            RMSE = (float)result.metrics.RootMeanSquaredError,
                            TrainedAt = DateTime.UtcNow,
                            TrainingTimeMs = result.trainingTimeMs
                        });
                        _db.SaveChanges();
                    }
                    catch { }

                    return Ok(new
                    {
                        rSquared = result.metrics.RSquared,
                        rmse = result.metrics.RootMeanSquaredError,
                        time = result.trainingTimeMs,
                        algorithm = "FastTree"
                    });
                }
                else
                {
                    var result = _engine.TrainSdca(cleanedPath);
                    try
                    {
                        var trainedInfo = new
                        {
                            algorithm = "SDCA",
                            trainedAt = DateTime.UtcNow,
                            metrics = new { rSquared = result.metrics.RSquared, rmse = result.metrics.RootMeanSquaredError, timeMs = result.trainingTimeMs },
                            modelPath = _engine.GetModelPath("SDCA"),
                            cleanInfo = cleanMessage
                        };
                        System.IO.File.WriteAllText(_trainedMarkerPath, System.Text.Json.JsonSerializer.Serialize(trainedInfo));

                        // Save to database
                        _db.ModelMetadatas.Add(new ModelMetadata
                        {
                            Algorithm = "SDCA",
                            RSquared = (float)result.metrics.RSquared,
                            RMSE = (float)result.metrics.RootMeanSquaredError,
                            TrainedAt = DateTime.UtcNow,
                            TrainingTimeMs = result.trainingTimeMs
                        });
                        _db.SaveChanges();
                    }
                    catch { }

                    return Ok(new
                    {
                        rSquared = result.metrics.RSquared,
                        rmse = result.metrics.RootMeanSquaredError,
                        time = result.trainingTimeMs,
                        algorithm = "SDCA"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Training failed");
                return StatusCode(500, $"Training failed: {ex.Message}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(cleanedPath) && System.IO.File.Exists(cleanedPath))
                {
                    try { System.IO.File.Delete(cleanedPath); } catch { }
                }
            }
        }

        [AllowAnonymous]
        [HttpPost("api/carprice/predict")]
        public IActionResult PredictPrice([FromBody] CarData input)
        {
            try
            {
                var allowedBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Hyundai", "Maruti", "Honda", "Tata", "Mahindra", "Toyota", "Kia", "BMW", "Audi" };
                if (string.IsNullOrWhiteSpace(input.Brand) || !allowedBrands.Contains(input.Brand))
                    return BadRequest(new { error = "Invalid brand selected." });

                if (input.Year < 2000 || input.Year > DateTime.Now.Year)
                    return BadRequest(new { error = "Enter a valid manufacturing year." });

                if (input.Mileage < 0 || input.Mileage > 200000)
                    return BadRequest(new { error = "Please enter realistic mileage (0 - 200000 km)." });

                var allowedFuels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Petrol", "Diesel" };
                if (string.IsNullOrWhiteSpace(input.Fuel) || !allowedFuels.Contains(input.Fuel))
                    return BadRequest(new { error = "Invalid fuel type selected." });

                var allowedTransmissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Manual", "Automatic" };
                if (string.IsNullOrWhiteSpace(input.Transmission) || !allowedTransmissions.Contains(input.Transmission))
                    return BadRequest(new { error = "Invalid transmission type selected." });

                var predictionSdca = _engine.Predict(input, "SDCA");
                var predictionFt = _engine.Predict(input, "FASTTREE");

                // Remove floor or make it very low to see real output during debug
                if (predictionSdca.PredictedPrice < 10000) predictionSdca.PredictedPrice = 10000;
                if (predictionFt.PredictedPrice < 10000) predictionFt.PredictedPrice = 10000;

                string statusSdca = "Fair Deal 👍";
                if (predictionSdca.PredictedPrice > 1000000) statusSdca = "Premium Deal 💎";
                else if (predictionSdca.PredictedPrice < 400000) statusSdca = "Budget Deal 💰";

                string statusFt = "Fair Deal 👍";
                if (predictionFt.PredictedPrice > 1000000) statusFt = "Premium Deal 💎";
                else if (predictionFt.PredictedPrice < 400000) statusFt = "Budget Deal 💰";

                // Save prediction to database
                try
                {
                    var history = new PredictionHistory
                    {
                        Brand = input.Brand,
                        Year = (int)input.Year,
                        Mileage = input.Mileage,
                        Fuel = input.Fuel,
                        Transmission = input.Transmission,
                        PriceSDCA = predictionSdca.PredictedPrice,
                        PriceFastTree = predictionFt.PredictedPrice,
                        PredictedAt = DateTime.UtcNow,
                        UserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    };
                    _db.PredictionHistories.Add(history);
                    _db.SaveChanges();
                }
                catch (Exception dbEx)
                {
                    _logger.LogWarning(dbEx, "Failed to save prediction history to database.");
                }

                return Ok(new
                {
                    sdca = new
                    {
                        priceFormatted = "₹" + predictionSdca.PredictedPrice.ToString("N0"),
                        dealStatus = statusSdca
                    },
                    fastTree = new
                    {
                        priceFormatted = "₹" + predictionFt.PredictedPrice.ToString("N0"),
                        dealStatus = statusFt
                    }
                });
            }
            catch (FileNotFoundException)
            {
                return BadRequest(new { error = "Model not trained yet. Please train the model from the dashboard." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Prediction failed");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
