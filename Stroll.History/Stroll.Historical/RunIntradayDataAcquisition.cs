using Microsoft.Extensions.Logging;

namespace Stroll.Historical;

/// <summary>
/// Intraday data acquisition specifically for 1DTE options backtesting
/// Focuses on 1-minute and 5-minute bars from Alpha Vantage free tier
/// Addresses data granularity requirements for proper options strategies
/// </summary>
public class RunIntradayDataAcquisition
{
    public static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug)); // Enable debug logs
        var logger = loggerFactory.CreateLogger<RunIntradayDataAcquisition>();
        
        logger.LogInformation("🚀 INTRADAY Data Acquisition for 1DTE Options");
        logger.LogInformation("============================================");

        try
        {
            // Configuration - focus on recent data for 1DTE backtesting
            var startDate = args.Length > 0 && DateTime.TryParse(args[0], out var start) 
                ? start : DateTime.Today.AddMonths(-6); // Last 6 months for testing
            var endDate = args.Length > 1 && DateTime.TryParse(args[1], out var end)
                ? end : DateTime.Today;
            
            var symbols = new[] { "SPY" }; // Start with SPY only
            var intervals = new[] { IntradayInterval.FiveMinute }; // Start with 5min (more manageable)

            logger.LogInformation("📅 Target Period: {Start} to {End} ({Days} days)", 
                startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"),
                (endDate - startDate).Days);
            logger.LogInformation("🏷️ Symbols: {Symbols}", string.Join(", ", symbols));
            logger.LogInformation("⏱️ Intervals: 5min, 1min");

            // Set up storage
            var dataPath = Path.GetFullPath("./intraday_data");
            Directory.CreateDirectory(dataPath);
            logger.LogInformation("💾 Output: {Path}", dataPath);

            // Get Alpha Vantage API key
            var apiKey = GetAlphaVantageKey(logger);
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogError("❌ Alpha Vantage API key required for intraday data");
                Environment.Exit(1);
            }

            var provider = new AlphaVantageProvider(apiKey, false, 
                loggerFactory.CreateLogger<AlphaVantageProvider>());

            // Acquire intraday data for each symbol and interval using month-by-month paging
            var totalBars = 0;
            foreach (var symbol in symbols)
            {
                foreach (var interval in intervals)
                {
                    try
                    {
                        logger.LogInformation("🔄 Acquiring {Symbol} {Interval} data month-by-month...", symbol, interval);
                        
                        var progress = new Progress<IntradayProgress>(p =>
                        {
                            logger.LogInformation("   📅 {Symbol} {Interval} - {Status} ({Progress:F1}%)", 
                                p.Symbol, p.Interval, p.Status, p.ProgressPercent);
                        });

                        var results = await provider.GetIntradayHistoricalRangeAsync(symbol, interval, startDate, endDate, progress);
                        
                        // Combine all monthly results
                        var allBars = results.SelectMany(r => r.Bars).OrderBy(b => (DateTime)b["t"]!).ToList();
                        
                        if (allBars.Count > 0)
                        {
                            await SaveIntradayBars(symbol, interval, allBars, dataPath, logger);
                            totalBars += allBars.Count;
                            
                            logger.LogInformation("✅ {Symbol} {Interval}: {Records:N0} bars across {Months} months", 
                                symbol, interval, allBars.Count, results.Count);
                            
                            // Show sample timestamps
                            var first = (DateTime)allBars.First()["t"]!;
                            var last = (DateTime)allBars.Last()["t"]!;
                            logger.LogInformation("   📊 Range: {First} to {Last}", 
                                first.ToString("yyyy-MM-dd HH:mm"), last.ToString("yyyy-MM-dd HH:mm"));
                        }
                        else
                        {
                            logger.LogWarning("⚠️ No {Interval} data for {Symbol} in date range", interval, symbol);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "❌ Failed to acquire {Symbol} {Interval}", symbol, interval);
                    }
                }
            }

            // Final report
            logger.LogInformation("🎯 INTRADAY ACQUISITION COMPLETE!");
            logger.LogInformation("=================================");
            logger.LogInformation("📊 Total Bars: {TotalBars:N0}", totalBars);
            logger.LogInformation("📁 Data Location: {Path}", dataPath);
            
            var dataFiles = Directory.GetFiles(dataPath, "*.csv");
            logger.LogInformation("📄 Generated Files: {Count}", dataFiles.Length);
            
            if (totalBars > 0)
            {
                logger.LogInformation("🎉 SUCCESS! Ready for 1DTE options backtesting!");
                logger.LogInformation("💡 Granularity: Minute-level precision for realistic execution");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 Intraday data acquisition failed");
            Environment.Exit(1);
        }
    }

    private static string? GetAlphaVantageKey(ILogger logger)
    {
        var key = Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY");
        if (string.IsNullOrEmpty(key))
        {
            logger.LogInformation("🔑 No Alpha Vantage API key found in environment");
            logger.LogInformation("💡 Get your FREE API key from: https://www.alphavantage.co/support/#api-key");
            Console.Write("Enter Alpha Vantage API key: ");
            key = Console.ReadLine();
        }

        if (!string.IsNullOrEmpty(key))
        {
            logger.LogInformation("✅ Alpha Vantage API key provided");
            return key;
        }

        return null;
    }

    private static async Task SaveIntradayBars(
        string symbol, 
        IntradayInterval interval, 
        List<Dictionary<string, object?>> bars, 
        string dataPath, 
        ILogger logger)
    {
        var intervalStr = interval switch
        {
            IntradayInterval.OneMinute => "1min",
            IntradayInterval.FiveMinute => "5min",
            IntradayInterval.FifteenMinute => "15min",
            IntradayInterval.ThirtyMinute => "30min",
            IntradayInterval.OneHour => "60min",
            _ => "5min"
        };

        var fileName = $"{symbol}_alphavantage_{intervalStr}.csv";
        var filePath = Path.Combine(dataPath, fileName);
        
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("timestamp,open,high,low,close,volume");

        foreach (var bar in bars.OrderBy(b => (DateTime)b["t"]!))
        {
            var timestamp = (DateTime)bar["t"]!;
            var open = (decimal)bar["o"]!;
            var high = (decimal)bar["h"]!;
            var low = (decimal)bar["l"]!;
            var close = (decimal)bar["c"]!;
            var volume = (long)bar["v"]!;

            csv.AppendLine($"{timestamp:yyyy-MM-dd HH:mm:ss},{open},{high},{low},{close},{volume}");
        }

        await File.WriteAllTextAsync(filePath, csv.ToString());
        logger.LogDebug("💾 Saved {Symbol} {Interval} to {File}", symbol, intervalStr, fileName);
    }
}