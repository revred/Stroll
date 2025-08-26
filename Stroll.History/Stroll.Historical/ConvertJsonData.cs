using Microsoft.Extensions.Logging;

namespace Stroll.Historical;

/// <summary>
/// Convert acquired Alpha Vantage JSON data to SQLite for backtesting
/// Processes 22 months of SPY 5-minute bars (88,610 total bars)
/// </summary>
public class ConvertJsonData
{
    public static async Task Main(string[] args)
    {
        // Set up logging
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<ConvertJsonData>();
        
        logger.LogInformation("🚀 Starting Alpha Vantage JSON to SQLite conversion");
        logger.LogInformation("📊 Dataset: 22 months of SPY 5-minute bars (88,610 expected bars)");
        
        try
        {
            var jsonDataPath = Path.GetFullPath("acquired_data");
            var sqliteDbPath = Path.GetFullPath("data/expanded_backtest.db");
            
            logger.LogInformation("📁 JSON Source: {JsonPath}", jsonDataPath);
            logger.LogInformation("💾 SQLite Target: {SqlitePath}", sqliteDbPath);
            
            // Verify source data exists
            if (!Directory.Exists(jsonDataPath))
            {
                throw new DirectoryNotFoundException($"JSON data directory not found: {jsonDataPath}");
            }
            
            var jsonFiles = Directory.GetFiles(jsonDataPath, "SPY_*_5min.json");
            logger.LogInformation("📋 Found {Count} JSON files to process", jsonFiles.Length);
            
            // Initialize converter
            var converterLogger = loggerFactory.CreateLogger<JsonToSqliteConverter>();
            var converter = new JsonToSqliteConverter(jsonDataPath, sqliteDbPath, converterLogger);
            
            // Execute conversion
            var result = await converter.ConvertAllAsync();
            
            if (result.Success)
            {
                logger.LogInformation("✅ CONVERSION SUCCESSFUL!");
                logger.LogInformation("📊 RESULTS:");
                logger.LogInformation("   • Files Processed: {ProcessedFiles}/{TotalFiles}", result.ProcessedFiles, result.TotalFiles);
                logger.LogInformation("   • Total Bars: {TotalBars:N0}", result.TotalBars);
                logger.LogInformation("   • Date Range: {FirstDate} to {LastDate}", 
                    result.FirstBarDate?.ToString("yyyy-MM-dd HH:mm:ss"),
                    result.LastBarDate?.ToString("yyyy-MM-dd HH:mm:ss"));
                logger.LogInformation("   • Duration: {Duration}", result.Duration);
                logger.LogInformation("   • Database: {DbPath}", result.SqliteDbPath);
                
                // Verify against expected count
                var expectedBars = 88610;
                var coverage = (double)result.TotalBars / expectedBars;
                logger.LogInformation("   • Coverage: {Coverage:P1} ({Actual}/{Expected})", coverage, result.TotalBars, expectedBars);
                
                if (result.FailedFiles.Count == 0)
                {
                    logger.LogInformation("🎯 Ready for expanded backtest with {Months} months of data!", 
                        Math.Round((result.LastBarDate!.Value - result.FirstBarDate!.Value).Days / 30.0, 1));
                }
                else
                {
                    logger.LogWarning("⚠️ Some files failed: {FailedFiles}", string.Join(", ", result.FailedFiles));
                }
            }
            else
            {
                logger.LogError("❌ Conversion failed: {Error}", result.ErrorMessage);
                Environment.Exit(1);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 Conversion process failed");
            Environment.Exit(1);
        }
    }
}