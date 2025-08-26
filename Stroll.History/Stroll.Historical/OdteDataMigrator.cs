using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Stroll.Storage;
using System.Globalization;

namespace Stroll.Historical;

/// <summary>
/// High-performance data migrator from ODTE SQLite database to Stroll optimized format
/// Extracts 20 years of proven market data and converts to Stroll's storage system
/// </summary>
public class OdteDataMigrator
{
    private readonly string _odteDbPath;
    private readonly IStorageProvider _strollStorage;
    private readonly ILogger<OdteDataMigrator>? _logger;

    public OdteDataMigrator(string odteDbPath, IStorageProvider strollStorage, ILogger<OdteDataMigrator>? logger = null)
    {
        _odteDbPath = odteDbPath ?? throw new ArgumentNullException(nameof(odteDbPath));
        _strollStorage = strollStorage ?? throw new ArgumentNullException(nameof(strollStorage));
        _logger = logger;
    }

    /// <summary>
    /// Migrate all data from ODTE database to Stroll format
    /// </summary>
    public async Task<MigrationResult> MigrateAllDataAsync()
    {
        _logger?.LogInformation("🚀 Starting ODTE to Stroll data migration");
        
        var result = new MigrationResult
        {
            StartTime = DateTime.UtcNow,
            OdteDbPath = _odteDbPath
        };

        try
        {
            // Check if ODTE database exists
            if (!File.Exists(_odteDbPath))
            {
                throw new FileNotFoundException($"ODTE database not found: {_odteDbPath}");
            }

            // Get available symbols from ODTE database
            var symbols = await GetAvailableSymbolsAsync();
            _logger?.LogInformation("📊 Found {Count} symbols in ODTE database", symbols.Count);

            result.TotalSymbols = symbols.Count;

            foreach (var symbol in symbols)
            {
                try
                {
                    _logger?.LogInformation("🔄 Migrating {Symbol}...", symbol.Symbol);
                    
                    var migrationStats = await MigrateSymbolAsync(symbol);
                    result.SymbolResults[symbol.Symbol] = migrationStats;
                    result.TotalRecords += migrationStats.RecordCount;
                    
                    _logger?.LogInformation("✅ Migrated {Symbol}: {Records} records from {StartDate} to {EndDate}",
                        symbol.Symbol, migrationStats.RecordCount, migrationStats.StartDate, migrationStats.EndDate);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "❌ Failed to migrate {Symbol}", symbol.Symbol);
                    result.FailedSymbols.Add(symbol.Symbol);
                }
            }

            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            result.Success = result.FailedSymbols.Count == 0;

            _logger?.LogInformation("🎯 Migration complete: {Records} records across {Symbols} symbols in {Duration}",
                result.TotalRecords, result.TotalSymbols, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            result.Success = false;
            result.ErrorMessage = ex.Message;
            
            _logger?.LogError(ex, "💥 Migration failed");
            throw;
        }
    }

