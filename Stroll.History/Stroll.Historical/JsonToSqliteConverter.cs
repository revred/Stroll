using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Stroll.Historical;

/// <summary>
/// High-performance converter for Alpha Vantage JSON data to SQLite format
/// Processes 22 months of acquired SPY data for backtesting
/// </summary>
public class JsonToSqliteConverter
{
    private readonly string _jsonDataPath;
    private readonly string _sqliteDbPath;
    private readonly ILogger<JsonToSqliteConverter>? _logger;

    public JsonToSqliteConverter(string jsonDataPath, string sqliteDbPath, ILogger<JsonToSqliteConverter>? logger = null)
    {
        _jsonDataPath = jsonDataPath ?? throw new ArgumentNullException(nameof(jsonDataPath));
        _sqliteDbPath = sqliteDbPath ?? throw new ArgumentNullException(nameof(sqliteDbPath));
        _logger = logger;
    }

    /// <summary>
    /// Convert all JSON files to SQLite database
    /// </summary>
    public async Task<ConversionResult> ConvertAllAsync()
    {
        _logger?.LogInformation("🚀 Starting JSON to SQLite conversion");
        
        var result = new ConversionResult
        {
            StartTime = DateTime.UtcNow,
            JsonDataPath = _jsonDataPath,
            SqliteDbPath = _sqliteDbPath
        };

        try
        {
            // Initialize SQLite database
            await InitializeDatabaseAsync();

            // Find all JSON files
            var jsonFiles = Directory.GetFiles(_jsonDataPath, "SPY_*_5min.json")
                .OrderBy(f => f)
                .ToList();

            _logger?.LogInformation("📊 Found {Count} JSON files to convert", jsonFiles.Count);
            result.TotalFiles = jsonFiles.Count;

            var allBars = new List<MarketBar>();

            // Process each JSON file
            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(jsonFile);
                    _logger?.LogInformation("🔄 Processing {FileName}...", fileName);

                    var bars = await ProcessJsonFileAsync(jsonFile);
                    allBars.AddRange(bars);
                    result.ProcessedFiles++;
                    result.TotalBars += bars.Count;

                    _logger?.LogInformation("✅ {FileName}: {Count} bars", fileName, bars.Count);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "❌ Failed to process {FileName}", Path.GetFileName(jsonFile));
                    result.FailedFiles.Add(Path.GetFileName(jsonFile));
                }
            }

            if (allBars.Count > 0)
            {
                // Sort all bars by timestamp for optimal storage
                allBars = allBars.OrderBy(b => b.Timestamp).ToList();
                
                // Bulk insert into SQLite
                await BulkInsertBarsAsync(allBars);
                
                result.FirstBarDate = allBars.First().Timestamp;
                result.LastBarDate = allBars.Last().Timestamp;
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            result.Success = result.FailedFiles.Count == 0;

            _logger?.LogInformation("🎯 Conversion complete: {Bars} bars across {Files} files in {Duration}",
                result.TotalBars, result.ProcessedFiles, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            result.Success = false;
            result.ErrorMessage = ex.Message;
            
            _logger?.LogError(ex, "💥 Conversion failed");
            throw;
        }
    }

    /// <summary>
    /// Initialize SQLite database with proper schema
    /// </summary>
    private async Task InitializeDatabaseAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_sqliteDbPath)!);

        using var connection = new SqliteConnection($"Data Source={_sqliteDbPath}");
        await connection.OpenAsync();

        // Enable WAL mode for performance
        using var walCmd = connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL";
        await walCmd.ExecuteNonQueryAsync();

        // Create market_bars table compatible with existing system
        using var createCmd = connection.CreateCommand();
        createCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS market_bars (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                timestamp DATETIME NOT NULL,
                open REAL NOT NULL,
                high REAL NOT NULL,
                low REAL NOT NULL,
                close REAL NOT NULL,
                volume INTEGER NOT NULL,
                granularity TEXT NOT NULL DEFAULT '5m',
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(symbol, timestamp, granularity)
            )";
        await createCmd.ExecuteNonQueryAsync();

        // Create optimized indices
        var indices = new[]
        {
            "CREATE INDEX IF NOT EXISTS idx_market_bars_symbol_timestamp ON market_bars(symbol, timestamp)",
            "CREATE INDEX IF NOT EXISTS idx_market_bars_symbol_granularity ON market_bars(symbol, granularity)",
            "CREATE INDEX IF NOT EXISTS idx_market_bars_timestamp ON market_bars(timestamp)"
        };

        using var indexCmd = connection.CreateCommand();
        foreach (var indexSql in indices)
        {
            indexCmd.CommandText = indexSql;
            await indexCmd.ExecuteNonQueryAsync();
        }

        _logger?.LogInformation("💾 SQLite database initialized: {Path}", _sqliteDbPath);
    }

    /// <summary>
    /// Process a single JSON file and extract market bars
    /// </summary>
    private async Task<List<MarketBar>> ProcessJsonFileAsync(string jsonFilePath)
    {
        var jsonContent = await File.ReadAllTextAsync(jsonFilePath);
        using var document = JsonDocument.Parse(jsonContent);
        
        var bars = new List<MarketBar>();
        
        if (document.RootElement.TryGetProperty("Time Series (5min)", out var timeSeries))
        {
            foreach (var kvp in timeSeries.EnumerateObject())
            {
                var timestampStr = kvp.Name;
                var barData = kvp.Value;

                if (DateTime.TryParseExact(timestampStr, "yyyy-MM-dd HH:mm:ss", 
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                {
                    var bar = new MarketBar
                    {
                        Symbol = "SPY",
                        Timestamp = timestamp,
                        Open = decimal.Parse(barData.GetProperty("1. open").GetString()!),
                        High = decimal.Parse(barData.GetProperty("2. high").GetString()!),
                        Low = decimal.Parse(barData.GetProperty("3. low").GetString()!),
                        Close = decimal.Parse(barData.GetProperty("4. close").GetString()!),
                        Volume = long.Parse(barData.GetProperty("5. volume").GetString()!),
                        Granularity = "5m"
                    };
                    
                    bars.Add(bar);
                }
            }
        }

        return bars;
    }

    /// <summary>
    /// High-performance bulk insert with transaction optimization
    /// </summary>
    private async Task BulkInsertBarsAsync(List<MarketBar> bars)
    {
        _logger?.LogInformation("💾 Bulk inserting {Count} bars into SQLite...", bars.Count);
        
        using var connection = new SqliteConnection($"Data Source={_sqliteDbPath}");
        await connection.OpenAsync();

        using var transaction = await connection.BeginTransactionAsync();
        
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO market_bars 
                (symbol, timestamp, open, high, low, close, volume, granularity) 
                VALUES (@symbol, @timestamp, @open, @high, @low, @close, @volume, @granularity)";

            var symbolParam = cmd.CreateParameter(); symbolParam.ParameterName = "@symbol"; cmd.Parameters.Add(symbolParam);
            var timestampParam = cmd.CreateParameter(); timestampParam.ParameterName = "@timestamp"; cmd.Parameters.Add(timestampParam);
            var openParam = cmd.CreateParameter(); openParam.ParameterName = "@open"; cmd.Parameters.Add(openParam);
            var highParam = cmd.CreateParameter(); highParam.ParameterName = "@high"; cmd.Parameters.Add(highParam);
            var lowParam = cmd.CreateParameter(); lowParam.ParameterName = "@low"; cmd.Parameters.Add(lowParam);
            var closeParam = cmd.CreateParameter(); closeParam.ParameterName = "@close"; cmd.Parameters.Add(closeParam);
            var volumeParam = cmd.CreateParameter(); volumeParam.ParameterName = "@volume"; cmd.Parameters.Add(volumeParam);
            var granularityParam = cmd.CreateParameter(); granularityParam.ParameterName = "@granularity"; cmd.Parameters.Add(granularityParam);

            const int batchSize = 1000;
            var batchCount = 0;
            
            foreach (var bar in bars)
            {
                symbolParam.Value = bar.Symbol;
                timestampParam.Value = bar.Timestamp;
                openParam.Value = (double)bar.Open;
                highParam.Value = (double)bar.High;
                lowParam.Value = (double)bar.Low;
                closeParam.Value = (double)bar.Close;
                volumeParam.Value = bar.Volume;
                granularityParam.Value = bar.Granularity;

                await cmd.ExecuteNonQueryAsync();
                batchCount++;

                if (batchCount % batchSize == 0)
                {
                    _logger?.LogDebug("💾 Inserted {Count}/{Total} bars", batchCount, bars.Count);
                }
            }

            await transaction.CommitAsync();
            _logger?.LogInformation("✅ Successfully inserted {Count} bars", bars.Count);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

/// <summary>
/// Market bar data structure matching SQLite schema
/// </summary>
public record MarketBar
{
    public required string Symbol { get; init; }
    public required DateTime Timestamp { get; init; }
    public required decimal Open { get; init; }
    public required decimal High { get; init; }
    public required decimal Low { get; init; }
    public required decimal Close { get; init; }
    public required long Volume { get; init; }
    public required string Granularity { get; init; }
}

/// <summary>
/// Conversion result tracking
/// </summary>
public record ConversionResult
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public required string JsonDataPath { get; init; }
    public required string SqliteDbPath { get; init; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public int TotalBars { get; set; }
    public DateTime? FirstBarDate { get; set; }
    public DateTime? LastBarDate { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> FailedFiles { get; set; } = new();
}