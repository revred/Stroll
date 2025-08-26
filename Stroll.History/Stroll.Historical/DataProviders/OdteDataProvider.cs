using Microsoft.Extensions.Logging;

namespace Stroll.Historical.DataProviders;

/// <summary>
/// Local Historical Data Provider - uses copied historical data files
/// This provides fast, reliable access to the comprehensive dataset
/// </summary>
public class OdteDataProvider : IDataProvider, IDisposable
{
    private readonly string _dataPath;
    private readonly ILogger<OdteDataProvider>? _logger;

    public string ProviderName => "Local Historical Data";
    public int Priority => 0; // Highest priority - fastest and most reliable
    public bool IsAvailable => Directory.Exists(_dataPath);

    public OdteDataProvider(string dataPath = @"C:\code\Stroll\Stroll.History\Data\Historical", ILogger<OdteDataProvider>? logger = null)
    {
        _dataPath = dataPath;
        _logger = logger;
    }

    public async Task<List<MarketDataBar>> GetHistoricalBarsAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string interval = "1d",
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation($"Loading data for {symbol} from local historical data");

            // Try to load from local historical data files
            var localData = await LoadLocalDataAsync(symbol, startDate, endDate);
            if (localData.Any())
            {
                _logger?.LogInformation($"Loaded {localData.Count} bars from local historical data");
                return localData;
            }

            _logger?.LogWarning($"No local data found for {symbol}");
            return new List<MarketDataBar>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Failed to load ODTE data for {symbol}");
            return new List<MarketDataBar>();
        }
    }

    public async Task<OptionsChainData?> GetOptionsChainAsync(
        string symbol,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        // ODTE historical data doesn't currently include options chains in CSV format
        await Task.Delay(1, cancellationToken);
        return null;
    }

    public async Task<ProviderHealthStatus> CheckHealthAsync()
    {
        var startTime = DateTime.UtcNow;
        try
        {
            var isHealthy = IsAvailable && await Task.Run(() =>
            {
                // Check if we can access local historical data
                return Directory.Exists(_dataPath) && Directory.GetFiles(_dataPath, "*.csv").Length > 0;
            });

            return new ProviderHealthStatus
            {
                IsHealthy = isHealthy,
                LastCheck = DateTime.UtcNow,
                ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                ConsecutiveFailures = isHealthy ? 0 : 1
            };
        }
        catch (Exception ex)
        {
            return new ProviderHealthStatus
            {
                IsHealthy = false,
                LastCheck = DateTime.UtcNow,
                ErrorMessage = ex.Message,
                ResponseTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                ConsecutiveFailures = 1
            };
        }
    }

    public RateLimitStatus GetRateLimitStatus()
    {
        // ODTE data access has no rate limits
        return new RateLimitStatus
        {
            RequestsRemaining = int.MaxValue,
            RequestsPerMinute = int.MaxValue,
            ResetTime = DateTime.UtcNow.AddMinutes(1),
            IsThrottled = false
        };
    }

    private async Task<List<MarketDataBar>> LoadLocalDataAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        var bars = new List<MarketDataBar>();

        try
        {
            if (!Directory.Exists(_dataPath))
                return bars;

            // Look for CSV files matching the symbol in main directory and subdirectories
            var csvFiles = Directory.GetFiles(_dataPath, $"{symbol}_*.csv", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(_dataPath, $"{symbol}_historical_data.csv", SearchOption.AllDirectories))
                .Distinct()
                .ToArray();

            foreach (var csvFile in csvFiles)
            {
                _logger?.LogDebug($"Parsing file: {csvFile}");
                var fileBars = await ParseCsvFileAsync(csvFile, startDate, endDate);
                bars.AddRange(fileBars);
            }

            // Remove duplicates and sort by timestamp
            bars = bars.GroupBy(b => b.Timestamp)
                      .Select(g => g.First())
                      .OrderBy(b => b.Timestamp)
                      .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load local historical data");
        }

        return bars;
    }

    private async Task<List<MarketDataBar>> ParseCsvFileAsync(string filePath, DateTime startDate, DateTime endDate)
    {
        var bars = new List<MarketDataBar>();

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            
            // Skip header line
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var fields = line.Split(',');
                if (fields.Length >= 6)
                {
                    try
                    {
                        // Handle different date formats
                        DateTime timestamp;
                        if (DateTime.TryParse(fields[0], out timestamp))
                        {
                            // Check if date is in range
                            if (timestamp < startDate || timestamp > endDate)
                                continue;

                            var bar = new MarketDataBar
                            {
                                Timestamp = timestamp,
                                Open = double.Parse(fields[1]),
                                High = double.Parse(fields[2]),
                                Low = double.Parse(fields[3]),
                                Close = double.Parse(fields[4]),
                                Volume = long.Parse(fields[5]),
                                VWAP = fields.Length > 6 ? double.Parse(fields[6]) : 0
                            };

                            // Calculate VWAP if not provided
                            if (bar.VWAP == 0)
                                bar.VWAP = (bar.High + bar.Low + bar.Close) / 3.0;

                            bars.Add(bar);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug($"Failed to parse line {i}: {ex.Message}");
                        // Skip invalid lines
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Failed to parse CSV file: {filePath}");
        }

        return bars;
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}