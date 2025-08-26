using Stroll.Storage;

namespace Stroll.Storage;

/// <summary>
/// Command-line utility to migrate CSV data to SQLite
/// Usage: dotnet run --project MigrationUtility
/// </summary>
public class MigrationUtility
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Stroll.History CSV to SQLite Migration ===");
        Console.WriteLine();

        try
        {
            var dataPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data");
            if (args.Length > 0)
            {
                dataPath = args[0];
            }

            dataPath = Path.GetFullPath(dataPath);
            Console.WriteLine($"Data directory: {dataPath}");

            if (!Directory.Exists(dataPath))
            {
                Console.WriteLine($"❌ Data directory not found: {dataPath}");
                return;
            }

            // Initialize SQLite storage
            var catalog = DataCatalog.Default(dataPath);
            using var sqliteStorage = new SqliteStorage(catalog);
            var migrator = new CsvToSqliteMigrator(sqliteStorage);

            Console.WriteLine("🔄 Starting migration...");
            await migrator.MigrateAllCsvFilesAsync(dataPath);

            Console.WriteLine();
            Console.WriteLine("✅ Migration completed successfully!");
            
            // Show database statistics
            var stats = sqliteStorage.GetDatabaseStats();
            Console.WriteLine();
            Console.WriteLine("📊 Database Statistics:");
            Console.WriteLine($"  Total bars: {stats["total_bars"]:N0}");
            Console.WriteLine($"  Database size: {stats["database_size_mb"]:F2} MB");
            
            if (stats["symbol_counts"] is Dictionary<string, int> symbolCounts)
            {
                Console.WriteLine($"  Symbols: {symbolCounts.Count}");
                Console.WriteLine();
                Console.WriteLine("📈 Top symbols by data volume:");
                foreach (var (symbol, count) in symbolCounts.OrderByDescending(x => x.Value).Take(10))
                {
                    Console.WriteLine($"    {symbol}: {count:N0} bars");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Migration failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Environment.Exit(1);
        }
    }
}