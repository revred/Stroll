using Microsoft.Data.Sqlite;
using System.Collections.Concurrent;
using System.Globalization;

namespace Stroll.Storage;

/// <summary>
/// High-performance SQLite storage provider for market data.
/// Provides sub-millisecond access with proper indexing and prepared statements.
/// </summary>
public sealed class SqliteStorage : IStorageProvider, IDisposable
{
    public DataCatalog Catalog { get; }

    private readonly string _databasePath;
    private readonly ConcurrentDictionary<string, CachedResponse> _responseCache = new();
    private readonly object _dbLock = new();
    private SqliteConnection? _connection;

    public SqliteStorage(DataCatalog catalog)
    {
        Catalog = catalog;
        _databasePath = Path.Combine(AppContext.BaseDirectory, "data", "market_data.db");
        
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        
        lock (_dbLock)
        {
            _connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared");
            _connection.Open();

            // Enable WAL mode for better concurrent access
            using var walCmd = _connection.CreateCommand();
            walCmd.CommandText = "PRAGMA journal_mode=WAL";
            walCmd.ExecuteNonQuery();

            // Create tables if they don't exist
            CreateTables();
            CreateIndices();
        }
    }

    private void CreateTables()
    {
        using var cmd = _connection!.CreateCommand();
        
        // Market data bars table
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS market_bars (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                timestamp DATETIME NOT NULL,
                open REAL NOT NULL,
                high REAL NOT NULL,
                low REAL NOT NULL,
                close REAL NOT NULL,
                volume INTEGER NOT NULL,
                granularity TEXT NOT NULL DEFAULT '1d',
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(symbol, timestamp, granularity)
            )";
        cmd.ExecuteNonQuery();

        // Options chain table
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS options_chain (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                expiry DATE NOT NULL,
                right TEXT NOT NULL,
                strike REAL NOT NULL,
                bid REAL,
                ask REAL,
                mid REAL,
                delta REAL,
                gamma REAL,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(symbol, expiry, right, strike)
            )";
        cmd.ExecuteNonQuery();
    }

    private void CreateIndices()
    {
        var indices = new[]
        {
            "CREATE INDEX IF NOT EXISTS idx_market_bars_symbol_timestamp ON market_bars(symbol, timestamp)",
            "CREATE INDEX IF NOT EXISTS idx_market_bars_symbol_granularity ON market_bars(symbol, granularity)",
            "CREATE INDEX IF NOT EXISTS idx_market_bars_timestamp ON market_bars(timestamp)",
            "CREATE INDEX IF NOT EXISTS idx_options_symbol_expiry ON options_chain(symbol, expiry)",
            "CREATE INDEX IF NOT EXISTS idx_options_expiry ON options_chain(expiry)"
        };

        using var cmd = _connection!.CreateCommand();
        foreach (var indexSql in indices)
        {
            cmd.CommandText = indexSql;
            cmd.ExecuteNonQuery();
        }
    }

    public async Task<IReadOnlyList<IDictionary<string, object?>>> GetBarsRawAsync(string symbol, DateOnly from, DateOnly to, Granularity g)
    {
        var cacheKey = $"bars_{symbol}_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}_{g.Canon()}";
        
        // Check cache first (target: <1ms for cache hits)
        if (_responseCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            return cached.Data;
        }

        var data = await GetBarsFromSqliteAsync(symbol, from, to, g);
        
        // Cache the result with 5-minute TTL
        _responseCache[cacheKey] = new CachedResponse(data, TimeSpan.FromMinutes(5));
        
        return data;
    }

    private async Task<List<IDictionary<string, object?>>> GetBarsFromSqliteAsync(string symbol, DateOnly from, DateOnly to, Granularity g)
    {
        var result = new List<IDictionary<string, object?>>();
        
        lock (_dbLock)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT symbol, timestamp, open, high, low, close, volume, granularity
                FROM market_bars 
                WHERE symbol = $symbol 
                    AND date(timestamp) >= $from 
                    AND date(timestamp) <= $to
                    AND granularity = $granularity
                ORDER BY timestamp";

            cmd.Parameters.AddWithValue("$symbol", symbol);
            cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$granularity", g.Canon());

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var bar = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["symbol"] = reader["symbol"],
                    ["t"] = reader["timestamp"],
                    ["o"] = Convert.ToDecimal(reader["open"]),
                    ["h"] = Convert.ToDecimal(reader["high"]),
                    ["l"] = Convert.ToDecimal(reader["low"]),
                    ["c"] = Convert.ToDecimal(reader["close"]),
                    ["v"] = Convert.ToInt64(reader["volume"])
                };
                
