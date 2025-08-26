using Microsoft.Extensions.Logging;
using Stroll.Storage;

namespace Stroll.Historical;

/// <summary>
/// Execute ODTE data migration to Stroll format
/// Migrates 20 years of proven market data from ODTE's SQLite database
/// </summary>
public class RunOdteDataMigration
{
    public static async Task Main(string[] args)
    {
        // Set up logging
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<RunOdteDataMigration>();
        
        logger.LogInformation("🚀 ODTE to Stroll Data Migration");
        logger.LogInformation("===================================");

        try
        {
            // ODTE database path
            var odteDbPath = @"C:\Code\ODTE\data\ODTE_TimeSeries_5Y.db";
            
            // Check if ODTE database exists
            if (!File.Exists(odteDbPath))
            {
                logger.LogError("❌ ODTE database not found at: {Path}", odteDbPath);
                logger.LogInformation("💡 Make sure ODTE data acquisition has been run first");
                Environment.Exit(1);
            }

            // Set up Stroll storage
            var dataPath = Path.GetFullPath("./data");
            Directory.CreateDirectory(dataPath);
            var catalog = DataCatalog.Default(dataPath);
            var strollStorage = new CompositeStorage(catalog);

            logger.LogInformation("📊 Source: {OdteDb}", odteDbPath);
            logger.LogInformation("💾 Target: {StrollPath}", dataPath);

            // Initialize migrator
            var migrator = new OdteDataMigrator(
                odteDbPath, 
                strollStorage, 
                loggerFactory.CreateLogger<OdteDataMigrator>());

            // Execute migration
            logger.LogInformation("🔄 Starting migration...");
            var result = await migrator.MigrateAllDataAsync();

            // Report results
            logger.LogInformation("✅ MIGRATION COMPLETE!");
            logger.LogInformation("======================");
            logger.LogInformation("📈 Total Records: {Records:N0}", result.TotalRecords);
            logger.LogInformation("🏷️ Total Symbols: {Symbols}", result.TotalSymbols);
            logger.LogInformation("⏱️ Duration: {Duration}", result.Duration);
            logger.LogInformation("💯 Success Rate: {Rate:P1} ({Success}/{Total})", 
                (result.TotalSymbols - result.FailedSymbols.Count) / (double)result.TotalSymbols,
                result.TotalSymbols - result.FailedSymbols.Count, 
                result.TotalSymbols);

            if (result.FailedSymbols.Count > 0)
            {
                logger.LogWarning("⚠️ Failed Symbols: {Failed}", string.Join(", ", result.FailedSymbols));
            }

            // Top 5 symbols by record count
            var topSymbols = result.SymbolResults
                .OrderByDescending(s => s.Value.RecordCount)
                .Take(5)
                .ToList();

            logger.LogInformation("🏆 Top 5 symbols by data volume:");
            foreach (var symbol in topSymbols)
            {
                var stats = symbol.Value;
                var years = (stats.EndDate - stats.StartDate).Days / 365.0;
                logger.LogInformation("   {Symbol}: {Records:N0} records ({Years:F1} years)",
                    symbol.Key, stats.RecordCount, years);
            }

            logger.LogInformation("🎯 Ready for backtesting with comprehensive market data!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 Migration failed");
            Environment.Exit(1);
        }
    }
}