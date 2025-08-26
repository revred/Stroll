using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Stroll.Historical;

/// <summary>
/// Multi-source SPX data acquisition strategy based on ODTE's proven approach
/// Uses free tiers across multiple providers with intelligent failover
/// Strategy: Stooq -> Yahoo Finance -> Alpha Vantage (demo) with chunked acquisition
/// </summary>
public class MultiSourceDataAcquisition
{
    private readonly ILogger<MultiSourceDataAcquisition> _logger;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, int> _providerFailures;
    private readonly SemaphoreSlim _yahooRateLimit;
    private readonly SemaphoreSlim _alphaRateLimit;
    private readonly SemaphoreSlim _stooqRateLimit;
    
    // Rate limits based on ODTE's proven configuration
    private const int YAHOO_MAX_PER_MINUTE = 30;
    private const int ALPHA_MAX_PER_MINUTE = 5; // Free tier limit
    private const int STOOQ_MAX_PER_MINUTE = 30;

    public MultiSourceDataAcquisition(ILogger<MultiSourceDataAcquisition>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MultiSourceDataAcquisition>.Instance;
        _httpClient = new HttpClient();
        _providerFailures = new Dictionary<string, int>();
        
        // Initialize rate limiters
        _yahooRateLimit = new SemaphoreSlim(YAHOO_MAX_PER_MINUTE, YAHOO_MAX_PER_MINUTE);
        _alphaRateLimit = new SemaphoreSlim(ALPHA_MAX_PER_MINUTE, ALPHA_MAX_PER_MINUTE);
        _stooqRateLimit = new SemaphoreSlim(STOOQ_MAX_PER_MINUTE, STOOQ_MAX_PER_MINUTE);
        
        // Set up HTTP client with browser-like headers to avoid 401 errors
        SetupHttpClient();
        
        // Start rate limiter reset tasks
        StartRateLimiterResets();
    }

    /// <summary>
    /// Acquire complete SPX dataset using multi-source strategy with chunked approach
    /// </summary>
    public async Task<SpxAcquisitionResult> AcquireSpxDataAsync(DateTime startDate, DateTime endDate, 
        IProgress<AcquisitionProgress>? progress = null)
    {
        _logger.LogInformation("🚀 Starting Multi-Source SPX Data Acquisition");
        _logger.LogInformation("📅 Target: {StartDate} to {EndDate} ({TotalDays} days)", 
            startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), 
            (endDate - startDate).Days);
        
        var result = new SpxAcquisitionResult
        {
            StartDate = startDate,
            EndDate = endDate,
            StartTime = DateTime.UtcNow
        };
        
        // Strategy: Break into yearly chunks for better success rate
        var yearlyChunks = GenerateYearlyChunks(startDate, endDate);
        var allData = new List<SpxDataPoint>();
        var totalChunks = yearlyChunks.Count;
        
        for (int i = 0; i < yearlyChunks.Count; i++)
        {
            var chunk = yearlyChunks[i];
            _logger.LogInformation("📊 Processing chunk {ChunkNum}/{TotalChunks}: {Year}", 
                i + 1, totalChunks, chunk.Year);
            
            var chunkData = await AcquireChunkAsync(chunk);
            allData.AddRange(chunkData);
            
            // Report progress
            var progressPercent = ((i + 1.0) / totalChunks) * 100;
            progress?.Report(new AcquisitionProgress
            {
                ProgressPercent = progressPercent,
                CurrentChunk = i + 1,
                TotalChunks = totalChunks,
                RecordsAcquired = chunkData.Count,
                TotalRecords = allData.Count,
                Status = $"Completed {chunk.Year}: {chunkData.Count} bars"
            });
            
            // Respectful delay between chunks
            await Task.Delay(2000);
        }
        
        // Deduplicate and sort
        allData = allData
            .GroupBy(d => d.Date.Date)
            .Select(g => g.First())
            .OrderBy(d => d.Date)
            .ToList();
        
        result.DataPoints = allData;
        result.TotalRecords = allData.Count;
        result.EndTime = DateTime.UtcNow;
        result.Duration = result.EndTime - result.StartTime;
        result.Success = allData.Count > 0;
        result.ProviderStats = _providerFailures.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        
        _logger.LogInformation("✅ Multi-Source Acquisition Complete: {Records} records in {Duration}", 
            result.TotalRecords, result.Duration);
        
