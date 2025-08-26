using Microsoft.Extensions.Logging;
using Stroll.Historical.DataProviders;

namespace Stroll.Historical;

/// <summary>
/// Data acquisition engine for Stroll.Historical
/// Manages multiple data providers and coordinates data fetching operations
/// Based on ODTE.Historical's acquisition patterns
/// </summary>
public class DataAcquisitionEngine : IDisposable
{
    private readonly List<IDataProvider> _providers = new();
    private readonly ILogger<DataAcquisitionEngine>? _logger;
    private readonly string _outputPath;

    public DataAcquisitionEngine(string outputPath, ILogger<DataAcquisitionEngine>? logger = null, bool initializeDefaultProviders = true)
    {
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _logger = logger;
        if (initializeDefaultProviders)
        {
            InitializeProviders();
        }
    }

    /// <summary>
    /// Execute comprehensive data acquisition for a symbol and date range
    /// </summary>
    public async Task<DataAcquisitionResult> AcquireDataAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string interval = "1d",
        CancellationToken cancellationToken = default)
    {
        var result = new DataAcquisitionResult
        {
            Symbol = symbol,
            StartDate = startDate,
            EndDate = endDate,
            StartTime = DateTime.UtcNow
        };

        _logger?.LogInformation($"Starting data acquisition for {symbol} from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

        // Handle invalid date range gracefully
        if (endDate < startDate)
        {
            _logger?.LogWarning($"Invalid date range: end date {endDate:yyyy-MM-dd} is before start date {startDate:yyyy-MM-dd}");
            result.Success = true;
            result.BarsAcquired = 0;
            result.EndTime = DateTime.UtcNow;
            return result;
        }

        try
        {
            Directory.CreateDirectory(_outputPath);

            var allBars = new List<MarketDataBar>();
            var successfulProviders = new List<string>();
            var failedProviders = new List<string>();

            // Try providers in priority order
            foreach (var provider in _providers.Where(p => p.IsAvailable).OrderBy(p => p.Priority))
            {
                try
                {
                    _logger?.LogDebug($"Trying provider: {provider.ProviderName}");
                    
                    var health = await provider.CheckHealthAsync();
                    if (!health.IsHealthy)
                    {
                        _logger?.LogWarning($"Provider {provider.ProviderName} is unhealthy: {health.ErrorMessage}");
                        failedProviders.Add(provider.ProviderName);
                        continue;
                    }

                    var bars = await provider.GetHistoricalBarsAsync(symbol, startDate, endDate, interval, cancellationToken);
                    if (bars.Any())
                    {
                        allBars.AddRange(bars);
                        successfulProviders.Add(provider.ProviderName);
                        _logger?.LogInformation($"Successfully fetched {bars.Count} bars from {provider.ProviderName}");
                        break; // Use first successful provider
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Failed to fetch data from {provider.ProviderName}");
                    failedProviders.Add(provider.ProviderName);
                }
            }

            if (allBars.Any())
            {
                // Remove duplicates and sort by timestamp
                var uniqueBars = allBars
                    .GroupBy(b => b.Timestamp)
                    .Select(g => g.First())
                    .OrderBy(b => b.Timestamp)
                    .ToList();

                // Save to storage format compatible with Stroll.Storage
                await SaveBarsAsync(symbol, uniqueBars, startDate, endDate);

                result.Success = true;
                result.BarsAcquired = uniqueBars.Count;
                result.SuccessfulProviders = successfulProviders;
                
                _logger?.LogInformation($"Data acquisition completed successfully: {uniqueBars.Count} bars acquired");
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = "No data could be acquired from any provider";
                _logger?.LogError("Failed to acquire data from any provider");
            }

            result.FailedProviders = failedProviders;
            result.EndTime = DateTime.UtcNow;
            
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
            
            _logger?.LogError(ex, "Data acquisition operation failed");
            return result;
        }
    }

    /// <summary>
    /// Get status of all configured providers
    /// </summary>
    public async Task<List<ProviderStatus>> GetProviderStatusAsync()
    {
        var statuses = new List<ProviderStatus>();

        foreach (var provider in _providers)
        {
            try
            {
                var health = await provider.CheckHealthAsync();
                var rateLimit = provider.GetRateLimitStatus();

                statuses.Add(new ProviderStatus
                {
                    Name = provider.ProviderName,
                    Priority = provider.Priority,
                    IsAvailable = provider.IsAvailable,
                    IsHealthy = health.IsHealthy,
                    ResponseTimeMs = health.ResponseTimeMs,
                    ErrorMessage = health.ErrorMessage,
                    RequestsRemaining = rateLimit.RequestsRemaining,
                    RequestsPerMinute = rateLimit.RequestsPerMinute,
                    IsThrottled = rateLimit.IsThrottled
                });
            }
            catch (Exception ex)
            {
                statuses.Add(new ProviderStatus
                {
                    Name = provider.ProviderName,
                    Priority = provider.Priority,
                    IsAvailable = false,
                    IsHealthy = false,
                    ErrorMessage = ex.Message
                });
            }
        }

        return statuses;
    }

    /// <summary>
    /// Add a custom data provider to the engine
    /// </summary>
    public void AddProvider(IDataProvider provider)
    {
        _providers.Add(provider);
        _logger?.LogInformation($"Added provider: {provider.ProviderName}");
    }

    private void InitializeProviders()
    {
        // Add Local Historical Data provider (highest priority - local data access)
        _providers.Add(new OdteDataProvider(@"C:\code\Stroll\Stroll.History\Data\Historical"));
        _logger?.LogInformation("Added Local Historical Data provider");

        // Add Yahoo Finance (free, no API key required)
        _providers.Add(new YahooFinanceProvider());
        _logger?.LogInformation("Added Yahoo Finance provider");

        // Add Alpha Vantage if API key is available
        var alphaVantageKey = Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY");
        if (!string.IsNullOrEmpty(alphaVantageKey))
        {
            _providers.Add(new AlphaVantageProvider(alphaVantageKey));
            _logger?.LogInformation("Added Alpha Vantage provider");
        }

        // Future providers can be added here (Polygon, Twelve Data, etc.)
        
        if (!_providers.Any())
        {
            _logger?.LogWarning("No data providers configured. Data acquisition will not work.");
        }
    }

    private async Task SaveBarsAsync(string symbol, List<MarketDataBar> bars, DateTime startDate, DateTime endDate)
    {
        try
        {
            // Save in CSV format compatible with Stroll.Storage expectations
            var csvContent = "timestamp,open,high,low,close,volume,vwap\n" + 
                           string.Join("\n", bars.Select(b => 
                               $"{b.Timestamp:yyyy-MM-dd HH:mm:ss},{b.Open},{b.High},{b.Low},{b.Close},{b.Volume},{b.VWAP}"));

            var fileName = $"{symbol}_{startDate:yyyyMMdd}_{endDate:yyyyMMdd}.csv";
            var filePath = Path.Combine(_outputPath, fileName);
            
            await File.WriteAllTextAsync(filePath, csvContent);
            _logger?.LogDebug($"Saved {bars.Count} bars to {filePath}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"Failed to save bars for {symbol}");
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var provider in _providers.OfType<IDisposable>())
        {
            provider.Dispose();
        }
        _providers.Clear();
    }
}

/// <summary>
/// Result of a data acquisition operation
/// </summary>
public class DataAcquisitionResult
{
    public string Symbol { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int BarsAcquired { get; set; }
    public List<string> SuccessfulProviders { get; set; } = new();
    public List<string> FailedProviders { get; set; } = new();

    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Status information for a data provider
/// </summary>
public class ProviderStatus
{
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsHealthy { get; set; }
    public double ResponseTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public int RequestsRemaining { get; set; }
    public int RequestsPerMinute { get; set; }
    public bool IsThrottled { get; set; }
}