using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ExpandedBacktestRunner;

/// <summary>
/// Simple, isolated expanded backtest runner for 22-month dataset performance comparison
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<Program>();
        
        logger.LogInformation("🚀 EXPANDED DATASET BACKTEST PERFORMANCE TEST");
        logger.LogInformation("📊 Comparing: Original vs Expanded (22 months, 88,610 bars)");

        try
        {
            var originalDbPath = @"C:\code\Stroll\Stroll.History\Stroll.Historical\historical_archive\historical_archive.db";
            var expandedDbPath = @"C:\code\Stroll\Stroll.History\data\expanded_backtest.db";

            var results = new List<(string Name, long TimeMs, int Bars, int Trades, decimal FinalValue)>();

            // Test 1: Original dataset (if available and has correct schema)
            if (File.Exists(originalDbPath))
            {
                try
                {
                    logger.LogInformation("📈 Running Original Dataset Test...");
                    var original = await RunBacktest("Original", originalDbPath, logger);
                    results.Add(original);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("⚠️ Original dataset incompatible ({Error}), skipping comparison", ex.Message);
                }
            }
            else
            {
                logger.LogWarning("⚠️ Original dataset not found, skipping comparison");
            }

            // Test 2: Expanded dataset
            if (File.Exists(expandedDbPath))
            {
                logger.LogInformation("📈 Running Expanded Dataset Test...");
                var expanded = await RunBacktest("Expanded", expandedDbPath, logger);
                results.Add(expanded);
            }
            else
            {
                logger.LogError("❌ Expanded dataset not found: {Path}", expandedDbPath);
                Environment.Exit(1);
            }

            // Report Results
            logger.LogInformation("");
            logger.LogInformation("🏁 PERFORMANCE COMPARISON RESULTS");
            logger.LogInformation("=================================");

            foreach (var (name, timeMs, bars, trades, finalValue) in results)
            {
                var yearsProcessed = CalculateYears(bars);
                var processingSpeed = yearsProcessed / (timeMs / 1000.0);
                var chatgptSpeed = 3.33; // ChatGPT benchmark

                logger.LogInformation("⚡ {Name} Dataset:", name);
                logger.LogInformation("   • Processing Time: {TimeMs}ms", timeMs);
                logger.LogInformation("   • Bars Processed: {Bars:N0}", bars);
                logger.LogInformation("   • Years of Data: {Years:F2}", yearsProcessed);
                logger.LogInformation("   • Processing Speed: {Speed:F2} years/second ({Percent:F1}% of ChatGPT)", 
                    processingSpeed, (processingSpeed / chatgptSpeed) * 100);
                logger.LogInformation("   • Trades Executed: {Trades}", trades);
                logger.LogInformation("   • Final Portfolio Value: ${FinalValue:N0}", finalValue);
                logger.LogInformation("");
            }

            // Performance Analysis
            if (results.Count == 2)
            {
                var (original, expanded) = (results[0], results[1]);
                var datasetRatio = (double)expanded.Bars / original.Bars;
                var timeRatio = (double)expanded.TimeMs / original.TimeMs;
                var efficiency = datasetRatio / timeRatio;

                logger.LogInformation("📊 SCALING ANALYSIS:");
                logger.LogInformation("   • Dataset Size Increase: {Ratio:F1}x ({Original:N0} → {Expanded:N0} bars)", 
                    datasetRatio, original.Bars, expanded.Bars);
                logger.LogInformation("   • Processing Time Increase: {Ratio:F1}x ({OriginalMs}ms → {ExpandedMs}ms)", 
                    timeRatio, original.TimeMs, expanded.TimeMs);
                logger.LogInformation("   • Efficiency Ratio: {Efficiency:F2} (>1.0 = better than linear scaling)", efficiency);
                
                var tradeRatio = (double)expanded.Trades / original.Trades;
                logger.LogInformation("   • Trade Volume Ratio: {Ratio:F1}x ({Original} → {Expanded} trades)", 
                    tradeRatio, original.Trades, expanded.Trades);

                // Performance verdict
                if (efficiency > 1.0)
                {
                    logger.LogInformation("🏆 EXCELLENT: System scales better than linear with dataset size!");
                }
                else if (efficiency > 0.8)
                {
                    logger.LogInformation("✅ GOOD: System scales well with dataset size");
                }
                else
                {
                    logger.LogInformation("⚠️ MODERATE: System scaling could be improved");
                }
            }

            logger.LogInformation("✅ Expanded backtest performance evaluation complete!");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 Backtest failed");
            Environment.Exit(1);
        }
    }

    static async Task<(string Name, long TimeMs, int Bars, int Trades, decimal FinalValue)> RunBacktest(
        string name, string databasePath, ILogger logger)
    {
        var stopwatch = Stopwatch.StartNew();

        // Load all bars from database
        var bars = await LoadBarsFromDatabase(databasePath, logger);
        
        if (bars.Count == 0)
        {
            throw new InvalidOperationException($"No data loaded from {databasePath}");
        }

        logger.LogInformation("   📊 Loaded {Count:N0} bars from {Period}", bars.Count, 
            $"{bars.Min(b => b.Timestamp):yyyy-MM} to {bars.Max(b => b.Timestamp):yyyy-MM}");

        // Simple 1DTE-style backtesting logic
        var accountValue = 100000m;
        var trades = 0;
        var currentDate = bars.Min(b => b.Timestamp).Date;
        var endDate = bars.Max(b => b.Timestamp).Date;

        while (currentDate <= endDate)
        {
            var dayBars = bars.Where(b => b.Timestamp.Date == currentDate).ToList();
            
            if (dayBars.Count > 0)
            {
                // Simplified 1DTE strategy: enter on first bar if conditions met
                var firstBar = dayBars.First();
                
                // Simple entry condition: IV environment check (simulated)
                if (IsGoodEntryCondition(firstBar, currentDate))
                {
                    // Simulate Iron Condor trade
                    var tradePnl = SimulateIronCondorTrade(firstBar, dayBars);
                    accountValue += tradePnl;
                    trades++;
                }
            }

            currentDate = currentDate.AddDays(1);
        }

        stopwatch.Stop();

        return (name, stopwatch.ElapsedMilliseconds, bars.Count, trades, accountValue);
    }

    static async Task<List<MarketBar>> LoadBarsFromDatabase(string databasePath, ILogger logger)
    {
        var bars = new List<MarketBar>();

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        const string sql = @"
            SELECT timestamp, open, high, low, close, volume 
            FROM market_bars 
            WHERE symbol = 'SPY' OR symbol = 'SPX'
            ORDER BY timestamp";

        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            bars.Add(new MarketBar
            {
                Timestamp = reader.GetDateTime(0),
                Open = (decimal)reader.GetDouble(1),
                High = (decimal)reader.GetDouble(2),
                Low = (decimal)reader.GetDouble(3),
                Close = (decimal)reader.GetDouble(4),
                Volume = reader.GetInt64(5)
            });
        }

        return bars;
    }

    static bool IsGoodEntryCondition(MarketBar bar, DateTime date)
    {
        // Simple conditions: weekday, market hours simulation, and price range
        if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            return false;

        // Entry time around 9:45 AM simulation (use hour 9-10 range)
        if (bar.Timestamp.Hour < 9 || bar.Timestamp.Hour > 15)
            return false;

        // Avoid extreme moves (simulated volatility filter)
        var dailyRange = (bar.High - bar.Low) / bar.Close;
        return dailyRange < 0.05m; // Less than 5% daily range
    }

    static decimal SimulateIronCondorTrade(MarketBar entryBar, List<MarketBar> dayBars)
    {
        // Simplified Iron Condor simulation
        var spxPrice = entryBar.Close;
        
        // Calculate strikes (simplified)
        var shortCallStrike = Math.Ceiling(spxPrice * 1.02m); // ~2% OTM
        var shortPutStrike = Math.Floor(spxPrice * 0.98m);    // ~2% OTM
        
        // Simulate credit received (simplified)
        var creditReceived = 200m; // $2.00 per contract * 100 multiplier
        
        // Check if strikes were breached during the day
        var dayHigh = dayBars.Max(b => b.High);
        var dayLow = dayBars.Min(b => b.Low);
        
        if (dayHigh > shortCallStrike || dayLow < shortPutStrike)
        {
            // Strikes breached - take loss (simplified)
            return -creditReceived * 3m; // 3:1 loss ratio
        }
        else
        {
            // Profit target hit (50% of credit)
            return creditReceived * 0.5m;
        }
    }

    static double CalculateYears(int barCount)
    {
        // 5-minute bars: ~78 bars/day * 252 trading days = ~19,656 bars/year
        return barCount / 19656.0;
    }
}

public record MarketBar
{
    public required DateTime Timestamp { get; init; }
    public required decimal Open { get; init; }
    public required decimal High { get; init; }
    public required decimal Low { get; init; }
    public required decimal Close { get; init; }
    public required long Volume { get; init; }
}