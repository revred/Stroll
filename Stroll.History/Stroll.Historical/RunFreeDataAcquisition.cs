using Microsoft.Extensions.Logging;
using Stroll.Storage;

namespace Stroll.Historical;

/// <summary>
/// 100% FREE data acquisition system for PoC projects
/// Uses only free providers: Alpha Vantage Free + Yahoo Finance + Stooq + ODTE data
/// No premium subscriptions required - perfect for proof of concept!
/// </summary>
public class RunFreeDataAcquisition
{
    public static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<RunFreeDataAcquisition>();
        
        logger.LogInformation("🆓 100% FREE Data Acquisition for PoC");
        logger.LogInformation("===================================");

        try
        {
            // Configuration
            var startDate = args.Length > 0 && DateTime.TryParse(args[0], out var start) 
                ? start : new DateTime(2005, 1, 1); // 20 years is plenty for PoC
            var endDate = args.Length > 1 && DateTime.TryParse(args[1], out var end)
                ? end : DateTime.Today;
            
            // Focus on core ETFs for PoC - enough to prove the system works
            var symbols = new[] { "SPY", "QQQ", "IWM" };

            logger.LogInformation("📅 Target Period: {Start} to {End} ({Years:F1} years)",
                startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"),
                (endDate - startDate).Days / 365.0);
            logger.LogInformation("🏷️ Symbols: {Symbols} (Core ETFs - perfect for PoC)", string.Join(", ", symbols));
            logger.LogInformation("💰 Cost: $0.00 (100% free providers!)");

            // Set up storage
            var dataPath = Path.GetFullPath("./data");
            Directory.CreateDirectory(dataPath);

            // Strategy 1: Check existing ODTE data first
            logger.LogInformation("🔍 Step 1: Checking existing ODTE data...");
            await CheckOdteDataAsync(symbols, logger);

            // Strategy 2: Use Alpha Vantage free tier
            logger.LogInformation("🔍 Step 2: Alpha Vantage Free Tier (5 req/min)...");
            var alphaVantageKey = GetAlphaVantageKey(logger);
            
            if (!string.IsNullOrEmpty(alphaVantageKey))
            {
                await AcquireWithAlphaVantageAsync(symbols, alphaVantageKey, dataPath, loggerFactory.CreateLogger<AlphaVantageProvider>());
            }

            // Strategy 3: Yahoo Finance (unlimited, no key required)
            logger.LogInformation("🔍 Step 3: Yahoo Finance (unlimited, free)...");
            await AcquireWithYahooFinanceAsync(symbols, startDate, endDate, dataPath, logger);

            // Strategy 4: Stooq (unlimited, no key required)  
            logger.LogInformation("🔍 Step 4: Stooq.com (unlimited, free)...");
            await AcquireWithStooqAsync(symbols, startDate, endDate, dataPath, logger);

            // Final report
            logger.LogInformation("🎯 FREE ACQUISITION COMPLETE!");
            logger.LogInformation("============================");
            
            var dataFiles = Directory.GetFiles(dataPath, "*.csv");
            logger.LogInformation("📁 Generated Files: {Count}", dataFiles.Length);
            
            foreach (var file in dataFiles)
            {
                var lines = File.ReadAllLines(file);
                var symbol = Path.GetFileNameWithoutExtension(file).Split('_')[0];
                logger.LogInformation("   📊 {Symbol}: {Records:N0} records", symbol, Math.Max(0, lines.Length - 1));
            }

            if (dataFiles.Length > 0)
            {
                logger.LogInformation("🎉 SUCCESS! Ready for backtesting with free data!");
                logger.LogInformation("💡 Total Cost: $0.00 - Perfect for PoC development!");
                logger.LogInformation("📍 Data Location: {Path}", dataPath);
            }
            else
            {
                logger.LogWarning("⚠️ No data files generated. Check network connection and API keys.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 Free data acquisition failed");
            Environment.Exit(1);
        }
    }