    /// <summary>
    /// Get all available symbols from ODTE database
    /// </summary>
    private async Task<List<OdteSymbolInfo>> GetAvailableSymbolsAsync()
    {
        var symbols = new List<OdteSymbolInfo>();

        using var connection = new SqliteConnection($"Data Source={_odteDbPath}");
        await connection.OpenAsync();

        // Query symbols table
        const string sql = @"
            SELECT s.id, s.symbol, s.description,
                   MIN(m.timestamp) as first_timestamp,
                   MAX(m.timestamp) as last_timestamp,
                   COUNT(m.id) as record_count
            FROM symbols s
            JOIN market_data m ON s.id = m.symbol_id
            GROUP BY s.id, s.symbol, s.description
            ORDER BY s.symbol";

        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            symbols.Add(new OdteSymbolInfo
            {
                Id = reader.GetInt32("id"),
                Symbol = reader.GetString("symbol"),
                Description = reader.IsDBNull("description") ? "" : reader.GetString("description"),
                FirstTimestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64("first_timestamp")).DateTime,
                LastTimestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64("last_timestamp")).DateTime,
                RecordCount = reader.GetInt64("record_count")
            });
        }

        return symbols;
    }

    /// <summary>
    /// Migrate a single symbol's data from ODTE to Stroll format
    /// </summary>
    private async Task<SymbolMigrationResult> MigrateSymbolAsync(OdteSymbolInfo symbol)
    {
        var result = new SymbolMigrationResult
        {
            Symbol = symbol.Symbol,
            StartDate = symbol.FirstTimestamp,
            EndDate = symbol.LastTimestamp
        };

        using var connection = new SqliteConnection($"Data Source={_odteDbPath}");
        await connection.OpenAsync();

        // Query market data for this symbol
        const string sql = @"
            SELECT timestamp, open_price, high_price, low_price, close_price, volume, vwap_price
            FROM market_data
            WHERE symbol_id = @symbolId
            ORDER BY timestamp";

        var strollBars = new List<Dictionary<string, object?>>();

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@symbolId", symbol.Id);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64("timestamp")).DateTime;
            
            // Convert ODTE's compressed prices back to decimals (divided by 10000)
            var open = reader.GetInt32("open_price") / 10000.0m;
            var high = reader.GetInt32("high_price") / 10000.0m;
            var low = reader.GetInt32("low_price") / 10000.0m;
            var close = reader.GetInt32("close_price") / 10000.0m;
            var volume = reader.GetInt64("volume");
            var vwap = reader.GetInt32("vwap_price") / 10000.0m;

            strollBars.Add(new Dictionary<string, object?>
            {
                ["t"] = timestamp,
                ["o"] = open,
                ["h"] = high,
                ["l"] = low,
                ["c"] = close,
                ["v"] = volume,
                ["vwap"] = vwap
            });
        }

        result.RecordCount = strollBars.Count;

        if (strollBars.Count > 0)
        {
            // Store in Stroll storage system
            await StoreInStrollAsync(symbol.Symbol, strollBars);
        }

        return result;
    }

    /// <summary>
    /// Store data in Stroll's optimized storage system
    /// </summary>
    private async Task StoreInStrollAsync(string symbol, List<Dictionary<string, object?>> bars)
    {
        // For now, we'll store via the CSV storage system since we don't have a direct bulk insert
        // In production, we'd implement a direct SQLite bulk insert method

        var strollPath = Path.Combine(_strollStorage.Catalog.Root, $"{symbol}_migrated_from_odte.csv");
        
        // Create CSV content
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("timestamp,open,high,low,close,volume,vwap");

        foreach (var bar in bars)
        {
            var timestamp = (DateTime)bar["t"]!;
            var open = (decimal)bar["o"]!;
            var high = (decimal)bar["h"]!;
            var low = (decimal)bar["l"]!;
            var close = (decimal)bar["c"]!;
            var volume = (long)bar["v"]!;
            var vwap = (decimal)bar["vwap"]!;

            csv.AppendLine($"{timestamp:yyyy-MM-dd HH:mm:ss},{open},{high},{low},{close},{volume},{vwap}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(strollPath)!);
        await File.WriteAllTextAsync(strollPath, csv.ToString());

        _logger?.LogDebug("💾 Stored {Records} records for {Symbol} in {Path}",
            bars.Count, symbol, strollPath);
    }
}

// Supporting data structures
public record OdteSymbolInfo
{
    public required int Id { get; init; }
    public required string Symbol { get; init; }
    public required string Description { get; init; }
    public required DateTime FirstTimestamp { get; init; }
    public required DateTime LastTimestamp { get; init; }
    public required long RecordCount { get; init; }
}

public record SymbolMigrationResult
{
    public required string Symbol { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public int RecordCount { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

public record MigrationResult
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public required string OdteDbPath { get; init; }
    public int TotalSymbols { get; set; }
    public int TotalRecords { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, SymbolMigrationResult> SymbolResults { get; set; } = new();
    public List<string> FailedSymbols { get; set; } = new();
}