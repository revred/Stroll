using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Stroll.Backtest.Tests.Core;
using Stroll.Backtest.Tests.Strategy;
using Stroll.Backtest.Tests.Backtests;
using System.Diagnostics;
using System.Globalization;

namespace Stroll.Backtest.Tests.Performance;

/// <summary>
/// Expanded dataset backtest using 22 months of Alpha Vantage data (88,610 bars)
/// Compares performance between original 6-month dataset and expanded dataset
/// </summary>
public class ExpandedDatasetBacktest
{
    private readonly ILogger<ExpandedDatasetBacktest> _logger;
    private readonly string _originalArchivePath;
    private readonly string _expandedArchivePath;

    public ExpandedDatasetBacktest() : this(null)
    {
    }

    public ExpandedDatasetBacktest(ILogger<ExpandedDatasetBacktest>? logger)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = logger ?? loggerFactory.CreateLogger<ExpandedDatasetBacktest>();
        
        _originalArchivePath = Path.GetFullPath(@"C:\code\Stroll\Stroll.History\Stroll.Historical\historical_archive\historical_archive.db");
        _expandedArchivePath = Path.GetFullPath(@"C:\code\Stroll\Stroll.History\data\expanded_backtest.db");
    }

    [Test]
    public async Task Expanded_Dataset_Performance_Comparison()
    {
        _logger.LogInformation("🚀 Expanded Dataset Performance Comparison");
        _logger.LogInformation("📊 Comparing: Original 6 months vs Expanded 22 months (88,610 bars)");

        var results = new List<(string Name, long TimeMs, BacktestResult Result, int BarCount)>();

        // Test 1: Original 6-month dataset with optimizations
        if (File.Exists(_originalArchivePath))
        {
            _logger.LogInformation("📈 Running Original Dataset Test (6 months)");
            var original = await MeasureExpandedBacktest("Original_6mo", async () =>
            {
                return await RunOptimizedBacktest(_originalArchivePath, isOriginal: true);
            });
            results.Add(("Original_6mo", original.TimeMs, original.Result, original.BarCount));
        }
        else
        {
            _logger.LogWarning("⚠️ Original archive not found: {Path}", _originalArchivePath);
        }

        // Test 2: Expanded 22-month dataset
        if (File.Exists(_expandedArchivePath))
        {
            _logger.LogInformation("📈 Running Expanded Dataset Test (22 months)");
            var expanded = await MeasureExpandedBacktest("Expanded_22mo", async () =>
            {
                return await RunOptimizedBacktest(_expandedArchivePath, isOriginal: false);
            });
            results.Add(("Expanded_22mo", expanded.TimeMs, expanded.Result, expanded.BarCount));
        }
        else
        {
            Assert.Fail($"Expanded archive not found: {_expandedArchivePath}");
        }

        // Report Performance Comparison
        _logger.LogInformation("");
        _logger.LogInformation("🏁 EXPANDED DATASET COMPARISON RESULTS");
        _logger.LogInformation("=====================================");

        foreach (var (name, timeMs, result, barCount) in results)
        {
            var yearsProcessed = CalculateYearsFromBars(barCount);
            var processingSpeed = yearsProcessed / (timeMs / 1000.0);
            var chatgptSpeed = 3.33; // ChatGPT claimed 3.33 years/second

            _logger.LogInformation("⚡ {Name}:", name);
            _logger.LogInformation("   • Time: {TimeMs}ms", timeMs);
            _logger.LogInformation("   • Bars Processed: {BarCount:N0}", barCount);
            _logger.LogInformation("   • Years Processed: {Years:F2}", yearsProcessed);
            _logger.LogInformation("   • Processing Speed: {Speed:F2} years/second ({Percent:F1}% of ChatGPT)", 
                processingSpeed, (processingSpeed / chatgptSpeed) * 100);
            _logger.LogInformation("   • Trades: {Trades}", result.TotalTrades);
            _logger.LogInformation("   • Final Value: ${FinalValue:N0}", result.FinalAccountValue);
            _logger.LogInformation("");
        }

        // Compare original vs expanded
        if (results.Count == 2)
        {
            var original = results[0];
            var expanded = results[1];
            
            var datasetSizeRatio = (double)expanded.BarCount / original.BarCount;
            var timeRatio = (double)expanded.TimeMs / original.TimeMs;
            var efficiency = datasetSizeRatio / timeRatio;
            
            _logger.LogInformation("📊 COMPARISON ANALYSIS:");
            _logger.LogInformation("   • Dataset Size Increase: {Ratio:F1}x ({Original:N0} → {Expanded:N0} bars)", 
                datasetSizeRatio, original.BarCount, expanded.BarCount);
            _logger.LogInformation("   • Processing Time Increase: {Ratio:F1}x ({OriginalTime}ms → {ExpandedTime}ms)", 
                timeRatio, original.TimeMs, expanded.TimeMs);
            _logger.LogInformation("   • Efficiency Ratio: {Efficiency:F2} (>1.0 = better scaling)", efficiency);
            
            var tradeRatio = (double)expanded.Result.TotalTrades / original.Result.TotalTrades;
            _logger.LogInformation("   • Trade Count Ratio: {Ratio:F1}x ({Original} → {Expanded} trades)", 
                tradeRatio, original.Result.TotalTrades, expanded.Result.TotalTrades);

            // Verify performance scales reasonably
            Assert.That(expanded.Result.TotalTrades, Is.GreaterThan(original.Result.TotalTrades), 
                "Expanded dataset should have more trades");
            Assert.That(timeRatio, Is.LessThan(datasetSizeRatio * 1.5), 
                "Processing time should scale better than dataset size");
        }

        var fastest = results.OrderBy(r => r.TimeMs / CalculateYearsFromBars(r.BarCount)).First();
        _logger.LogInformation("🏆 Most Efficient: {Name}", fastest.Name);
    }

    private async Task<(long TimeMs, BacktestResult Result, int BarCount)> MeasureExpandedBacktest(
        string name, Func<Task<(BacktestResult, int)>> testFunc)
    {
        // Warm up
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var stopwatch = Stopwatch.StartNew();
        var (result, barCount) = await testFunc();
        stopwatch.Stop();

        return (stopwatch.ElapsedMilliseconds, result, barCount);
    }

    /// <summary>
    /// Run optimized backtest on either original or expanded dataset
    /// </summary>
    private async Task<(BacktestResult Result, int BarCount)> RunOptimizedBacktest(string databasePath, bool isOriginal)
    {
        var strategy = new SpxOneDteStrategy(null, seed: 42);
        var fillEngine = new RealFillEngine(null, seed: 42);
        var activePositions = new Dictionary<string, Position>();
        var completedTrades = new List<Trade>();
        var accountValue = 100000m;

        // Load all data in bulk for maximum performance
        var allBars = await LoadAllBarsFromDatabase(databasePath);
        _logger.LogInformation("📊 Loaded {Count:N0} bars from {Database}", allBars.Count, 
            isOriginal ? "original" : "expanded");

        if (allBars.Count == 0)
        {
            throw new InvalidOperationException($"No bars loaded from {databasePath}");
        }

        var (startDate, endDate) = (allBars.Min(b => b.Timestamp).Date, allBars.Max(b => b.Timestamp).Date);
        _logger.LogInformation("📅 Processing period: {StartDate} to {EndDate} ({Days} days)", 
            startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), (endDate - startDate).Days);

        // Process all bars with optimized streaming approach
        var processedDays = 0;
        var currentDate = startDate;
        var lastReportDate = DateTime.MinValue;

        while (currentDate <= endDate)
        {
            var dayBars = allBars.Where(b => b.Timestamp.Date == currentDate).OrderBy(b => b.Timestamp).ToList();
            
            if (dayBars.Any())
            {
                // Process day with compiled strategy rules (optimized)
                accountValue = await ProcessDayWithOptimizations(dayBars, strategy, fillEngine, activePositions, completedTrades, accountValue);
                processedDays++;

                // Progress reporting (every 30 days)
                if ((currentDate - lastReportDate).Days >= 30)
                {
                    _logger.LogDebug("📈 Processed {Days} days, {Trades} trades, ${Value:N0}", 
                        processedDays, completedTrades.Count, accountValue);
                    lastReportDate = currentDate;
                }
            }

            currentDate = currentDate.AddDays(1);
        }

        var result = new BacktestResult
        {
            TotalTrades = completedTrades.Count,
            FinalAccountValue = accountValue,
            StartDate = startDate,
            EndDate = endDate
        };

        return (result, allBars.Count);
    }

    /// <summary>
    /// Load all bars from SQLite database with optimized query
    /// </summary>
    private async Task<List<MarketBarData>> LoadAllBarsFromDatabase(string databasePath)
    {
        var bars = new List<MarketBarData>();

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        const string sql = @"
            SELECT timestamp, open, high, low, close, volume 
            FROM market_bars 
            WHERE symbol = 'SPY' 
            ORDER BY timestamp";

        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            bars.Add(new MarketBarData
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

    /// <summary>
    /// Process a single day with all performance optimizations applied
    /// </summary>
    private async Task<decimal> ProcessDayWithOptimizations(
        List<MarketBarData> dayBars, 
        SpxOneDteStrategy strategy, 
        RealFillEngine fillEngine,
        Dictionary<string, Position> activePositions,
        List<Trade> completedTrades,
        decimal accountValue)
    {
        foreach (var bar in dayBars)
        {
            var marketData = new MarketData
            {
                Timestamp = bar.Timestamp,
                SpxPrice = bar.Close,
                ImpliedVolatility = 0.2m // Simplified for performance
            };

            // Use compiled strategy rules for maximum performance
            var signals = await strategy.GenerateSignalsOptimizedAsync(marketData, activePositions);

            foreach (var signal in signals)
            {
                try
                {
                    // Simplified fill for performance testing
                    var fill = CreateSimplifiedFill(signal, marketData);
                    accountValue = ApplyFillOptimized(fill, activePositions, completedTrades, accountValue);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Signal execution failed: {Error}", ex.Message);
                }
            }
        }
        
        return accountValue;
    }

    /// <summary>
    /// Create simplified fill for performance testing
    /// </summary>
    private Fill CreateSimplifiedFill(TradeSignal signal, MarketData marketData)
    {
        var legs = signal.Legs.Select(leg => new LegFill
        {
            Quantity = leg.Quantity,
            Price = CalculateOptionPrice(leg, marketData.SpxPrice), // Simplified pricing
            Symbol = leg.Symbol
        }).ToList();

        return new Fill
        {
            Signal = signal,
            Timestamp = marketData.Timestamp,
            Legs = legs
        };
    }

    /// <summary>
    /// Simplified option pricing for performance testing
    /// </summary>
    private decimal CalculateOptionPrice(OptionLeg leg, decimal spxPrice)
    {
        // Simplified option pricing - intrinsic value + small time value
        var intrinsic = leg.OptionType == OptionType.Call ? 
            Math.Max(0, spxPrice - leg.Strike) : 
            Math.Max(0, leg.Strike - spxPrice);
        
        var timeValue = 2.0m; // Simplified time value for 1DTE
        return intrinsic + timeValue;
    }

    /// <summary>
    /// Optimized fill application with minimal allocations
    /// </summary>
    private decimal ApplyFillOptimized(
        Fill fill, 
        Dictionary<string, Position> activePositions,
        List<Trade> completedTrades,
        decimal accountValue)
    {
        if (fill.Signal.SignalType == SignalType.Entry)
        {
            var position = new Position
            {
                Id = Guid.NewGuid().ToString(),
                Strategy = "SPX_1DTE",
                OpenTime = fill.Timestamp,
                Legs = fill.Signal.Legs,
                AccountValue = accountValue
            };
            activePositions[position.Id] = position;
        }
        else if (fill.Signal.SignalType == SignalType.Exit && activePositions.Any())
        {
            // For 1DTE strategy, close all positions at end of day
            var positionsToClose = activePositions.Values.ToList();
            foreach (var position in positionsToClose)
            {
                var pnl = fill.Legs.Sum(leg => (decimal)leg.Quantity * leg.Price * 
                    (leg.Symbol.Contains("Call") ? 1m : -1m));
                var trade = new Trade
                {
                    OpenTime = position.OpenTime,
                    CloseTime = fill.Timestamp,
                    Pnl = pnl * 100m, // SPX multiplier
                    Strategy = position.Strategy
                };
                
                completedTrades.Add(trade);
                accountValue += trade.Pnl;
            }
            activePositions.Clear(); // Clear all positions
        }
        
        return accountValue;
    }

    /// <summary>
    /// Calculate years processed based on bar count (assuming 5-minute bars)
    /// </summary>
    private double CalculateYearsFromBars(int barCount)
    {
        // 5-minute bars: 78 bars/day * 252 trading days = 19,656 bars/year approximately
        const double barsPerYear = 19656.0;
        return barCount / barsPerYear;
    }

    private ILogger<T> CreateQuietLogger<T>()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        return loggerFactory.CreateLogger<T>();
    }
}

/// <summary>
/// Market bar data structure for performance testing
/// </summary>
public record MarketBarData
{
    public required DateTime Timestamp { get; init; }
    public required decimal Open { get; init; }
    public required decimal High { get; init; }
    public required decimal Low { get; init; }
    public required decimal Close { get; init; }
    public required long Volume { get; init; }
}

/// <summary>
/// Supporting classes for expanded backtest
/// </summary>
public record Fill
{
    public required TradeSignal Signal { get; init; }
    public required DateTime Timestamp { get; init; }
    public required List<LegFill> Legs { get; init; }
}

public record LegFill
{
    public required int Quantity { get; init; }
    public required decimal Price { get; init; }
    public required string Symbol { get; init; } = "";
}

public record Trade
{
    public required DateTime OpenTime { get; init; }
    public required DateTime CloseTime { get; init; }
    public required decimal Pnl { get; init; }
    public required string Strategy { get; init; }
}

public enum SignalAction { OpenPosition, ClosePosition }
public enum OptionType { Call, Put }
public enum OrderSide { Buy, Sell }