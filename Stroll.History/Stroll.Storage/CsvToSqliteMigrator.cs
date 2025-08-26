using System.Globalization;

namespace Stroll.Storage;

/// <summary>
/// Utility to migrate CSV data to SQLite database for improved performance
/// </summary>
public sealed class CsvToSqliteMigrator
{
    private readonly SqliteStorage _sqliteStorage;
    
    public CsvToSqliteMigrator(SqliteStorage sqliteStorage)
    {
        _sqliteStorage = sqliteStorage;
    }

    /// <summary>
    /// Migrate all CSV files in the data directory to SQLite
    /// </summary>
    public async Task MigrateAllCsvFilesAsync(string dataDirectory)
    {
        Console.WriteLine($"Starting CSV to SQLite migration from: {dataDirectory}");
        
        var csvFiles = Directory.GetFiles(dataDirectory, "*.csv", SearchOption.AllDirectories);
        var totalFiles = csvFiles.Length;
        var completed = 0;
        
        foreach (var csvFile in csvFiles)
        {
            try
            {
                var symbol = ExtractSymbolFromPath(csvFile);
                if (!string.IsNullOrEmpty(symbol))
                {
                    Console.WriteLine($"Migrating {symbol} from {Path.GetFileName(csvFile)}...");
                    await MigrateCsvFileAsync(csvFile, symbol);
                    completed++;
                    Console.WriteLine($"  ✓ {symbol} migrated ({completed}/{totalFiles})");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ Failed to migrate {csvFile}: {ex.Message}");
            }
        }
        
        // Print summary
        var stats = _sqliteStorage.GetDatabaseStats();
        Console.WriteLine($"\nMigration completed:");
        Console.WriteLine($"  Files processed: {completed}/{totalFiles}");
        Console.WriteLine($"  Total bars imported: {stats["total_bars"]}");
        Console.WriteLine($"  Database size: {stats["database_size_mb"]:F2} MB");
        
        if (stats["symbol_counts"] is Dictionary<string, int> symbolCounts)
        {
            Console.WriteLine($"  Symbols imported:");
            foreach (var (symbol, count) in symbolCounts.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"    {symbol}: {count:N0} bars");
            }
        }
    }

    /// <summary>
    /// Migrate a single CSV file to SQLite
    /// </summary>
    public async Task MigrateCsvFileAsync(string csvPath, string symbol)
    {
        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException($"CSV file not found: {csvPath}");
        }

        var bars = new List<Dictionary<string, object?>>();
        var lines = await File.ReadAllLinesAsync(csvPath);
        
        // Skip header if present
        var startIndex = lines.Length > 0 && IsHeaderLine(lines[0]) ? 1 : 0;
        
        for (int i = startIndex; i < lines.Length; i++)
        {
            if (TryParseCsvLine(lines[i], out var bar))
            {
                bars.Add(bar);
            }
        }
        
        if (bars.Count > 0)
        {
            await _sqliteStorage.BulkInsertBarsAsync(symbol, bars);
        }
    }

    private static bool IsHeaderLine(string line)
    {
        return line.Contains("Date") || line.Contains("Open") || line.Contains("High") || 
               line.Contains("Low") || line.Contains("Close") || line.Contains("Volume");
    }

    private static bool TryParseCsvLine(ReadOnlySpan<char> line, out Dictionary<string, object?> bar)
    {
        bar = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        
        if (line.IsEmpty || line.StartsWith("Date"))
        {
            return false;
        }

        var parts = line.ToString().Split(',');
        if (parts.Length < 6)
        {
            return false;
        }

        try
        {
            // Parse CSV format: Date,Open,High,Low,Close,Volume
            if (DateTime.TryParse(parts[0], out var date))
            {
                // Normalize to market close time (4:00 PM ET = 13:30 UTC for most of year)
                bar["t"] = DateTime.SpecifyKind(date.Date.AddHours(13).AddMinutes(30), DateTimeKind.Utc);
            }
            else
            {
                return false; // Skip rows with invalid dates
            }

            if (decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var open))
                bar["o"] = open;
            else return false;
            
            if (decimal.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var high))
                bar["h"] = high;
            else return false;
            
            if (decimal.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var low))
                bar["l"] = low;
            else return false;
            
            if (decimal.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var close))
                bar["c"] = close;
            else return false;
            
            if (long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var volume))
                bar["v"] = volume;
            else return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractSymbolFromPath(string csvPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(csvPath);
        
        // Handle various naming patterns:
        // SPY_20050101_20250815.csv -> SPY
        // SPY_historical_data.csv -> SPY
        // SPY.csv -> SPY
        
        var underscoreIndex = fileName.IndexOf('_');
        if (underscoreIndex > 0)
        {
            return fileName[..underscoreIndex].ToUpperInvariant();
        }
        
        return fileName.ToUpperInvariant();
    }

    /// <summary>
    /// Validate migrated data integrity against original CSV
    /// </summary>
    public async Task<bool> ValidateMigrationAsync(string csvPath, string symbol)
    {
        Console.WriteLine($"Validating migration for {symbol}...");
        
        // Count original CSV records
        var lines = await File.ReadAllLinesAsync(csvPath);
        var csvCount = 0;
        var startIndex = lines.Length > 0 && IsHeaderLine(lines[0]) ? 1 : 0;
        
        for (int i = startIndex; i < lines.Length; i++)
        {
            if (TryParseCsvLine(lines[i], out _))
            {
                csvCount++;
            }
        }
        
        // Count SQLite records
        var sqliteBars = await _sqliteStorage.GetBarsRawAsync(symbol, DateOnly.MinValue, DateOnly.MaxValue, Granularity.Daily);
        var sqliteCount = sqliteBars.Count;
        
        var isValid = csvCount == sqliteCount;
        
        Console.WriteLine($"  CSV records: {csvCount}");
        Console.WriteLine($"  SQLite records: {sqliteCount}");
        Console.WriteLine($"  Validation: {(isValid ? "✓ PASSED" : "✗ FAILED")}");
        
        return isValid;
    }
}