                result.Add(bar);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<IDictionary<string, object?>>> GetOptionsChainRawAsync(string symbol, DateOnly expiry)
    {
        var cacheKey = $"options_{symbol}_{expiry:yyyy-MM-dd}";
        
        if (_responseCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            return cached.Data;
        }

        var data = await GetOptionsFromSqliteAsync(symbol, expiry);
        _responseCache[cacheKey] = new CachedResponse(data, TimeSpan.FromMinutes(10));
        
        return data;
    }

    private async Task<List<IDictionary<string, object?>>> GetOptionsFromSqliteAsync(string symbol, DateOnly expiry)
    {
        var result = new List<IDictionary<string, object?>>();
        
        lock (_dbLock)
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                SELECT symbol, expiry, right, strike, bid, ask, mid, delta, gamma
                FROM options_chain 
                WHERE symbol = $symbol AND expiry = $expiry
                ORDER BY right, strike";

            cmd.Parameters.AddWithValue("$symbol", symbol);
            cmd.Parameters.AddWithValue("$expiry", expiry.ToString("yyyy-MM-dd"));

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var option = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["symbol"] = reader["symbol"],
                    ["expiry"] = reader["expiry"],
                    ["right"] = reader["right"],
                    ["strike"] = Convert.ToDecimal(reader["strike"]),
                    ["bid"] = reader["bid"] == DBNull.Value ? null : Convert.ToDecimal(reader["bid"]),
                    ["ask"] = reader["ask"] == DBNull.Value ? null : Convert.ToDecimal(reader["ask"]),
                    ["mid"] = reader["mid"] == DBNull.Value ? null : Convert.ToDecimal(reader["mid"]),
                    ["delta"] = reader["delta"] == DBNull.Value ? null : Convert.ToDecimal(reader["delta"]),
                    ["gamma"] = reader["gamma"] == DBNull.Value ? null : Convert.ToDecimal(reader["gamma"])
                };
                
                result.Add(option);
            }
        }

        return result;
    }

    /// <summary>
    /// Bulk insert market data from CSV for migration
    /// </summary>
    public async Task BulkInsertBarsAsync(string symbol, IEnumerable<Dictionary<string, object?>> bars, string granularity = "1d")
    {
        lock (_dbLock)
        {
            using var transaction = _connection!.BeginTransaction();
            
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO market_bars 
                (symbol, timestamp, open, high, low, close, volume, granularity)
                VALUES ($symbol, $timestamp, $open, $high, $low, $close, $volume, $granularity)";

            foreach (var bar in bars)
            {
                cmd.Parameters.Clear();
                cmd.Parameters.AddWithValue("$symbol", symbol);
                cmd.Parameters.AddWithValue("$timestamp", bar["t"]);
                cmd.Parameters.AddWithValue("$open", bar["o"]);
                cmd.Parameters.AddWithValue("$high", bar["h"]);
                cmd.Parameters.AddWithValue("$low", bar["l"]);
                cmd.Parameters.AddWithValue("$close", bar["c"]);
                cmd.Parameters.AddWithValue("$volume", bar["v"]);
                cmd.Parameters.AddWithValue("$granularity", granularity);
                
                cmd.ExecuteNonQuery();
            }
            
            transaction.Commit();
        }
    }

    /// <summary>
    /// Get database statistics for monitoring
    /// </summary>
    public Dictionary<string, object> GetDatabaseStats()
    {
        lock (_dbLock)
        {
            var stats = new Dictionary<string, object>();
            
            using var cmd = _connection!.CreateCommand();
            
            // Count bars by symbol
            cmd.CommandText = "SELECT symbol, COUNT(*) as count FROM market_bars GROUP BY symbol ORDER BY count DESC";
            using var reader = cmd.ExecuteReader();
            var symbolCounts = new Dictionary<string, int>();
            while (reader.Read())
            {
                symbolCounts[reader["symbol"].ToString()!] = Convert.ToInt32(reader["count"]);
            }
            reader.Close();
            
            stats["symbol_counts"] = symbolCounts;
            stats["total_bars"] = symbolCounts.Values.Sum();
            stats["database_size_mb"] = new FileInfo(_databasePath).Length / (1024.0 * 1024.0);
            
            return stats;
        }
    }

    public void Dispose()
    {
        lock (_dbLock)
        {
            _connection?.Dispose();
        }
    }
}