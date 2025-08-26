using System.Text.Json;

namespace Stroll.Historical.DataProviders;

/// <summary>
/// Alpha Vantage provider for Stroll.Historical
/// API-based provider with free tier available
/// </summary>
public class AlphaVantageProvider : IDataProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly SemaphoreSlim _rateLimiter;
    private const int MAX_REQUESTS_PER_MINUTE = 5; // Alpha Vantage free tier limit
    private DateTime _lastResetTime = DateTime.UtcNow;
    private int _requestsThisMinute = 0;

    public string ProviderName => "Alpha Vantage";
    public int Priority => 2; // Lower priority due to strict rate limits
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

    public AlphaVantageProvider(string apiKey)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _httpClient = new HttpClient();
        _rateLimiter = new SemaphoreSlim(MAX_REQUESTS_PER_MINUTE, MAX_REQUESTS_PER_MINUTE);
    }

    public async Task<List<MarketDataBar>> GetHistoricalBarsAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string interval = "1d",
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return new List<MarketDataBar>();

        await _rateLimiter.WaitAsync(cancellationToken);

        try
        {
            await CheckRateLimit();

            var function = interval == "1d" ? "TIME_SERIES_DAILY" : "TIME_SERIES_INTRADAY";
            var url = $"https://www.alphavantage.co/query?function={function}&symbol={symbol}&apikey={_apiKey}&outputsize=full";

            if (function == "TIME_SERIES_INTRADAY")
            {
                url += $"&interval={ConvertInterval(interval)}";
            }

            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            return ParseAlphaVantageResponse(response, startDate, endDate);
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    public async Task<OptionsChainData?> GetOptionsChainAsync(
        string symbol,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        // Alpha Vantage options data is not in free tier
        await Task.Delay(1, cancellationToken);
        return null;
    }

    public async Task<ProviderHealthStatus> CheckHealthAsync()
    {
        var startTime = DateTime.UtcNow;
        try
        {
            var testUrl = $"https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol=SPY&apikey={_apiKey}&outputsize=compact";

            using var testClient = new HttpClient();
            testClient.Timeout = TimeSpan.FromSeconds(15);
            
            var response = await testClient.GetAsync(testUrl);
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            var content = await response.Content.ReadAsStringAsync();
            var isHealthy = response.IsSuccessStatusCode && !content.Contains("Error Message") && !content.Contains("Note");

            return new ProviderHealthStatus
            {
                IsHealthy = isHealthy,
                LastCheck = DateTime.UtcNow,
                ResponseTimeMs = responseTime,
                ConsecutiveFailures = isHealthy ? 0 : 1,
                ErrorMessage = isHealthy ? null : "API limit reached or invalid response"
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
        var now = DateTime.UtcNow;
        var minutesSinceReset = (now - _lastResetTime).TotalMinutes;

        if (minutesSinceReset >= 1.0)
        {
            _requestsThisMinute = 0;
            _lastResetTime = now;
        }

        return new RateLimitStatus
        {
            RequestsRemaining = Math.Max(0, MAX_REQUESTS_PER_MINUTE - _requestsThisMinute),
            RequestsPerMinute = MAX_REQUESTS_PER_MINUTE,
            ResetTime = _lastResetTime.AddMinutes(1),
            IsThrottled = _requestsThisMinute >= MAX_REQUESTS_PER_MINUTE
        };
    }

    private async Task CheckRateLimit()
    {
        var now = DateTime.UtcNow;
        var minutesSinceReset = (now - _lastResetTime).TotalMinutes;

        if (minutesSinceReset >= 1.0)
        {
            _requestsThisMinute = 0;
            _lastResetTime = now;
        }

        if (_requestsThisMinute >= MAX_REQUESTS_PER_MINUTE)
        {
            var waitTime = _lastResetTime.AddMinutes(1) - now;
            if (waitTime > TimeSpan.Zero)
            {
                await Task.Delay(waitTime);
                _requestsThisMinute = 0;
                _lastResetTime = DateTime.UtcNow;
            }
        }

        _requestsThisMinute++;
    }

    private string ConvertInterval(string interval)
    {
        return interval switch
        {
            "1m" => "1min",
            "5m" => "5min",
            "15m" => "15min",
            "30m" => "30min",
            "1h" => "60min",
            _ => "1min"
        };
    }

    private List<MarketDataBar> ParseAlphaVantageResponse(string jsonResponse, DateTime startDate, DateTime endDate)
    {
        var bars = new List<MarketDataBar>();

        try
        {
            using var document = JsonDocument.Parse(jsonResponse);
            var root = document.RootElement;

            // Find time series data
            JsonElement timeSeries = default;
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Contains("Time Series"))
                {
                    timeSeries = property.Value;
                    break;
                }
            }

            if (timeSeries.ValueKind == JsonValueKind.Undefined)
                return bars;

            foreach (var entry in timeSeries.EnumerateObject())
            {
                if (DateTime.TryParse(entry.Name, out var timestamp))
                {
                    if (timestamp < startDate || timestamp > endDate)
                        continue;

                    var data = entry.Value;
                    
                    var bar = new MarketDataBar
                    {
                        Timestamp = timestamp,
                        Open = GetDouble(data, "1. open"),
                        High = GetDouble(data, "2. high"),
                        Low = GetDouble(data, "3. low"),
                        Close = GetDouble(data, "4. close"),
                        Volume = GetLong(data, "5. volume"),
                        VWAP = 0 // Alpha Vantage doesn't provide VWAP
                    };

                    // Calculate simple VWAP approximation
                    bar.VWAP = (bar.High + bar.Low + bar.Close) / 3.0;

                    bars.Add(bar);
                }
            }
        }
        catch
        {
            // Return empty list on parsing error
        }

        return bars.OrderBy(b => b.Timestamp).ToList();
    }

    private double GetDouble(JsonElement element, string key)
    {
        if (element.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            if (double.TryParse(prop.GetString(), out var result))
                return result;
        }
        return 0.0;
    }

    private long GetLong(JsonElement element, string key)
    {
        if (element.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            if (long.TryParse(prop.GetString(), out var result))
                return result;
        }
        return 0L;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _rateLimiter?.Dispose();
    }
}