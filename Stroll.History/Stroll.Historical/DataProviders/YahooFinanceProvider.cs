using System.Globalization;
using System.Text.Json;

namespace Stroll.Historical.DataProviders;

/// <summary>
/// Yahoo Finance provider for Stroll.Historical
/// Free data source for historical market data acquisition
/// </summary>
public class YahooFinanceProvider : IDataProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _rateLimiter;
    private const int MAX_REQUESTS_PER_MINUTE = 30;
    private DateTime _lastResetTime = DateTime.UtcNow;
    private int _requestsThisMinute = 0;

    public string ProviderName => "Yahoo Finance";
    public int Priority => 1; // High priority for free provider
    public bool IsAvailable => _httpClient != null;

    public YahooFinanceProvider()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _rateLimiter = new SemaphoreSlim(MAX_REQUESTS_PER_MINUTE, MAX_REQUESTS_PER_MINUTE);
    }

    public async Task<List<MarketDataBar>> GetHistoricalBarsAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string interval = "1d",
        CancellationToken cancellationToken = default)
    {
        await _rateLimiter.WaitAsync(cancellationToken);

        try
        {
            await CheckRateLimit();

            var period1 = ((DateTimeOffset)startDate).ToUnixTimeSeconds();
            var period2 = ((DateTimeOffset)endDate).ToUnixTimeSeconds();

            var url = $"https://query1.finance.yahoo.com/v7/finance/download/{symbol}" +
                      $"?period1={period1}&period2={period2}&interval={interval}&events=history";

            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            return ParseCsvResponse(response);
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
        // Yahoo Finance options data requires premium access
        // Return null to indicate unsupported for free tier
        await Task.Delay(1, cancellationToken);
        return null;
    }

    public async Task<ProviderHealthStatus> CheckHealthAsync()
    {
        var startTime = DateTime.UtcNow;
        try
        {
            // Test with a simple SPY request
            var testUrl = "https://query1.finance.yahoo.com/v7/finance/download/SPY" +
                         "?period1=1640995200&period2=1640995200&interval=1d&events=history";

            using var testClient = new HttpClient();
            testClient.Timeout = TimeSpan.FromSeconds(10);
            
            var response = await testClient.GetAsync(testUrl);
            var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

            return new ProviderHealthStatus
            {
                IsHealthy = response.IsSuccessStatusCode,
                LastCheck = DateTime.UtcNow,
                ResponseTimeMs = responseTime,
                ConsecutiveFailures = response.IsSuccessStatusCode ? 0 : 1
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

    private List<MarketDataBar> ParseCsvResponse(string csvData)
    {
        var bars = new List<MarketDataBar>();
        var lines = csvData.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i < lines.Length; i++) // Skip header
        {
            var fields = lines[i].Split(',');
            if (fields.Length >= 6)
            {
                try
                {
                    var bar = new MarketDataBar
                    {
                        Timestamp = DateTime.Parse(fields[0]),
                        Open = double.Parse(fields[1], CultureInfo.InvariantCulture),
                        High = double.Parse(fields[2], CultureInfo.InvariantCulture),
                        Low = double.Parse(fields[3], CultureInfo.InvariantCulture),
                        Close = double.Parse(fields[4], CultureInfo.InvariantCulture),
                        Volume = long.Parse(fields[5]),
                        VWAP = fields.Length > 6 ? double.Parse(fields[6], CultureInfo.InvariantCulture) : 0
                    };

                    if (bar.VWAP == 0)
                        bar.VWAP = (bar.High + bar.Low + bar.Close) / 3.0; // Simple VWAP approximation

                    bars.Add(bar);
                }
                catch
                {
                    // Skip invalid rows
                }
            }
        }

        return bars;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _rateLimiter?.Dispose();
    }
}