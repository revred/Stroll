using Stroll.Historical;

namespace Stroll.Historical.Tests.TestData;

/// <summary>
/// Mock data provider for reliable testing without external dependencies
/// </summary>
public class TestDataProvider : IDataProvider, IDisposable
{
    private readonly List<MarketDataBar> _testData;
    private readonly OptionsChainData? _testOptionsData;
    private bool _isHealthy;
    private int _requestCount;

    public string ProviderName => "Test Data Provider";
    public int Priority { get; set; } = 0;
    public bool IsAvailable { get; set; } = true;
    public bool SimulateFailure { get; set; } = false;
    public int MaxRequests { get; set; } = 100;

    public TestDataProvider(List<MarketDataBar>? testData = null, OptionsChainData? optionsData = null)
    {
        _testData = testData ?? GenerateDefaultTestData();
        _testOptionsData = optionsData;
        _isHealthy = true;
        _requestCount = 0;
    }

    public async Task<List<MarketDataBar>> GetHistoricalBarsAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        string interval = "1d",
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken); // Simulate network delay
        
        _requestCount++;

        if (SimulateFailure || _requestCount > MaxRequests)
        {
            throw new InvalidOperationException("Simulated provider failure");
        }

        // Filter test data by date range and symbol
        return _testData
            .Where(bar => bar.Timestamp >= startDate && 
                         bar.Timestamp <= endDate)
            .ToList();
    }

    public async Task<OptionsChainData?> GetOptionsChainAsync(
        string symbol,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(5, cancellationToken);
        
        if (SimulateFailure)
            throw new InvalidOperationException("Simulated provider failure");

        return _testOptionsData;
    }

    public async Task<ProviderHealthStatus> CheckHealthAsync()
    {
        await Task.Delay(1);
        
        return new ProviderHealthStatus
        {
            IsHealthy = _isHealthy && !SimulateFailure,
            LastCheck = DateTime.UtcNow,
            ResponseTimeMs = 10,
            ConsecutiveFailures = SimulateFailure ? 1 : 0
        };
    }

    public RateLimitStatus GetRateLimitStatus()
    {
        return new RateLimitStatus
        {
            RequestsRemaining = Math.Max(0, MaxRequests - _requestCount),
            RequestsPerMinute = MaxRequests,
            ResetTime = DateTime.UtcNow.AddMinutes(1),
            IsThrottled = _requestCount >= MaxRequests
        };
    }

    public void SetHealthy(bool healthy) => _isHealthy = healthy;
    public void ResetRequestCount() => _requestCount = 0;
    public int GetRequestCount() => _requestCount;

    private static List<MarketDataBar> GenerateDefaultTestData()
    {
        var bars = new List<MarketDataBar>();
        var startDate = new DateTime(2020, 1, 1);
        var random = new Random(42); // Deterministic seed

        for (int i = 0; i < 100; i++)
        {
            var date = startDate.AddDays(i);
            var price = 100.0 + (random.NextDouble() - 0.5) * 20; // Price between 90-110
            var dailyRange = random.NextDouble() * 5; // Max daily range of 5

            bars.Add(new MarketDataBar
            {
                Timestamp = date,
                Open = price - dailyRange / 4,
                High = price + dailyRange / 2,
                Low = price - dailyRange / 2,
                Close = price + dailyRange / 4,
                Volume = random.Next(1000000, 10000000),
                VWAP = price
            });
        }

        return bars;
    }

    public void Dispose()
    {
        // No resources to dispose
    }
}