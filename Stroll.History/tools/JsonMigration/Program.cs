using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Diagnostics;

namespace JsonMigration;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("🚀 COMPREHENSIVE JSON TO SQLITE MIGRATION");
        Console.WriteLine("==========================================");
        Console.WriteLine("Migrating 47 months of SPY 5-minute data to hyperfast SQLite storage");
        Console.WriteLine("");

        var stopwatch = Stopwatch.StartNew();
        var acquiredDataPath = Path.Combine("..", "acquired_data");
        var dbPath = Path.Combine("..", "data", "consolidated_backtest.db");

        Console.WriteLine($"📁 Source: {Path.GetFullPath(acquiredDataPath)}");
        Console.WriteLine($"🗄️ Target: {Path.GetFullPath(dbPath)}");

        if (!Directory.Exists(acquiredDataPath))
        {
            Console.WriteLine("❌ ERROR: acquired_data directory not found!");
            return 1;
        }

        // Ensure target directory exists
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        try
        {
            // Initialize database with optimized schema
            await InitializeDatabaseAsync(dbPath);
            
            // Get all JSON files sorted by date
            var jsonFiles = Directory.GetFiles(acquiredDataPath, "SPY_*.json")
                .OrderBy(f => ExtractDateFromFilename(f))
                .ToList();

            Console.WriteLine($"📊 Found {jsonFiles.Count} JSON files to process");
            Console.WriteLine("");

            var totalBars = 0;
            var processedFiles = 0;
            var failedFiles = 0;

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            // Begin transaction for performance
            using var transaction = connection.BeginTransaction();

            foreach (var jsonFile in jsonFiles)
            {
                try
                {
                    Console.Write($"📥 Processing {Path.GetFileName(jsonFile)}...");
                    var barsInserted = await ProcessJsonFileAsync(connection, transaction, jsonFile);
                    totalBars += barsInserted;
                    processedFiles++;
                    Console.WriteLine($" {barsInserted:N0} bars");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" ❌ FAILED: {ex.Message}");
                    failedFiles++;
                }
            }

            // Commit transaction
            await transaction.CommitAsync();

            // Create indexes for hyperfast access
            Console.WriteLine("");
            Console.WriteLine("🚄 Creating performance indexes...");
            await CreatePerformanceIndexesAsync(connection);

            stopwatch.Stop();

            Console.WriteLine("");
            Console.WriteLine("✅ MIGRATION COMPLETED!");
            Console.WriteLine("======================");
            Console.WriteLine($"📊 Files Processed: {processedFiles}/{jsonFiles.Count}");
            Console.WriteLine($"📊 Total Bars Migrated: {totalBars:N0}");
            Console.WriteLine($"📊 Failed Files: {failedFiles}");
            Console.WriteLine($"⏱️ Total Time: {stopwatch.ElapsedMilliseconds:N0}ms");
            Console.WriteLine($"🚄 Processing Rate: {totalBars / (stopwatch.ElapsedMilliseconds / 1000.0):F0} bars/second");
            Console.WriteLine("");

            // Verify database integrity
            Console.WriteLine("🔍 Verifying database integrity...");
            await VerifyDatabaseAsync(connection);

            Console.WriteLine("🎯 Hyperfast SQLite storage ready for backtesting!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 MIGRATION FAILED: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return 1;
        }
    }

    static async Task InitializeDatabaseAsync(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // Drop existing table if it exists
        var dropSql = "DROP TABLE IF EXISTS market_bars";
        using var dropCommand = new SqliteCommand(dropSql, connection);
        await dropCommand.ExecuteNonQueryAsync();

        // Create optimized table schema
        var createSql = @"
            CREATE TABLE market_bars (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                symbol TEXT NOT NULL,
                timestamp DATETIME NOT NULL,
                open REAL NOT NULL,
                high REAL NOT NULL,
                low REAL NOT NULL,
                close REAL NOT NULL,
                volume INTEGER NOT NULL,
                date_only DATE GENERATED ALWAYS AS (date(timestamp)) STORED
            )";
        
        using var createCommand = new SqliteCommand(createSql, connection);
        await createCommand.ExecuteNonQueryAsync();

        // Set SQLite performance optimizations
        var pragmas = new[]
        {
            "PRAGMA journal_mode = WAL",
            "PRAGMA synchronous = NORMAL", 
            "PRAGMA cache_size = 1000000",
            "PRAGMA temp_store = MEMORY",
            "PRAGMA mmap_size = 268435456"
        };

        foreach (var pragma in pragmas)
        {
            using var pragmaCommand = new SqliteCommand(pragma, connection);
            await pragmaCommand.ExecuteNonQueryAsync();
        }
    }

    static async Task<int> ProcessJsonFileAsync(SqliteConnection connection, SqliteTransaction transaction, string jsonFile)
    {
        var jsonContent = await File.ReadAllTextAsync(jsonFile);
        var jsonDoc = JsonDocument.Parse(jsonContent);
        
        if (!jsonDoc.RootElement.TryGetProperty("Time Series (5min)", out var timeSeries))
        {
            throw new InvalidOperationException("No 'Time Series (5min)' property found");
        }

        var insertSql = @"
            INSERT INTO market_bars (symbol, timestamp, open, high, low, close, volume)
            VALUES (@symbol, @timestamp, @open, @high, @low, @close, @volume)";

        using var insertCommand = new SqliteCommand(insertSql, connection, transaction);
        
        // Add parameters
        insertCommand.Parameters.Add("@symbol", SqliteType.Text);
        insertCommand.Parameters.Add("@timestamp", SqliteType.Text);
        insertCommand.Parameters.Add("@open", SqliteType.Real);
        insertCommand.Parameters.Add("@high", SqliteType.Real);
        insertCommand.Parameters.Add("@low", SqliteType.Real);
        insertCommand.Parameters.Add("@close", SqliteType.Real);
        insertCommand.Parameters.Add("@volume", SqliteType.Integer);

        var barsInserted = 0;

        foreach (var bar in timeSeries.EnumerateObject())
        {
            var timestamp = bar.Name;
            var ohlcv = bar.Value;

            insertCommand.Parameters["@symbol"].Value = "SPY";
            insertCommand.Parameters["@timestamp"].Value = timestamp;
            insertCommand.Parameters["@open"].Value = double.Parse(ohlcv.GetProperty("1. open").GetString()!);
            insertCommand.Parameters["@high"].Value = double.Parse(ohlcv.GetProperty("2. high").GetString()!);
            insertCommand.Parameters["@low"].Value = double.Parse(ohlcv.GetProperty("3. low").GetString()!);
            insertCommand.Parameters["@close"].Value = double.Parse(ohlcv.GetProperty("4. close").GetString()!);
            insertCommand.Parameters["@volume"].Value = long.Parse(ohlcv.GetProperty("5. volume").GetString()!);

            await insertCommand.ExecuteNonQueryAsync();
            barsInserted++;
        }

        return barsInserted;
    }

    static async Task CreatePerformanceIndexesAsync(SqliteConnection connection)
    {
        var indexes = new[]
        {
            "CREATE INDEX IF NOT EXISTS idx_symbol_timestamp ON market_bars(symbol, timestamp)",
            "CREATE INDEX IF NOT EXISTS idx_timestamp ON market_bars(timestamp)",
            "CREATE INDEX IF NOT EXISTS idx_date_only ON market_bars(date_only)",
            "CREATE INDEX IF NOT EXISTS idx_symbol_date ON market_bars(symbol, date_only)"
        };

        foreach (var index in indexes)
        {
            using var indexCommand = new SqliteCommand(index, connection);
            await indexCommand.ExecuteNonQueryAsync();
        }
    }

    static async Task VerifyDatabaseAsync(SqliteConnection connection)
    {
        // Count total bars
        using var countCommand = new SqliteCommand("SELECT COUNT(*) FROM market_bars", connection);
        var totalBars = (long)(await countCommand.ExecuteScalarAsync())!;

        Console.WriteLine($"✅ Total bars in database: {totalBars:N0}");

        if (totalBars > 0)
        {
            // Get date range
            using var rangeCommand = new SqliteCommand(
                "SELECT MIN(timestamp), MAX(timestamp) FROM market_bars", connection);
            using var reader = await rangeCommand.ExecuteReaderAsync();
            
            if (await reader.ReadAsync())
            {
                var minDate = reader.IsDBNull(0) ? "Unknown" : reader.GetString(0);
                var maxDate = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1);
                
                Console.WriteLine($"✅ Date range: {minDate} to {maxDate}");
            }
        }
        else
        {
            Console.WriteLine("⚠️ No data found in database");
        }
    }

    static DateTime ExtractDateFromFilename(string filename)
    {
        // Extract date from filename like "SPY_2021_09_5min.json"
        var parts = Path.GetFileNameWithoutExtension(filename).Split('_');
        if (parts.Length >= 3 && int.TryParse(parts[1], out var year) && int.TryParse(parts[2], out var month))
        {
            return new DateTime(year, month, 1);
        }
        return DateTime.MinValue;
    }
}