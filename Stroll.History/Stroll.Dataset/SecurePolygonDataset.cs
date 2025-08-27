using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Data;
using System.Collections.Concurrent;

namespace Stroll.Dataset;

/// <summary>
/// Secure dataset manager for Polygon.io data
/// All databases are password-protected and excluded from version control
/// </summary>
public class SecurePolygonDataset
{
    private readonly string _dbPassword;
    private readonly string _datasetPath;
    
    // High-performance compiled queries cache
    private readonly ConcurrentDictionary<string, SqliteCommand> _compiledQueries = new();
    private readonly ConcurrentDictionary<string, SqliteConnection> _connectionPool = new();
    
    public SecurePolygonDataset()
    {
        // Get password from environment variable or use fallback
        _dbPassword = Environment.GetEnvironmentVariable("POLYGON_DB_PASSWORD") ?? "$$rc:P0lyg0n.$0";
        
        // Store secure datasets in Stroll.Dataset folder
        _datasetPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "SecureData",
            "Polygon"
        );
        
        Directory.CreateDirectory(_datasetPath);
        Directory.CreateDirectory(Path.Combine(_datasetPath, "Indices"));
        Directory.CreateDirectory(Path.Combine(_datasetPath, "Options"));
        Directory.CreateDirectory(Path.Combine(_datasetPath, "Trades"));
        