    private static async Task CheckOdteDataAsync(string[] symbols, ILogger logger)
    {
        var odteDbPath = @"C:\Code\ODTE\data\ODTE_TimeSeries_5Y.db";
        var odteCsvPath = @"C:\Code\ODTE\data\Staging";

        if (File.Exists(odteDbPath))
        {
            logger.LogInformation("✅ Found ODTE SQLite database: {Path}", odteDbPath);
            logger.LogInformation("💡 Consider using RunOdteDataMigration to leverage this existing data");
        }

        if (Directory.Exists(odteCsvPath))
        {
            var csvFiles = Directory.GetFiles(odteCsvPath, "*.csv");
            logger.LogInformation("✅ Found {Count} ODTE CSV files in staging", csvFiles.Length);
            
            foreach (var file in csvFiles.Take(3)) // Show first few
            {
                var fileName = Path.GetFileName(file);
                var fileInfo = new FileInfo(file);
                var size = fileInfo.Length / 1024; // KB
                logger.LogInformation("   📁 {File} ({Size:N0} KB)", fileName, size);
            }
        }
        
        await Task.CompletedTask; // Make method properly async
    }

    private static string? GetAlphaVantageKey(ILogger logger)
    {
        var key = Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY");
        if (string.IsNullOrEmpty(key))
        {
            logger.LogInformation("🔑 No Alpha Vantage API key found");
            logger.LogInformation("💡 Get your FREE API key from: https://www.alphavantage.co/support/#api-key");
            Console.Write("Enter Alpha Vantage API key (or press Enter to skip): ");
            key = Console.ReadLine();
        }

        if (!string.IsNullOrEmpty(key))
        {
            logger.LogInformation("✅ Alpha Vantage API key provided");
            return key;
        }

        logger.LogInformation("⏭️ Skipping Alpha Vantage (no API key)");
        return null;
    }