        return result;
    }

    /// <summary>
    /// Acquire data for a single yearly chunk using provider fallback strategy
    /// </summary>
    private async Task<List<SpxDataPoint>> AcquireChunkAsync(YearlyChunk chunk)
    {
        var providers = GetProviderOrder();
        
        foreach (var provider in providers)
        {
            try
            {
                _logger.LogDebug("🔄 Trying {Provider} for {Year}", provider, chunk.Year);
                
                var data = provider switch
                {
                    "Stooq" => await TryStooqAsync(chunk),
                    "Yahoo" => await TryYahooAsync(chunk),
                    "Alpha" => await TryAlphaVantageAsync(chunk),
                    _ => new List<SpxDataPoint>()
                };
                
                if (data.Count > 0)
                {
                    _logger.LogInformation("✅ {Provider} succeeded for {Year}: {Count} bars", 
                        provider, chunk.Year, data.Count);
                    ResetFailureCount(provider);
                    return data;
                }
                else
                {
                    _logger.LogWarning("⚠️ {Provider} returned no data for {Year}", provider, chunk.Year);
                    IncrementFailureCount(provider);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("❌ {Provider} failed for {Year}: {Message}", 
                    provider, chunk.Year, ex.Message);
                IncrementFailureCount(provider);
            }
        }
        
        _logger.LogError("💔 All providers failed for {Year}", chunk.Year);
        return new List<SpxDataPoint>();
    }

    /// <summary>
    /// Try Stooq provider (fastest, no API key required)
    /// </summary>
    private async Task<List<SpxDataPoint>> TryStooqAsync(YearlyChunk chunk)
    {
        await _stooqRateLimit.WaitAsync();
        
        // Stooq URL for SPX data (uses ^SPX symbol)
        var url = "https://stooq.com/q/d/l/?s=%5ESPX&i=d";
        
        var response = await _httpClient.GetStringAsync(url);
        return ParseStooqCsv(response, chunk.StartDate, chunk.EndDate);
    }

    /// <summary>
    /// Try Yahoo Finance provider (reliable, free tier)
    /// </summary>
    private async Task<List<SpxDataPoint>> TryYahooAsync(YearlyChunk chunk)
    {
        await _yahooRateLimit.WaitAsync();
        
        // Convert to Unix timestamps for Yahoo API
        var startUnix = ((DateTimeOffset)chunk.StartDate).ToUnixTimeSeconds();
        var endUnix = ((DateTimeOffset)chunk.EndDate).ToUnixTimeSeconds();
        
        var url = $"https://query1.finance.yahoo.com/v7/finance/download/%5ESPX" +
                  $"?period1={startUnix}&period2={endUnix}&interval=1d" +
                  $"&events=history&includeAdjustedClose=true";
        
        var response = await _httpClient.GetStringAsync(url);
        return ParseYahooCsv(response, chunk.StartDate, chunk.EndDate);
    }

    /// <summary>
    /// Try Alpha Vantage provider (fallback, demo key)
    /// </summary>
    private async Task<List<SpxDataPoint>> TryAlphaVantageAsync(YearlyChunk chunk)
    {
        await _alphaRateLimit.WaitAsync();
        
        // Use demo API key (limited but functional)
        var url = $"https://www.alphavantage.co/query?" +
                  $"function=TIME_SERIES_DAILY_ADJUSTED&symbol=SPX" +
                  $"&outputsize=full&apikey=demo";
        
        var response = await _httpClient.GetStringAsync(url);
        return ParseAlphaVantageJson(response, chunk.StartDate, chunk.EndDate);
    }

    /// <summary>
    /// Parse Stooq CSV format
    /// </summary>
    private List<SpxDataPoint> ParseStooqCsv(string csvData, DateTime startDate, DateTime endDate)
    {
        var dataPoints = new List<SpxDataPoint>();
        var lines = csvData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        for (int i = 1; i < lines.Length; i++) // Skip header
        {
            var parts = lines[i].Split(',');
            if (parts.Length >= 6)
            {
                if (DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, 
                    DateTimeStyles.None, out var date) &&
                    date >= startDate && date <= endDate &&
                    decimal.TryParse(parts[1], out var open) &&
                    decimal.TryParse(parts[2], out var high) &&
                    decimal.TryParse(parts[3], out var low) &&
                    decimal.TryParse(parts[4], out var close) &&
                    long.TryParse(parts[5], out var volume))
                {
                    dataPoints.Add(new SpxDataPoint
                    {
                        Date = date,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = volume,
                        Provider = "Stooq"
                    });
                }
            }
        }
        
        return dataPoints.OrderBy(d => d.Date).ToList();
    }

    /// <summary>
    /// Parse Yahoo Finance CSV format
    /// </summary>
    private List<SpxDataPoint> ParseYahooCsv(string csvData, DateTime startDate, DateTime endDate)
    {
        var dataPoints = new List<SpxDataPoint>();
        var lines = csvData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        for (int i = 1; i < lines.Length; i++) // Skip header
        {
            var parts = lines[i].Split(',');
            if (parts.Length >= 6)
            {
                if (DateTime.TryParse(parts[0], out var date) &&
                    date >= startDate && date <= endDate &&
                    decimal.TryParse(parts[1], out var open) &&
                    decimal.TryParse(parts[2], out var high) &&
                    decimal.TryParse(parts[3], out var low) &&
                    decimal.TryParse(parts[4], out var close) &&
                    long.TryParse(parts[6], out var volume)) // Volume is column 6 in Yahoo format
                {
                    dataPoints.Add(new SpxDataPoint
                    {
                        Date = date,
                        Open = open,
                        High = high,
                        Low = low,
                        Close = close,
                        Volume = volume,
                        Provider = "Yahoo"
                    });
                }
            }
        }
        
        return dataPoints.OrderBy(d => d.Date).ToList();
    }

    /// <summary>
    /// Parse Alpha Vantage JSON format
    /// </summary>
    private List<SpxDataPoint> ParseAlphaVantageJson(string jsonData, DateTime startDate, DateTime endDate)
    {
        var dataPoints = new List<SpxDataPoint>();
        
        try
        {
            var doc = JsonDocument.Parse(jsonData);
            if (doc.RootElement.TryGetProperty("Time Series (Daily)", out var timeSeries))
            {
                foreach (var kvp in timeSeries.EnumerateObject())
                {
                    if (DateTime.TryParse(kvp.Name, out var date) &&
                        date >= startDate && date <= endDate)
                    {
                        var dayData = kvp.Value;
                        dataPoints.Add(new SpxDataPoint
                        {
                            Date = date,
                            Open = decimal.Parse(dayData.GetProperty("1. open").GetString()!),
                            High = decimal.Parse(dayData.GetProperty("2. high").GetString()!),
                            Low = decimal.Parse(dayData.GetProperty("3. low").GetString()!),
                            Close = decimal.Parse(dayData.GetProperty("4. close").GetString()!),
                            Volume = long.Parse(dayData.GetProperty("6. volume").GetString()!),
                            Provider = "Alpha"
                        });
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("JSON parsing error for Alpha Vantage: {Message}", ex.Message);
        }
        
        return dataPoints.OrderBy(d => d.Date).ToList();
    }

    private void SetupHttpClient()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
        _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
        _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
    }

    private void StartRateLimiterResets()
    {
        // Reset rate limiters every minute
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(1));
                _yahooRateLimit.Release(YAHOO_MAX_PER_MINUTE - _yahooRateLimit.CurrentCount);
                _alphaRateLimit.Release(ALPHA_MAX_PER_MINUTE - _alphaRateLimit.CurrentCount);
                _stooqRateLimit.Release(STOOQ_MAX_PER_MINUTE - _stooqRateLimit.CurrentCount);
            }
        });
    }

    private List<YearlyChunk> GenerateYearlyChunks(DateTime startDate, DateTime endDate)
    {
        var chunks = new List<YearlyChunk>();
        var currentYear = startDate.Year;
        var endYear = endDate.Year;
        
        while (currentYear <= endYear)
        {
            var chunkStart = currentYear == startDate.Year ? startDate : new DateTime(currentYear, 1, 1);
            var chunkEnd = currentYear == endYear ? endDate : new DateTime(currentYear, 12, 31);
            
            chunks.Add(new YearlyChunk
            {
                Year = currentYear,
                StartDate = chunkStart,
                EndDate = chunkEnd
            });
            
            currentYear++;
        }
        
        return chunks;
    }

    private List<string> GetProviderOrder()
    {
        // Order providers by failure count (ascending - most reliable first)
        var providers = new[]
        {
            ("Stooq", GetFailureCount("Stooq")),
            ("Yahoo", GetFailureCount("Yahoo")), 
            ("Alpha", GetFailureCount("Alpha"))
        };
        
        return providers
            .OrderBy(p => p.Item2)
            .Select(p => p.Item1)
            .ToList();
    }

    private int GetFailureCount(string provider) => 
        _providerFailures.TryGetValue(provider, out var count) ? count : 0;

    private void IncrementFailureCount(string provider) => 
        _providerFailures[provider] = GetFailureCount(provider) + 1;

    private void ResetFailureCount(string provider) => 
        _providerFailures[provider] = 0;
}

// Supporting data structures
public record YearlyChunk
{
    public required int Year { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
}

public record SpxDataPoint
{
    public required DateTime Date { get; init; }
    public required decimal Open { get; init; }
    public required decimal High { get; init; }
    public required decimal Low { get; init; }
    public required decimal Close { get; init; }
    public required long Volume { get; init; }
    public required string Provider { get; init; }
}

public record SpxAcquisitionResult
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public List<SpxDataPoint> DataPoints { get; set; } = new();
    public int TotalRecords { get; set; }
    public bool Success { get; set; }
    public Dictionary<string, int> ProviderStats { get; set; } = new();
}

public record AcquisitionProgress
{
    public double ProgressPercent { get; set; }
    public int CurrentChunk { get; set; }
    public int TotalChunks { get; set; }
    public int RecordsAcquired { get; set; }
    public int TotalRecords { get; set; }
    public string Status { get; set; } = "";
}