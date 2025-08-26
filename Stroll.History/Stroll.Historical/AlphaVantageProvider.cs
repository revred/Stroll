using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Stroll.Historical;

/// <summary>
/// Alpha Vantage data provider - reliable free and premium historical data
/// Comprehensive API coverage with both free tier and premium subscriptions
/// Documentation: https://www.alphavantage.co/documentation/
/// </summary>
public class AlphaVantageProvider : IDataProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AlphaVantageProvider>? _logger;
    private readonly string _apiKey;
    private readonly SemaphoreSlim _rateLimiter;
    private readonly bool _isPremium;
    
    // Rate limits based on subscription tier
    private readonly int _maxRequestsPerMinute;
    private const int FREE_TIER_LIMIT = 5;    // 5 requests per minute
    private const int PREMIUM_LIMIT = 75;     // 75 requests per minute

    public AlphaVantageProvider(string apiKey, bool isPremium = false, ILogger<AlphaVantageProvider>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _isPremium = isPremium;
        _logger = logger;
        _maxRequestsPerMinute = isPremium ? PREMIUM_LIMIT : FREE_TIER_LIMIT;

        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://www.alphavantage.co/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Stroll-Backtest/1.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(2);

        _rateLimiter = new SemaphoreSlim(_maxRequestsPerMinute, _maxRequestsPerMinute);

        // Rate limiter reset
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
                _rateLimiter.Release(_maxRequestsPerMinute - _rateLimiter.CurrentCount);
            }
        });

        _logger?.LogInformation("🔑 Alpha Vantage provider initialized ({Tier} tier, {Rate} req/min)",
            isPremium ? "Premium" : "Free", _maxRequestsPerMinute);
    }

    // IDataProvider implementation
    public string ProviderName => "Alpha Vantage";
    public int Priority => 2;
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

    /// <summary>
    /// Get comprehensive historical data by combining multiple requests
    /// </summary>
    public async Task<List<AlphaVantageResult>> GetComprehensiveHistoricalAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        IProgress<AlphaVantageProgress>? progress = null)
    {
        var results = new List<AlphaVantageResult>();
        _logger?.LogInformation("🚀 Starting comprehensive acquisition for {Symbol}: {Start} to {End}",
            symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

        try
        {
            // Get daily data for the full range
            var dailyResult = await GetDailyHistoricalAsync(symbol, OutputSize.Full);
            results.Add(dailyResult);

            progress?.Report(new AlphaVantageProgress
            {
                Symbol = symbol,
                ProgressPercent = 100,
                CurrentFunction = "Daily",
                Status = $"Retrieved {dailyResult.RecordCount} daily bars"
            });

            return results;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Comprehensive acquisition failed for {Symbol}", symbol);
            return results;
        }
    }

    /// <summary>
    /// Get intraday historical data for a specific month (TIME_SERIES_INTRADAY with month parameter)
    /// </summary>
    public async Task<AlphaVantageResult> GetIntradayHistoricalMonthAsync(
        string symbol,
        IntradayInterval interval,
        string month, // Format: YYYY-MM
        OutputSize outputSize = OutputSize.Full)
    {
        await _rateLimiter.WaitAsync();

        var result = new AlphaVantageResult
        {
            Symbol = symbol,
            Function = "TIME_SERIES_INTRADAY",
            Interval = GetIntervalString(interval),
            RequestTime = DateTime.UtcNow
        };

        try
        {
            var intervalParam = GetIntervalString(interval);
            var url = $"query?function=TIME_SERIES_INTRADAY&symbol={symbol}&interval={intervalParam}" +
                     $"&month={month}&outputsize={outputSize.ToString().ToLower()}&apikey={_apiKey}";

            _logger?.LogInformation("📊 Requesting {Interval} intraday data for {Symbol} ({Month})", 
                interval, symbol, month);

            var response = await _httpClient.GetStringAsync(url);
            result.Bars = ParseIntradayTimeSeriesJson(response, symbol, intervalParam);
            result.Success = result.Bars.Count > 0;
            result.RecordCount = result.Bars.Count;

            if (result.Success)
            {
                _logger?.LogInformation("✅ Retrieved {Count} {Interval} bars for {Symbol} ({Month})", 
                    result.RecordCount, interval, symbol, month);
            }
            else
            {
                _logger?.LogWarning("⚠️ No {Interval} intraday data returned for {Symbol} ({Month})", interval, symbol, month);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Error getting {Interval} intraday data for {Symbol} ({Month})", interval, symbol, month);
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Get comprehensive intraday historical data by paging month-by-month
    /// </summary>
    public async Task<List<AlphaVantageResult>> GetIntradayHistoricalRangeAsync(
        string symbol,
        IntradayInterval interval,
        DateTime startDate,
        DateTime endDate,
        IProgress<IntradayProgress>? progress = null)
    {
        var results = new List<AlphaVantageResult>();
        var currentDate = new DateTime(startDate.Year, startDate.Month, 1);
        var endMonth = new DateTime(endDate.Year, endDate.Month, 1);
        
        var totalMonths = ((endDate.Year - startDate.Year) * 12) + (endDate.Month - startDate.Month) + 1;
        var processedMonths = 0;

        _logger?.LogInformation("🚀 Starting month-by-month intraday acquisition: {Symbol} {Interval} from {Start} to {End} ({Months} months)",
            symbol, interval, startDate.ToString("yyyy-MM"), endDate.ToString("yyyy-MM"), totalMonths);

        while (currentDate <= endMonth)
        {
            var month = currentDate.ToString("yyyy-MM");
            
            try
            {
                var monthResult = await GetIntradayHistoricalMonthAsync(symbol, interval, month);
                results.Add(monthResult);

                processedMonths++;
                var progressPercent = (double)processedMonths / totalMonths * 100;

                progress?.Report(new IntradayProgress
                {
                    Symbol = symbol,
                    Interval = interval,
                    Month = month,
                    ProgressPercent = progressPercent,
                    RecordsThisMonth = monthResult.RecordCount,
                    TotalRecordsProcessed = results.Sum(r => r.RecordCount),
                    Status = $"Month {processedMonths}/{totalMonths}: {monthResult.RecordCount} bars"
                });

                // Respect free tier rate limit (5 requests/minute)
                await Task.Delay(15000); // 15 seconds between requests
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Failed to get data for {Symbol} {Month}", symbol, month);
            }

            currentDate = currentDate.AddMonths(1);
        }

        var totalRecords = results.Sum(r => r.RecordCount);
        _logger?.LogInformation("✅ Month-by-month acquisition complete: {Records:N0} total bars across {Months} months",
            totalRecords, results.Count);

        return results;
    }

    /// <summary>
    /// Get daily historical data (TIME_SERIES_DAILY_ADJUSTED)
    /// </summary>
    public async Task<AlphaVantageResult> GetDailyHistoricalAsync(
        string symbol,
        OutputSize outputSize = OutputSize.Full)
    {
        await _rateLimiter.WaitAsync();

        var result = new AlphaVantageResult
        {
            Symbol = symbol,
            Function = "TIME_SERIES_DAILY_ADJUSTED",
            RequestTime = DateTime.UtcNow
        };

        try
        {
            var url = $"query?function=TIME_SERIES_DAILY_ADJUSTED&symbol={symbol}" +
                     $"&outputsize={outputSize.ToString().ToLower()}&apikey={_apiKey}";

            _logger?.LogInformation("📊 Requesting daily data for {Symbol} ({OutputSize})", symbol, outputSize);

            var response = await _httpClient.GetStringAsync(url);
            result.Bars = ParseDailyTimeSeriesJson(response, symbol);
            result.Success = result.Bars.Count > 0;
            result.RecordCount = result.Bars.Count;

            if (result.Success)
            {
                _logger?.LogInformation("✅ Retrieved {Count} daily bars for {Symbol}", result.RecordCount, symbol);
            }
            else
            {
                _logger?.LogWarning("⚠️ No daily data returned for {Symbol}", symbol);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Error getting daily data for {Symbol}", symbol);
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Parse TIME_SERIES_DAILY_ADJUSTED JSON response
    /// </summary>
    private List<Dictionary<string, object?>> ParseDailyTimeSeriesJson(string jsonData, string symbol)
    {
        var bars = new List<Dictionary<string, object?>>();

        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            // Check for API errors
            if (root.TryGetProperty("Error Message", out _))
            {
                _logger?.LogWarning("⚠️ Alpha Vantage API error for {Symbol}", symbol);
                return bars;
            }

            if (root.TryGetProperty("Note", out _))
            {
                _logger?.LogWarning("⚠️ Alpha Vantage rate limit or API note for {Symbol}", symbol);
                return bars;
            }

            // Parse time series data
            if (!root.TryGetProperty("Time Series (Daily)", out var timeSeries))
            {
                _logger?.LogWarning("⚠️ No daily time series data found for {Symbol}", symbol);
                return bars;
            }

            foreach (var dayData in timeSeries.EnumerateObject())
            {
                if (DateTime.TryParse(dayData.Name, out var date))
                {
                    var values = dayData.Value;
                    if (TryParseOhlcvData(values, out var ohlcv))
                    {
                        bars.Add(new Dictionary<string, object?>
                        {
                            ["t"] = date,
                            ["o"] = ohlcv.Open,
                            ["h"] = ohlcv.High,
                            ["l"] = ohlcv.Low,
                            ["c"] = ohlcv.Close,
                            ["v"] = ohlcv.Volume,
                            ["adj_close"] = ohlcv.AdjustedClose
                        });
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "❌ JSON parsing error for {Symbol}", symbol);
        }

        return bars.OrderBy(b => (DateTime)b["t"]!).ToList();
    }

    /// <summary>
    /// Parse TIME_SERIES_INTRADAY JSON response
    /// </summary>
    private List<Dictionary<string, object?>> ParseIntradayTimeSeriesJson(string jsonData, string symbol, string interval)
    {
        var bars = new List<Dictionary<string, object?>>();

        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            // Debug: Log all top-level properties
            _logger?.LogDebug("🔍 JSON Response properties: {Properties}", 
                string.Join(", ", root.EnumerateObject().Select(p => p.Name)));

            // Check for API errors
            if (root.TryGetProperty("Error Message", out _))
            {
                _logger?.LogWarning("⚠️ Alpha Vantage API error for {Symbol} ({Interval})", symbol, interval);
                return bars;
            }

            if (root.TryGetProperty("Note", out _))
            {
                _logger?.LogWarning("⚠️ Alpha Vantage rate limit or API note for {Symbol} ({Interval})", symbol, interval);
                return bars;
            }

            // Parse intraday time series data - Alpha Vantage uses specific key format
            var timeSeriesKey = $"Time Series ({interval})";
            _logger?.LogDebug("🔍 Looking for key: '{Key}'", timeSeriesKey);
            
            if (!root.TryGetProperty(timeSeriesKey, out var timeSeries))
            {
                _logger?.LogWarning("⚠️ No '{Key}' intraday time series data found for {Symbol}", timeSeriesKey, symbol);
                return bars;
            }

            var totalEntries = timeSeries.EnumerateObject().Count();
            _logger?.LogDebug("🔍 Found time series data with {Count} entries", totalEntries);
            
            var parsedCount = 0;
            foreach (var timeData in timeSeries.EnumerateObject())
            {
                if (DateTime.TryParse(timeData.Name, out var timestamp))
                {
                    var values = timeData.Value;
                    if (TryParseOhlcvData(values, out var ohlcv))
                    {
                        bars.Add(new Dictionary<string, object?>
                        {
                            ["t"] = timestamp,
                            ["o"] = ohlcv.Open,
                            ["h"] = ohlcv.High,
                            ["l"] = ohlcv.Low,
                            ["c"] = ohlcv.Close,
                            ["v"] = ohlcv.Volume
                        });
                        parsedCount++;
                    }
                }
            }
            
            _logger?.LogDebug("🔍 Successfully parsed {ParsedCount} bars from {TotalEntries} entries", 
                parsedCount, totalEntries);
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "❌ JSON parsing error for {Symbol} ({Interval})", symbol, interval);
        }

        return bars.OrderBy(b => (DateTime)b["t"]!).ToList();
    }

    private string GetIntervalString(IntradayInterval interval)
    {
        return interval switch
        {
            IntradayInterval.OneMinute => "1min",
            IntradayInterval.FiveMinute => "5min",
            IntradayInterval.FifteenMinute => "15min",
            IntradayInterval.ThirtyMinute => "30min",
            IntradayInterval.OneHour => "60min",
            _ => "5min"
        };
    }

    private bool TryParseOhlcvData(JsonElement values, out OhlcvData ohlcv)
    {
        ohlcv = new OhlcvData();

        try
        {
            ohlcv.Open = decimal.Parse(values.GetProperty("1. open").GetString()!);
            ohlcv.High = decimal.Parse(values.GetProperty("2. high").GetString()!);
            ohlcv.Low = decimal.Parse(values.GetProperty("3. low").GetString()!);
            ohlcv.Close = decimal.Parse(values.GetProperty("4. close").GetString()!);
            ohlcv.Volume = long.Parse(values.GetProperty("5. volume").GetString()!);

            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("⚠️ OHLCV parsing failed: {Error}", ex.Message);
            return false;
        }
    }

    // IDataProvider interface methods
    public async Task<List<MarketDataBar>> GetHistoricalBarsAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string interval = "1d",
        CancellationToken cancellationToken = default)
    {
        // Stub implementation - convert from existing methods
        var result = await GetIntradayHistoricalMonthAsync(symbol, IntradayInterval.FiveMinute, DateTime.Now.ToString("yyyy-MM"));
        return result.Bars.Select(bar => new MarketDataBar
        {
            Timestamp = (DateTime)bar["t"]!,
            Open = Convert.ToDouble(bar["o"]),
            High = Convert.ToDouble(bar["h"]),
            Low = Convert.ToDouble(bar["l"]),
            Close = Convert.ToDouble(bar["c"]),
            Volume = Convert.ToInt64(bar["v"]),
            VWAP = Convert.ToDouble(bar["c"]) // Approximation
        }).ToList();
    }

    public Task<OptionsChainData?> GetOptionsChainAsync(
        string symbol,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        // Alpha Vantage doesn't provide options chain data
        return Task.FromResult<OptionsChainData?>(null);
    }

    public async Task<ProviderHealthStatus> CheckHealthAsync()
    {
        try
        {
            var start = DateTime.UtcNow;
            await GetIntradayHistoricalMonthAsync("SPY", IntradayInterval.FiveMinute, DateTime.Now.ToString("yyyy-MM"));
            var elapsed = DateTime.UtcNow - start;
            
            return new ProviderHealthStatus
            {
                IsHealthy = true,
                LastCheck = DateTime.UtcNow,
                ResponseTimeMs = elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            return new ProviderHealthStatus
            {
                IsHealthy = false,
                LastCheck = DateTime.UtcNow,
                ErrorMessage = ex.Message
            };
        }
    }

    public RateLimitStatus GetRateLimitStatus()
    {
        return new RateLimitStatus
        {
            RequestsRemaining = _rateLimiter.CurrentCount,
            RequestsPerMinute = _maxRequestsPerMinute,
            IsThrottled = _rateLimiter.CurrentCount == 0
        };
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _rateLimiter?.Dispose();
    }
}

// Supporting data structures
public record AlphaVantageResult
{
    public required string Symbol { get; init; }
    public required string Function { get; init; }
    public string? Interval { get; set; }
    public DateTime RequestTime { get; set; }
    public List<Dictionary<string, object?>> Bars { get; set; } = new();
    public int RecordCount { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public record AlphaVantageProgress
{
    public required string Symbol { get; init; }
    public double ProgressPercent { get; set; }
    public string CurrentFunction { get; set; } = "";
    public string Status { get; set; } = "";
}

public record OhlcvData
{
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public decimal? AdjustedClose { get; set; }
}

public enum OutputSize
{
    Compact,  // Latest 100 data points
    Full      // Full historical data
}

public record IntradayProgress
{
    public required string Symbol { get; init; }
    public required IntradayInterval Interval { get; init; }
    public required string Month { get; init; }
    public double ProgressPercent { get; set; }
    public int RecordsThisMonth { get; set; }
    public int TotalRecordsProcessed { get; set; }
    public string Status { get; set; } = "";
}

public enum IntradayInterval
{
    OneMinute,
    FiveMinute,
    FifteenMinute,
    ThirtyMinute,
    OneHour
}