using Microsoft.Extensions.Logging;
using Stroll.Storage;

namespace Stroll.Historical;

/// <summary>
/// Execute comprehensive data acquisition using Databento's institutional-grade data
/// Acquires 1-minute SPY/SPX data from 2000-2025 for robust backtesting
/// </summary>
public class RunDatabentoAcquisition
{
    public static async Task Main(string[] args)
    {
        // Set up logging
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<RunDatabentoAcquisition>();
        
        logger.LogInformation("🚀 Databento Historical Data Acquisition for Stroll");
        logger.LogInformation("==================================================");

        try
        {
            // Get Databento API key from environment or user input
            var apiKey = Environment.GetEnvironmentVariable("DATABENTO_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.Write("Enter Databento API Key: ");
                apiKey = Console.ReadLine();
                
                if (string.IsNullOrEmpty(apiKey))
                {
                    logger.LogError("❌ Databento API key is required");
                    logger.LogInformation("💡 Get your API key from https://databento.com/");
                    Environment.Exit(1);
                }
            }

            // Parse command line arguments for date range
            var startDate = args.Length > 0 && DateTime.TryParse(args[0], out var start) 
                ? start 
                : new DateTime(2000, 1, 1); // 25 years of data

            var endDate = args.Length > 1 && DateTime.TryParse(args[1], out var end)
                ? end
                : DateTime.Today;

            // Symbols for comprehensive backtesting
            var symbols = new[] { "SPY", "QQQ", "IWM", "XLE", "XLF", "XLK" };

            logger.LogInformation("📅 Date Range: {StartDate} to {EndDate} ({Years:F1} years)",
                startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"),
                (endDate - startDate).Days / 365.0);
            logger.LogInformation("🏷️ Symbols: {Symbols}", string.Join(", ", symbols));

            // Set up storage
            var dataPath = Path.GetFullPath("./data");
            Directory.CreateDirectory(dataPath);
            var catalog = DataCatalog.Default(dataPath);
            var storage = new CompositeStorage(catalog);

            logger.LogInformation("💾 Output Directory: {DataPath}", dataPath);

            // Initialize Databento provider
            var databento = new DatabentoProvider(
                apiKey, 
                loggerFactory.CreateLogger<DatabentoProvider>());

            var allResults = new List<DatabentoResult>();
            var totalStartTime = DateTime.UtcNow;

            // Process each symbol
            foreach (var symbol in symbols)
            {
                logger.LogInformation("🔄 Processing {Symbol}...", symbol);

                var progress = new Progress<DatabentoProgress>(p =>
                {
                    logger.LogInformation("📊 {Symbol}: {Progress:F1}% - Chunk {Current} - {Status}",
                        symbol, p.ProgressPercent, p.CurrentChunk, p.Status);
                });

                try
                {
                    var symbolResults = await databento.GetHistoricalBarsChunkedAsync(
                        symbol, startDate, endDate, 
                        DatabentoGranularity.OneMinute, // 1-minute bars
                        TimeSpan.FromDays(30), // 30-day chunks
                        progress);

                    allResults.AddRange(symbolResults);

                    // Store in Stroll format
                    await StoreSymbolDataAsync(symbol, symbolResults, storage, logger);

                    var totalRecords = symbolResults.Sum(r => r.RecordCount);
                    var years = (endDate - startDate).Days / 365.0;
                    logger.LogInformation("✅ {Symbol} complete: {Records:N0} records ({RecordsPerYear:N0}/year)",
                        symbol, totalRecords, totalRecords / years);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "❌ Failed to process {Symbol}", symbol);
                }

                // Respectful delay between symbols
                await Task.Delay(2000);
            }

            // Final statistics
            var totalDuration = DateTime.UtcNow - totalStartTime;
            var grandTotalRecords = allResults.Sum(r => r.RecordCount);
            var successfulSymbols = allResults.GroupBy(r => r.Symbol).Count(g => g.Any(r => r.Success));

            logger.LogInformation("🎯 ACQUISITION COMPLETE!");
            logger.LogInformation("========================");
            logger.LogInformation("📈 Total Records: {Records:N0}", grandTotalRecords);
            logger.LogInformation("🏷️ Successful Symbols: {Success}/{Total}", successfulSymbols, symbols.Length);
            logger.LogInformation("⏱️ Total Duration: {Duration}", totalDuration);
            logger.LogInformation("🚀 Records/Second: {Rate:N0}", grandTotalRecords / totalDuration.TotalSeconds);

            if (grandTotalRecords > 0)
            {
                logger.LogInformation("🎉 Data acquisition successful! Ready for professional backtesting.");
                logger.LogInformation("💡 Data stored in: {Path}", dataPath);
            }
            else
            {
                logger.LogError("❌ No data acquired. Check API key and network connection.");
                Environment.Exit(1);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 Data acquisition failed");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Store symbol data in Stroll's optimized format
    /// </summary>
    private static async Task StoreSymbolDataAsync(
        string symbol, 
        List<DatabentoResult> results, 
        IStorageProvider storage, 
        ILogger logger)
    {
        try
        {
            // Combine all chunks for this symbol
            var allBars = new List<Dictionary<string, object?>>();
            foreach (var result in results.Where(r => r.Success))
            {
                allBars.AddRange(result.Bars);
            }

            if (allBars.Count == 0) return;

            // Sort by timestamp and remove duplicates
            allBars = allBars
                .GroupBy(b => (DateTime)b["t"]!)
                .Select(g => g.First())
                .OrderBy(b => (DateTime)b["t"]!)
                .ToList();

            // Store as CSV for now (could be enhanced to direct SQLite)
            var csvPath = Path.Combine(storage.Catalog.Root, $"{symbol}_databento_1min.csv");
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("timestamp,open,high,low,close,volume");

            foreach (var bar in allBars)
            {
                var timestamp = (DateTime)bar["t"]!;
                var open = (decimal)bar["o"]!;
                var high = (decimal)bar["h"]!;
                var low = (decimal)bar["l"]!;
                var close = (decimal)bar["c"]!;
                var volume = (long)bar["v"]!;

                csv.AppendLine($"{timestamp:yyyy-MM-dd HH:mm:ss},{open},{high},{low},{close},{volume}");
            }

            await File.WriteAllTextAsync(csvPath, csv.ToString());
            
            logger.LogDebug("💾 Stored {Symbol}: {Records:N0} bars in {Path}",
                symbol, allBars.Count, csvPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to store {Symbol} data", symbol);
        }
    }
}