    private static async Task AcquireWithAlphaVantageAsync(string[] symbols, string apiKey, string dataPath, ILogger<AlphaVantageProvider> logger)
    {
        try
        {
            var provider = new AlphaVantageProvider(apiKey, false, logger); // Free tier
            
            foreach (var symbol in symbols)
            {
                try
                {
                    logger.LogInformation("📊 Alpha Vantage: {Symbol}...", symbol);
                    var results = await provider.GetComprehensiveHistoricalAsync(symbol, DateTime.Today.AddYears(-5), DateTime.Today);
                    
                    var allBars = results.SelectMany(r => r.Bars).ToList();
                    if (allBars.Count > 0)
                    {
                        await SaveBarsToFile(symbol, allBars, dataPath, "alphavantage", logger);
                        logger.LogInformation("✅ Alpha Vantage {Symbol}: {Records:N0} records", symbol, allBars.Count);
                    }

                    // Respect free tier rate limit
                    await Task.Delay(15000); // 15 seconds between requests for 5/minute limit
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "⚠️ Alpha Vantage failed for {Symbol}", symbol);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Alpha Vantage provider error");
        }
    }

    private static async Task AcquireWithYahooFinanceAsync(string[] symbols, DateTime startDate, DateTime endDate, string dataPath, ILogger logger)
    {
        try
        {
            foreach (var symbol in symbols)
            {
                try
                {
                    logger.LogInformation("📊 Yahoo Finance: {Symbol}...", symbol);
                    
                    // Yahoo Finance URL format (same as ODTE uses)
                    var startUnix = ((DateTimeOffset)startDate).ToUnixTimeSeconds();
                    var endUnix = ((DateTimeOffset)endDate).ToUnixTimeSeconds();
                    
                    var url = $"https://query1.finance.yahoo.com/v7/finance/download/{symbol}" +
                             $"?period1={startUnix}&period2={endUnix}&interval=1d&events=history&includeAdjustedClose=true";

                    using var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    
                    var csvData = await httpClient.GetStringAsync(url);
                    var bars = ParseYahooCsv(csvData, symbol);

                    if (bars.Count > 0)
                    {
                        await SaveBarsToFile(symbol, bars, dataPath, "yahoo", logger);
                        logger.LogInformation("✅ Yahoo Finance {Symbol}: {Records:N0} records", symbol, bars.Count);
                    }

                    await Task.Delay(5000); // 5 seconds delay - Yahoo is aggressive with blocking
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "⚠️ Yahoo Finance failed for {Symbol}", symbol);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Yahoo Finance error");
        }
    }

    private static async Task AcquireWithStooqAsync(string[] symbols, DateTime startDate, DateTime endDate, string dataPath, ILogger logger)
    {
        try
        {
            foreach (var symbol in symbols)
            {
                try
                {
                    logger.LogInformation("📊 Stooq: {Symbol}...", symbol);
                    
                    // Stooq URL format (same as ODTE uses)
                    var stooqSymbol = $"{symbol}.US"; // Add .US suffix for US ETFs
                    var url = $"https://stooq.com/q/d/l/?s={stooqSymbol}&i=d";

                    using var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                    
                    var csvData = await httpClient.GetStringAsync(url);
                    var bars = ParseStooqCsv(csvData, symbol, startDate, endDate);

                    if (bars.Count > 0)
                    {
                        await SaveBarsToFile(symbol, bars, dataPath, "stooq", logger);
                        logger.LogInformation("✅ Stooq {Symbol}: {Records:N0} records", symbol, bars.Count);
                    }

                    await Task.Delay(1000); // 1 second delay to be respectful
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "⚠️ Stooq failed for {Symbol}", symbol);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Stooq error");
        }
    }

    private static List<Dictionary<string, object?>> ParseYahooCsv(string csvData, string symbol)
    {
        var bars = new List<Dictionary<string, object?>>();
        var lines = csvData.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i < lines.Length; i++) // Skip header
        {
            var parts = lines[i].Split(',');
            if (parts.Length >= 6 && DateTime.TryParse(parts[0], out var date))
            {
                if (decimal.TryParse(parts[1], out var open) &&
                    decimal.TryParse(parts[2], out var high) &&
                    decimal.TryParse(parts[3], out var low) &&
                    decimal.TryParse(parts[4], out var close) &&
                    long.TryParse(parts[6], out var volume))
                {
                    bars.Add(new Dictionary<string, object?>
                    {
                        ["t"] = date,
                        ["o"] = open,
                        ["h"] = high,
                        ["l"] = low,
                        ["c"] = close,
                        ["v"] = volume
                    });
                }
            }
        }

        return bars.OrderBy(b => (DateTime)b["t"]!).ToList();
    }

    private static List<Dictionary<string, object?>> ParseStooqCsv(string csvData, string symbol, DateTime startDate, DateTime endDate)
    {
        var bars = new List<Dictionary<string, object?>>();
        var lines = csvData.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i < lines.Length; i++) // Skip header
        {
            var parts = lines[i].Split(',');
            if (parts.Length >= 6 && DateTime.TryParseExact(parts[0], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date))
            {
                if (date >= startDate && date <= endDate &&
                    decimal.TryParse(parts[1], out var open) &&
                    decimal.TryParse(parts[2], out var high) &&
                    decimal.TryParse(parts[3], out var low) &&
                    decimal.TryParse(parts[4], out var close) &&
                    long.TryParse(parts[5], out var volume))
                {
                    bars.Add(new Dictionary<string, object?>
                    {
                        ["t"] = date,
                        ["o"] = open,
                        ["h"] = high,
                        ["l"] = low,
                        ["c"] = close,
                        ["v"] = volume
                    });
                }
            }
        }

        return bars.OrderBy(b => (DateTime)b["t"]!).ToList();
    }

    private static async Task SaveBarsToFile(string symbol, List<Dictionary<string, object?>> bars, string dataPath, string provider, ILogger logger)
    {
        var fileName = $"{symbol}_{provider}_daily.csv";
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

            csv.AppendLine($"{timestamp:yyyy-MM-dd},{open},{high},{low},{close},{volume}");
        }

        await File.WriteAllTextAsync(filePath, csv.ToString());
        logger.LogDebug("💾 Saved {Symbol} to {File}", symbol, fileName);
    }
}