namespace Stroll.Historical;

/// <summary>
/// Common interface for all data providers in Stroll.Historical
/// Provides consistent data acquisition from remote sources
/// </summary>
public interface IDataProvider
{
    string ProviderName { get; }
    int Priority { get; } // Lower number = higher priority
    bool IsAvailable { get; }

    /// <summary>
    /// Fetch historical bars for a symbol and date range
    /// </summary>
    Task<List<MarketDataBar>> GetHistoricalBarsAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string interval = "1d",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetch options chain data for a specific symbol and date
    /// </summary>
    Task<OptionsChainData?> GetOptionsChainAsync(
        string symbol,
        DateTime date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if provider is healthy and can accept requests
    /// </summary>
    Task<ProviderHealthStatus> CheckHealthAsync();

    /// <summary>
    /// Get current rate limit status
    /// </summary>
    RateLimitStatus GetRateLimitStatus();
}

/// <summary>
/// Market data bar for historical data
/// </summary>
public class MarketDataBar
{
    public DateTime Timestamp { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
    public double VWAP { get; set; }
}

/// <summary>
/// Options chain data structure
/// </summary>
public class OptionsChainData
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal UnderlyingPrice { get; set; }
    public List<OptionsContract> Calls { get; set; } = new();
    public List<OptionsContract> Puts { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public string DataSource { get; set; } = string.Empty;
}

/// <summary>
/// Individual options contract
/// </summary>
public class OptionsContract
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime ExpirationDate { get; set; }
    public decimal Strike { get; set; }
    public string Type { get; set; } = ""; // "CALL" or "PUT"
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal Last { get; set; }
    public int Volume { get; set; }
    public int OpenInterest { get; set; }
    public decimal ImpliedVolatility { get; set; }
    public decimal Delta { get; set; }
    public decimal Gamma { get; set; }
    public decimal Theta { get; set; }
    public decimal Vega { get; set; }
    public decimal Rho { get; set; }
}

/// <summary>
/// Provider health status
/// </summary>
public class ProviderHealthStatus
{
    public bool IsHealthy { get; set; }
    public DateTime LastCheck { get; set; }
    public string? ErrorMessage { get; set; }
    public double ResponseTimeMs { get; set; }
    public int ConsecutiveFailures { get; set; }
}

/// <summary>
/// Rate limit tracking
/// </summary>
public class RateLimitStatus
{
    public int RequestsRemaining { get; set; }
    public int RequestsPerMinute { get; set; }
    public DateTime ResetTime { get; set; }
    public bool IsThrottled { get; set; }
    public TimeSpan? RetryAfter { get; set; }
}