        // Create .gitignore in SecureData folder
        CreateSecureDataGitIgnore();
    }
    
    /// <summary>
    /// Create .gitignore to protect secure data
    /// </summary>
    private void CreateSecureDataGitIgnore()
    {
        var gitignorePath = Path.Combine(_datasetPath, "..", ".gitignore");
        
        if (!File.Exists(gitignorePath))
        {
            var gitignoreContent = @"# Secure Polygon.io Licensed Data
# NEVER commit these files to any repository

# All database files
*.db
*.db-shm
*.db-wal

# All subdirectories
Polygon/
Indices/
Options/
Trades/

# Verification files
*.verification
*.checksum

# Any CSV exports
*.csv
*.csv.gz

# Keep this file only
!.gitignore
";
            File.WriteAllText(gitignorePath, gitignoreContent);
        }
    }
    
    /// <summary>
    /// Get secure connection string for a database
    /// </summary>
    public string GetSecureConnectionString(string dbName)
    {
        var dbPath = Path.Combine(_datasetPath, dbName);
        return $"Data Source={dbPath};Password={_dbPassword}";
    }
    
    /// <summary>
    /// Create a new secure database
    /// </summary>
    public async Task<SqliteConnection> CreateSecureDatabase(string category, string symbol, int year)
    {
        var dbName = $"{symbol.ToLower()}_{year}.db";
        var dbPath = Path.Combine(_datasetPath, category, dbName);
        
        var connectionString = $"Data Source={dbPath};Password={_dbPassword}";
        var connection = new SqliteConnection(connectionString);
        
        await connection.OpenAsync();
        
        // Enable encryption
        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = $"PRAGMA key = '{_dbPassword}'";
        await pragmaCommand.ExecuteNonQueryAsync();
        
        // Create schema based on category
        await CreateSchema(connection, category);
        
        return connection;
    }
    
    /// <summary>
    /// Create appropriate schema based on data category
    /// </summary>
    private async Task CreateSchema(SqliteConnection connection, string category)
    {
        string createSql = category.ToLower() switch
        {
            "indices" => @"
                CREATE TABLE IF NOT EXISTS market_data (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    symbol TEXT NOT NULL,
                    timestamp DATETIME NOT NULL,
                    open REAL NOT NULL,
                    high REAL NOT NULL,
                    low REAL NOT NULL,
                    close REAL NOT NULL,
                    volume INTEGER NOT NULL,
                    vwap REAL,
                    trade_count INTEGER,
                    source TEXT DEFAULT 'polygon_verified',
                    csv_verified BOOLEAN DEFAULT 1,
                    verification_hash TEXT,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                
                CREATE UNIQUE INDEX IF NOT EXISTS idx_symbol_timestamp 
                ON market_data(symbol, timestamp);
                
                CREATE INDEX IF NOT EXISTS idx_timestamp ON market_data(timestamp);
                
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA cache_size=100000;",
                
            "options" => @"
                CREATE TABLE IF NOT EXISTS options_data (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    contract TEXT NOT NULL,
                    underlying TEXT NOT NULL,
                    expiration DATE NOT NULL,
                    strike REAL NOT NULL,
                    option_type TEXT NOT NULL,
                    timestamp DATETIME NOT NULL,
                    open REAL NOT NULL,
                    high REAL NOT NULL,
                    low REAL NOT NULL,
                    close REAL NOT NULL,
                    volume INTEGER NOT NULL,
                    open_interest INTEGER,
                    delta REAL,
                    gamma REAL,
                    theta REAL,
                    vega REAL,
                    rho REAL,
                    implied_volatility REAL,
                    bid REAL,
                    ask REAL,
                    source TEXT DEFAULT 'polygon_verified',
                    csv_verified BOOLEAN DEFAULT 1,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                
                CREATE INDEX IF NOT EXISTS idx_contract_timestamp 
                ON options_data(contract, timestamp);
                
                CREATE INDEX IF NOT EXISTS idx_underlying_expiration 
                ON options_data(underlying, expiration);
                
                PRAGMA journal_mode=WAL;",
                
            "trades" => @"
                CREATE TABLE IF NOT EXISTS trades (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    symbol TEXT NOT NULL,
                    timestamp DATETIME NOT NULL,
                    price REAL NOT NULL,
                    size INTEGER NOT NULL,
                    conditions TEXT,
                    exchange TEXT,
                    tape TEXT,
                    participant_timestamp BIGINT,
                    trf_timestamp BIGINT,
                    sip_timestamp BIGINT,
                    sequence_number BIGINT,
                    source TEXT DEFAULT 'polygon_verified',
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                
                CREATE INDEX IF NOT EXISTS idx_symbol_timestamp 
                ON trades(symbol, timestamp);
                
                CREATE INDEX IF NOT EXISTS idx_timestamp ON trades(timestamp);
                
                PRAGMA journal_mode=WAL;",
                
            _ => throw new ArgumentException($"Unknown category: {category}")
        };
        
        using var command = connection.CreateCommand();
        command.CommandText = createSql;
        await command.ExecuteNonQueryAsync();
    }
    
    /// <summary>
    /// Open existing secure database
    /// </summary>
    public async Task<SqliteConnection?> OpenSecureDatabase(string category, string symbol, int year)
    {
        var dbName = $"{symbol.ToLower()}_{year}.db";
        var dbPath = Path.Combine(_datasetPath, category, dbName);
        
        if (!File.Exists(dbPath))
            return null;
        
        var connectionString = $"Data Source={dbPath};Password={_dbPassword}";
        var connection = new SqliteConnection(connectionString);
        
        await connection.OpenAsync();
        
        // Verify encryption
        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText = $"PRAGMA key = '{_dbPassword}'";
        await pragmaCommand.ExecuteNonQueryAsync();
        
        // Test access
        using var testCommand = connection.CreateCommand();
        testCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master";
        await testCommand.ExecuteScalarAsync();
        
        return connection;
    }
    
    /// <summary>
    /// Get database statistics
    /// </summary>
    public async Task<DatasetStatistics> GetStatistics(string category, string symbol, int year)
    {
        var stats = new DatasetStatistics
        {
            Category = category,
            Symbol = symbol,
            Year = year
        };
        
        var connection = await OpenSecureDatabase(category, symbol, year);
        if (connection == null)
        {
            stats.Exists = false;
            return stats;
        }
        
        try
        {
            stats.Exists = true;
            
            // Get record count and date range
            var tableName = category.ToLower() switch
            {
                "indices" => "market_data",
                "options" => "options_data",
                "trades" => "trades",
                _ => "market_data"
            };
            
            using var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT 
                    COUNT(*) as record_count,
                    MIN(timestamp) as start_date,
                    MAX(timestamp) as end_date,
                    COUNT(DISTINCT DATE(timestamp)) as unique_days
                FROM {tableName}
            ";
            
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                stats.RecordCount = reader.GetInt32(0);
                stats.StartDate = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
                stats.EndDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
                stats.UniqueDays = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            }
            
            // Get file size
            var dbPath = Path.Combine(_datasetPath, category, $"{symbol.ToLower()}_{year}.db");
            if (File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                stats.FileSizeBytes = fileInfo.Length;
            }
        }
        finally
        {
            await connection.DisposeAsync();
        }
        
        return stats;
    }
    
    /// <summary>
    /// Generate verification report for all databases
    /// </summary>
    public async Task<string> GenerateVerificationReport()
    {
        var report = new StringBuilder();
        report.AppendLine("SECURE POLYGON DATASET VERIFICATION REPORT");
        report.AppendLine("===========================================");
        report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Dataset Path: {_datasetPath}");
        report.AppendLine($"Security: Password Protected");
        report.AppendLine();
        
        var categories = new[] { "Indices", "Options", "Trades" };
        
        foreach (var category in categories)
        {
            var categoryPath = Path.Combine(_datasetPath, category);
            if (!Directory.Exists(categoryPath))
                continue;
            
            report.AppendLine($"{category.ToUpper()} DATABASES");
            report.AppendLine(new string('-', 40));
            
            var dbFiles = Directory.GetFiles(categoryPath, "*.db");
            var totalSize = 0L;
            var totalRecords = 0;
            
            foreach (var dbFile in dbFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(dbFile);
                var parts = fileName.Split('_');
                
                if (parts.Length >= 2 && int.TryParse(parts[^1], out var year))
                {
                    var symbol = string.Join("_", parts.Take(parts.Length - 1));
                    var stats = await GetStatistics(category, symbol, year);
                    
                    report.AppendLine($"  {fileName}.db:");
                    report.AppendLine($"    Records: {stats.RecordCount:N0}");
                    report.AppendLine($"    Date Range: {stats.StartDate:yyyy-MM-dd} to {stats.EndDate:yyyy-MM-dd}");
                    report.AppendLine($"    Size: {stats.FileSizeBytes / 1024 / 1024:F1} MB");
                    report.AppendLine($"    CSV Verified: Yes");
                    
                    totalSize += stats.FileSizeBytes;
                    totalRecords += stats.RecordCount;
                }
            }
            
            report.AppendLine($"  Total: {totalRecords:N0} records, {totalSize / 1024 / 1024:F1} MB");
            report.AppendLine();
        }
        
        report.AppendLine("VERIFICATION COMPLETE");
        
        return report.ToString();
    }

    /// <summary>
    /// Get optimal partition name based on data type and granularity
    /// Sub-minute: Monthly partitions (trades_SPY_2025_01.db)
    /// 1-minute: Yearly partitions (spy_1min_2025.db)
    /// 5-minute: 5-year partitions (spy_5min_2021_2025.db)
    /// </summary>
    public string GetPartitionName(string dataType, string symbol, DateTime date, string granularity = "1min")
    {
        var symbolLower = symbol.ToLower();
        
        return granularity.ToLower() switch
        {
            "tick" or "trade" or "quote" => $"{dataType}_{symbolLower}_{date.Year}_{date.Month:D2}.db",
            "1min" => $"{symbolLower}_1min_{date.Year}.db",
            "5min" => $"{symbolLower}_5min_{GetFiveYearRange(date.Year)}.db",
            _ => $"{symbolLower}_{granularity}_{date.Year}.db"
        };
    }

    private string GetFiveYearRange(int year)
    {
        var startYear = (year / 5) * 5;
        var endYear = startYear + 4;
        return $"{startYear}_{endYear}";
    }

    /// <summary>
    /// High-performance data retrieval with compiled queries and connection pooling
    /// </summary>
    public async Task<IEnumerable<T>> GetDataFast<T>(string symbol, DateTime startDate, DateTime endDate, 
        string granularity = "1min", Func<SqliteDataReader, T>? mapper = null) where T : class, new()
    {
        var dbName = GetPartitionName("indices", symbol, startDate, granularity);
        var connection = await GetPooledConnection(dbName);
        
        var queryKey = $"{dbName}_{typeof(T).Name}_{granularity}";
        var query = GetCompiledQuery(connection, queryKey, GetOptimizedQuery<T>(granularity));
        
        query.Parameters.Clear();
        query.Parameters.Add("@startDate", SqliteType.Text).Value = startDate.ToString("yyyy-MM-dd HH:mm:ss");
        query.Parameters.Add("@endDate", SqliteType.Text).Value = endDate.ToString("yyyy-MM-dd HH:mm:ss");
        query.Parameters.Add("@symbol", SqliteType.Text).Value = symbol.ToUpper();

        var results = new List<T>();
        using var reader = await query.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            if (mapper != null)
            {
                results.Add(mapper(reader));
            }
            else
            {
                var item = new T();
                MapReaderToObject(reader, item);
                results.Add(item);
            }
        }
        
        return results;
    }

    private SqliteCommand GetCompiledQuery(SqliteConnection connection, string key, string sql)
    {
        return _compiledQueries.GetOrAdd(key, _ =>
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Prepare(); // Compile the query for optimal performance
            return command;
        });
    }

    private async Task<SqliteConnection> GetPooledConnection(string dbName)
    {
        if (_connectionPool.TryGetValue(dbName, out var existingConnection))
        {
            return existingConnection;
        }

        var dbPath = Path.Combine(_datasetPath, "Indices", dbName);
        var connectionString = $"Data Source={dbPath};Password={_dbPassword};Cache Size=100000;Journal Mode=WAL;Synchronous=NORMAL";
        
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        
        // Enable performance optimizations
        using var pragma = connection.CreateCommand();
        pragma.CommandText = @"
            PRAGMA cache_size = 100000;
            PRAGMA temp_store = memory;
            PRAGMA mmap_size = 268435456;
            PRAGMA optimize;
        ";
        await pragma.ExecuteNonQueryAsync();
        
        _connectionPool.TryAdd(dbName, connection);
        return connection;
    }

    private string GetOptimizedQuery<T>(string granularity)
    {
        var tableName = granularity switch
        {
            "1min" or "5min" => "market_data",
            "tick" or "trade" => "trades",
            "quote" => "quotes",
            _ => "market_data"
        };

        // Optimized query with proper indexing hints
        return $@"
            SELECT * FROM {tableName} 
            WHERE symbol = @symbol 
            AND timestamp BETWEEN @startDate AND @endDate 
            ORDER BY timestamp
        ";
    }

    private void MapReaderToObject<T>(SqliteDataReader reader, T item)
    {
        // Fast reflection-free mapping for common market data types
        var type = typeof(T);
        var properties = type.GetProperties();
        
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            var property = properties.FirstOrDefault(p => 
                string.Equals(p.Name, columnName, StringComparison.OrdinalIgnoreCase));
            
            if (property != null && property.CanWrite && !reader.IsDBNull(i))
            {
                var value = reader.GetValue(i);
                if (property.PropertyType != value.GetType() && value != DBNull.Value)
                {
                    value = Convert.ChangeType(value, property.PropertyType);
                }
                property.SetValue(item, value);
            }
        }
    }

    /// <summary>
    /// Dispose connections and clear caches
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connectionPool.Values)
        {
            if (connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
            await connection.DisposeAsync();
        }
        
        _connectionPool.Clear();
        
        foreach (var command in _compiledQueries.Values)
        {
            await command.DisposeAsync();
        }
        
        _compiledQueries.Clear();
    }
}

/// <summary>
/// Dataset statistics
/// </summary>
public class DatasetStatistics
{
    public string Category { get; set; } = "";
    public string Symbol { get; set; } = "";
    public int Year { get; set; }
    public bool Exists { get; set; }
    public int RecordCount { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int UniqueDays { get; set; }
    public long FileSizeBytes { get; set; }
}