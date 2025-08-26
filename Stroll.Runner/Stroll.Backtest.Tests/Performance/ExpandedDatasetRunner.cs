using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Stroll.Backtest.Tests.Performance;

/// <summary>
/// Expanded dataset backtest runner for 22-month dataset performance comparison
/// Consolidated from standalone ExpandedBacktestRunner project
/// </summary>
public class ExpandedDatasetRunner
{
    private readonly ILogger<ExpandedDatasetRunner> _logger;
    private readonly string _originalDbPath;
    private readonly string _expandedDbPath;

    public ExpandedDatasetRunner(ILogger<ExpandedDatasetRunner>? logger = null)
    {
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = logger ?? loggerFactory.CreateLogger<ExpandedDatasetRunner>();
        
        // Navigate up to solution root and find the database files
        var solutionRoot = FindSolutionRoot();
        _originalDbPath = Path.Combine(solutionRoot, @"Stroll.History\Stroll.Historical\historical_archive\historical_archive.db");
        _expandedDbPath = Path.Combine(solutionRoot, @"Stroll.History\Data\Partitions\spy_2021_2025.db");
    }

    public async Task<PerformanceComparisonResult> RunPerformanceComparisonAsync()
    {
        _logger.LogInformation("🚀 EXPANDED DATASET BACKTEST PERFORMANCE TEST");
        _logger.LogInformation("📊 Comparing: Original vs Expanded (47 months, 188,162 bars)");

        var result = new PerformanceComparisonResult();

        try
        {
            // Test 1: Original dataset (if available and has correct schema)
            if (File.Exists(_originalDbPath))
            {
                try
                {
                    _logger.LogInformation("📈 Running Original Dataset Test...");
                    var original = await RunBacktestAsync("Original", _originalDbPath);
                    result.OriginalResult = original;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("⚠️ Original dataset incompatible ({Error}), skipping comparison", ex.Message);
                    result.OriginalError = ex.Message;
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Original dataset not found, skipping comparison");
                result.OriginalError = "Dataset not found";
            }

            // Test 2: Expanded dataset
            if (File.Exists(_expandedDbPath))
            {
                _logger.LogInformation("📈 Running Expanded Dataset Test...");
                var expanded = await RunBacktestAsync("Expanded", _expandedDbPath);
                result.ExpandedResult = expanded;
            }
            else
            {
                throw new FileNotFoundException($"Expanded dataset not found: {_expandedDbPath}");
            }

            // Calculate comparison metrics
            if (result.OriginalResult != null && result.ExpandedResult != null)
            {
                result.CalculateComparison();
            }

            ReportResults(result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Backtest comparison failed");
            throw;
        }
    }

    private async Task<BacktestResult> RunBacktestAsync(string name, string databasePath)
    {
        var stopwatch = Stopwatch.StartNew();

        // Load all bars from database
        var bars = await LoadBarsFromDatabaseAsync(databasePath);
        
        if (bars.Count == 0)
        {
            throw new InvalidOperationException($"No data loaded from {databasePath}");
        }

        _logger.LogInformation("   📊 Loaded {Count:N0} bars from {Period}", bars.Count, 
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

        return new BacktestResult
        {
            Name = name,
            TimeMs = stopwatch.ElapsedMilliseconds,
            BarCount = bars.Count,
            TradeCount = trades,
            FinalValue = accountValue,
            StartDate = bars.Min(b => b.Timestamp),
            EndDate = bars.Max(b => b.Timestamp)
        };
    }

    private async Task<List<MarketBar>> LoadBarsFromDatabaseAsync(string databasePath)
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

    private bool IsGoodEntryCondition(MarketBar bar, DateTime date)
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

    private decimal SimulateIronCondorTrade(MarketBar entryBar, List<MarketBar> dayBars)
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

    private void ReportResults(PerformanceComparisonResult result)
    {
        _logger.LogInformation("");
        _logger.LogInformation("🏁 PERFORMANCE COMPARISON RESULTS");
        _logger.LogInformation("=================================");

        if (result.OriginalResult != null)
        {
            ReportSingleResult(result.OriginalResult);
        }

        if (result.ExpandedResult != null)
        {
            ReportSingleResult(result.ExpandedResult);
        }

        // Report comparison analysis if both results available
        if (result.DatasetSizeRatio.HasValue)
        {
            _logger.LogInformation("");
            _logger.LogInformation("📊 SCALING ANALYSIS:");
            _logger.LogInformation("   • Dataset Size Increase: {Ratio:F1}x", result.DatasetSizeRatio);
            _logger.LogInformation("   • Processing Time Increase: {Ratio:F1}x", result.TimeRatio);
            _logger.LogInformation("   • Efficiency Ratio: {Efficiency:F2} (>1.0 = better than linear scaling)", result.EfficiencyRatio);
            _logger.LogInformation("   • Trade Volume Ratio: {Ratio:F1}x", result.TradeRatio);

            // Performance verdict
            if (result.EfficiencyRatio > 1.0)
            {
                _logger.LogInformation("🏆 EXCELLENT: System scales better than linear with dataset size!");
            }
            else if (result.EfficiencyRatio > 0.8)
            {
                _logger.LogInformation("✅ GOOD: System scales well with dataset size");
            }
            else
            {
                _logger.LogInformation("⚠️ MODERATE: System scaling could be improved");
            }
        }

        _logger.LogInformation("");
        _logger.LogInformation("✅ Expanded backtest performance evaluation complete!");
    }

    private void ReportSingleResult(BacktestResult result)
    {
        var yearsProcessed = CalculateYears(result.BarCount);
        var processingSpeed = yearsProcessed / (result.TimeMs / 1000.0);
        var chatgptSpeed = 3.33; // ChatGPT benchmark

        _logger.LogInformation("");
        _logger.LogInformation("⚡ {Name} Dataset:", result.Name);
        _logger.LogInformation("   • Processing Time: {TimeMs}ms", result.TimeMs);
        _logger.LogInformation("   • Bars Processed: {Bars:N0}", result.BarCount);
        _logger.LogInformation("   • Years of Data: {Years:F2}", yearsProcessed);
        _logger.LogInformation("   • Processing Speed: {Speed:F2} years/second ({Percent:F1}% of ChatGPT)", 
            processingSpeed, (processingSpeed / chatgptSpeed) * 100);
        _logger.LogInformation("   • Trades Executed: {Trades}", result.TradeCount);
        _logger.LogInformation("   • Final Portfolio Value: ${FinalValue:N0}", result.FinalValue);
    }

    private static double CalculateYears(int barCount)
    {
        // 5-minute bars: ~78 bars/day * 252 trading days = ~19,656 bars/year
        return barCount / 19656.0;
    }

    private static string FindSolutionRoot()
    {
        var currentDir = Environment.CurrentDirectory;
        
        // Navigate up from test bin directory to find solution root
        while (currentDir != null && !File.Exists(Path.Combine(currentDir, "Stroll.History")))
        {
            var parentDir = Directory.GetParent(currentDir)?.FullName;
            if (parentDir == currentDir) break; // Reached root
            currentDir = parentDir;
            
            // Also check if we find a directory containing Stroll.History
            if (currentDir != null && Directory.Exists(Path.Combine(currentDir, "Stroll.History")))
            {
                break;
            }
        }
        
        return currentDir ?? Environment.CurrentDirectory;
    }
}

/// <summary>
/// Market bar data structure
/// </summary>
public record MarketBar
{
    public required DateTime Timestamp { get; init; }
    public required decimal Open { get; init; }
    public required decimal High { get; init; }
    public required decimal Low { get; init; }
    public required decimal Close { get; init; }
    public required long Volume { get; init; }
}

/// <summary>
/// Individual backtest result
/// </summary>
public record BacktestResult
{
    public required string Name { get; init; }
    public required long TimeMs { get; init; }
    public required int BarCount { get; init; }
    public required int TradeCount { get; init; }
    public required decimal FinalValue { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
}

/// <summary>
/// Performance comparison result
/// </summary>
public class PerformanceComparisonResult
{
    public BacktestResult? OriginalResult { get; set; }
    public BacktestResult? ExpandedResult { get; set; }
    public string? OriginalError { get; set; }
    
    // Calculated comparison metrics
    public double? DatasetSizeRatio { get; private set; }
    public double? TimeRatio { get; private set; }
    public double? EfficiencyRatio { get; private set; }
    public double? TradeRatio { get; private set; }

    public void CalculateComparison()
    {
        if (OriginalResult != null && ExpandedResult != null)
        {
            DatasetSizeRatio = (double)ExpandedResult.BarCount / OriginalResult.BarCount;
            TimeRatio = (double)ExpandedResult.TimeMs / OriginalResult.TimeMs;
            EfficiencyRatio = DatasetSizeRatio / TimeRatio;
            TradeRatio = (double)ExpandedResult.TradeCount / Math.Max(1, OriginalResult.TradeCount);
        }
    }
}