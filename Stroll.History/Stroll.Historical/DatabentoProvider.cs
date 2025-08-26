using Microsoft.Extensions.Logging;
using Stroll.Storage;
using System.Net.Http.Json;
using System.Text.Json;

namespace Stroll.Historical;

/// <summary>
/// High-performance Databento data provider for institutional-grade market data
/// Provides 1-minute bars since 2000 with comprehensive coverage
/// Ideal for professional backtesting and options strategies
/// </summary>
public class DatabentoProvider : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DatabentoProvider>? _logger;
    private readonly string _apiKey;
    private readonly SemaphoreSlim _rateLimiter;
    private const int MAX_REQUESTS_PER_SECOND = 10; // Databento rate limit

    public DatabentoProvider(string apiKey, ILogger<DatabentoProvider>? logger = null)
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _logger = logger;
        _httpClient = new HttpClient();
        _rateLimiter = new SemaphoreSlim(MAX_REQUESTS_PER_SECOND, MAX_REQUESTS_PER_SECOND);

        // Configure HTTP client for Databento API
        _httpClient.BaseAddress = new Uri("https://hist.databento.com/v0/");
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Stroll-Backtest/1.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(5); // Allow for large data downloads

        // Rate limiter reset
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                _rateLimiter.Release(MAX_REQUESTS_PER_SECOND - _rateLimiter.CurrentCount);
            }
        });
    }

    /// <summary>
    /// Get comprehensive historical data from Databento
    /// </summary>
    public async Task<DatabentoResult> GetHistoricalBarsAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        DatabentoGranularity granularity = DatabentoGranularity.OneMinute,
        string dataset = "XNAS.ITCH")
    {
        await _rateLimiter.WaitAsync();

        _logger?.LogInformation("📊 Requesting {Symbol} data from {Start} to {End} ({Granularity})",
            symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), granularity);

        var result = new DatabentoResult
        {
            Symbol = symbol,
            StartDate = startDate,
            EndDate = endDate,
            Granularity = granularity,
            RequestTime = DateTime.UtcNow
        };

        try
        {
            // Prepare request parameters
            var request = new DatabentoRequest
            {
                Dataset = dataset,
                Symbols = new[] { symbol },
                Schema = "ohlcv-1m", // 1-minute OHLCV bars
                Start = startDate.ToString("yyyy-MM-dd"),
                End = endDate.ToString("yyyy-MM-dd"),
                Encoding = "csv", // CSV for easier parsing
                Compression = "none"
            };

            // Make request to Databento
            var response = await _httpClient.PostAsJsonAsync("timeseries.get_range", request);
            response.EnsureSuccessStatusCode();

            var csvData = await response.Content.ReadAsStringAsync();
            result.Bars = ParseDatabentoCsv(csvData, symbol);
            result.Success = true;
            result.RecordCount = result.Bars.Count;

            _logger?.LogInformation("✅ Received {Count} bars for {Symbol} from Databento",
                result.RecordCount, symbol);

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "❌ HTTP error requesting {Symbol} from Databento", symbol);
            result.ErrorMessage = ex.Message;
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Error processing {Symbol} data from Databento", symbol);
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    /// <summary>
    /// Get data in chunks to handle large date ranges efficiently
    /// </summary>
    public async Task<List<DatabentoResult>> GetHistoricalBarsChunkedAsync(
        string symbol,
        DateTime startDate,
        DateTime endDate,
        DatabentoGranularity granularity = DatabentoGranularity.OneMinute,
        TimeSpan? chunkSize = null,
        IProgress<DatabentoProgress>? progress = null)
    {
        chunkSize ??= TimeSpan.FromDays(30); // Default 30-day chunks
        var results = new List<DatabentoResult>();
        var currentStart = startDate;
        var totalDays = (endDate - startDate).Days;
        var processedDays = 0;

        _logger?.LogInformation("🚀 Starting chunked acquisition: {Symbol} from {Start} to {End}",
            symbol, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

        while (currentStart < endDate)
        {
            var chunkEnd = currentStart.Add(chunkSize.Value);
            if (chunkEnd > endDate) chunkEnd = endDate;

            try
            {
                var chunkResult = await GetHistoricalBarsAsync(symbol, currentStart, chunkEnd, granularity);
                results.Add(chunkResult);

                processedDays += (chunkEnd - currentStart).Days;
                var progressPercent = (double)processedDays / totalDays * 100;

                progress?.Report(new DatabentoProgress
                {
                    Symbol = symbol,
                    ProgressPercent = progressPercent,
                    CurrentChunk = results.Count,
                    RecordsProcessed = results.Sum(r => r.RecordCount),
                    ChunkStart = currentStart,
                    ChunkEnd = chunkEnd,
                    Status = $"Completed chunk {results.Count}: {chunkResult.RecordCount} bars"
                });

                // Respectful delay between chunks
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ Failed to get chunk {Start} to {End}", currentStart, chunkEnd);
            }

            currentStart = chunkEnd.AddDays(1);
        }

        var totalRecords = results.Sum(r => r.RecordCount);
        _logger?.LogInformation("✅ Chunked acquisition complete: {Records:N0} total records across {Chunks} chunks",
            totalRecords, results.Count);

        return results;
    }

    /// <summary>
    /// Parse Databento CSV format into standardized bars
    /// </summary>
    private List<Dictionary<string, object?>> ParseDatabentoCsv(string csvData, string symbol)
    {
        var bars = new List<Dictionary<string, object?>>();
        var lines = csvData.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length <= 1) return bars;

        // Skip header line - Databento CSV format:
        // ts_recv,ts_event,symbol,open,high,low,close,volume
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length >= 8)
            {
                try
                {
                    // Parse Databento timestamp (nanoseconds since epoch)
                    var tsEvent = long.Parse(parts[1]);
                    var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsEvent / 1_000_000).DateTime;

                    var open = decimal.Parse(parts[3]);
                    var high = decimal.Parse(parts[4]);
                    var low = decimal.Parse(parts[5]);
                    var close = decimal.Parse(parts[6]);
                    var volume = long.Parse(parts[7]);

                    bars.Add(new Dictionary<string, object?>
                    {
                        ["t"] = timestamp,
                        ["o"] = open,
                        ["h"] = high,
                        ["l"] = low,
                        ["c"] = close,
                        ["v"] = volume
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("⚠️ Failed to parse Databento line {LineNum}: {Error}", i, ex.Message);
                }
            }
        }

        return bars.OrderBy(b => (DateTime)b["t"]!).ToList();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        _rateLimiter?.Dispose();
    }
}

// Supporting data structures
public record DatabentoRequest
{
    public required string Dataset { get; init; }
    public required string[] Symbols { get; init; }
    public required string Schema { get; init; }
    public required string Start { get; init; }
    public required string End { get; init; }
    public string Encoding { get; init; } = "csv";
    public string Compression { get; init; } = "none";
}

public record DatabentoResult
{
    public required string Symbol { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required DatabentoGranularity Granularity { get; init; }
    public DateTime RequestTime { get; set; }
    public List<Dictionary<string, object?>> Bars { get; set; } = new();
    public int RecordCount { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public record DatabentoProgress
{
    public required string Symbol { get; init; }
    public double ProgressPercent { get; set; }
    public int CurrentChunk { get; set; }
    public int RecordsProcessed { get; set; }
    public DateTime ChunkStart { get; set; }
    public DateTime ChunkEnd { get; set; }
    public string Status { get; set; } = "";
}

public enum DatabentoGranularity
{
    OneMinute,
    FiveMinute,
    OneHour,
    Daily
}