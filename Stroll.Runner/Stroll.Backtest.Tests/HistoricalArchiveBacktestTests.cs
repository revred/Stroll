using Microsoft.Extensions.Logging;
using Stroll.Backtest.Tests.Backtests;
using Stroll.Backtest.Tests.Core;

namespace Stroll.Backtest.Tests;

/// <summary>
/// Test class for historical archive backtesting with Bar Magnifier
/// Uses our SQLite historical data (35,931 bars) with 1-minute precision
/// </summary>
public class HistoricalArchiveBacktestTests
{
    private readonly ILogger<HistoricalArchiveBacktestTests> _logger;
    private readonly string _archivePath;

    public HistoricalArchiveBacktestTests()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<HistoricalArchiveBacktestTests>();

        // Path to our SQLite historical archive
        _archivePath = Path.GetFullPath(@"C:\code\Stroll\Stroll.History\Stroll.Historical\historical_archive\historical_archive.db");
    }

    /// <summary>
    /// Run 6-month 1DTE SPX backtest using historical archive with Bar Magnifier
    /// </summary>
    [Test]
    public async Task Run_SixMonth_1DTE_Backtest_With_BarMagnifier()
    {
        // Arrange
        _logger.LogInformation("🚀 Starting 6-Month Historical Archive Backtest");
        _logger.LogInformation("📁 Archive Path: {ArchivePath}", _archivePath);
        
        if (!File.Exists(_archivePath))
        {
            _logger.LogError("❌ Historical archive not found: {Path}", _archivePath);
            Assert.Fail($"Historical archive database not found at: {_archivePath}");
        }

        var backtestLogger = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information))
            .CreateLogger<HistoricalArchiveBacktestRunner>();
        var runner = new HistoricalArchiveBacktestRunner(_archivePath, backtestLogger);

        // Act
        var result = await runner.RunSixMonthBacktestAsync();

        // Assert & Report Results
        _logger.LogInformation("🎯 BACKTEST RESULTS SUMMARY");
        _logger.LogInformation("==========================");
        _logger.LogInformation("📅 Period: {StartDate} to {EndDate}", 
            result.StartDate.ToString("yyyy-MM-dd"), result.EndDate.ToString("yyyy-MM-dd"));
        _logger.LogInformation("💰 Starting Capital: ${StartingCapital:N0}", result.StartingCapital);
        _logger.LogInformation("💰 Final Account Value: ${FinalValue:N0}", result.FinalAccountValue);
        _logger.LogInformation("📈 Total Return: {TotalReturn:P2}", result.TotalReturn);
        _logger.LogInformation("📊 Annualized Return: {AnnualizedReturn:P2}", result.AnnualizedReturn);
        _logger.LogInformation("📉 Maximum Drawdown: {MaxDrawdown:P2}", result.MaxDrawdown);
        _logger.LogInformation("🎯 Total Trades: {TotalTrades}", result.TotalTrades);
        _logger.LogInformation("✅ Winning Trades: {WinningTrades} ({WinRate:P1})", 
            result.WinningTrades, result.WinRate);
        _logger.LogInformation("❌ Losing Trades: {LosingTrades}", result.LosingTrades);
        _logger.LogInformation("💡 Average Win: ${AverageWin:F2}", result.AverageWin);
        _logger.LogInformation("💸 Average Loss: ${AverageLoss:F2}", result.AverageLoss);
        _logger.LogInformation("⚖️ Profit Factor: {ProfitFactor:F2}", result.ProfitFactor);

        // Performance validations
        Assert.That(result.TotalTrades, Is.GreaterThan(0), "Should have executed some trades");
        Assert.That(result.FinalAccountValue, Is.GreaterThan(0), "Should have positive account value");
        
        // Log detailed trade analysis
        if (result.Trades.Length > 0)
        {
            _logger.LogInformation("🔍 TRADE ANALYSIS:");
            _logger.LogInformation("Recent Trades:");
            
            var recentTrades = result.Trades.TakeLast(10);
            foreach (var trade in recentTrades)
            {
                _logger.LogInformation("  {Timestamp}: {Strategy} - P&L: ${PnL:F2} (Premium: ${NetPremium:F2})",
                    trade.Timestamp.ToString("MM/dd HH:mm"), 
                    trade.StrategyName, 
                    trade.PnL, 
                    trade.NetPremium);
            }
        }

        _logger.LogInformation("✅ 6-Month backtest completed successfully using Bar Magnifier!");
    }

    /// <summary>
    /// Test Bar Magnifier functionality independently
    /// </summary>
    [Test]
    public async Task Test_BarMagnifier_Functionality()
    {
        _logger.LogInformation("🔬 Testing Bar Magnifier functionality");

        // Create sample 5-minute bar
        var testBar = new Bar5m(
            T: DateTime.Today.AddHours(10),
            O: 450.00m,
            H: 452.50m,
            L: 449.25m,
            C: 451.75m,
            V: 1000000
        );

        // Test Conservative mode
        var conservativeMinutes = BarMagnifier.ToMinutes(testBar, MagnifierMode.Conservative).ToList();
        _logger.LogInformation("🔧 Conservative Mode: Generated {Count} 1-minute bars", conservativeMinutes.Count);

        Assert.That(conservativeMinutes.Count, Is.EqualTo(5), "Should generate exactly 5 one-minute bars");

        // Test Bridge mode
        var bridgeMinutes = BarMagnifier.ToMinutes(testBar, MagnifierMode.Bridge, seed: 42).ToList();
        _logger.LogInformation("🌉 Bridge Mode: Generated {Count} 1-minute bars", bridgeMinutes.Count);

        Assert.That(bridgeMinutes.Count, Is.EqualTo(5), "Should generate exactly 5 one-minute bars");

        // Validate re-aggregation
        var reAggregated = BarMagnifier.ReAggregate(conservativeMinutes);
        var isValid = BarMagnifier.ValidateMagnification(testBar, conservativeMinutes);

        _logger.LogInformation("✅ Re-aggregation validation: {IsValid}", isValid);
        Assert.That(isValid, Is.True, "Re-aggregated bar should match original");

        _logger.LogInformation("🔍 Original: O={O} H={H} L={L} C={C} V={V}",
            testBar.O, testBar.H, testBar.L, testBar.C, testBar.V);
        _logger.LogInformation("🔍 Re-agg:   O={O} H={H} L={L} C={C} V={V}",
            reAggregated.O, reAggregated.H, reAggregated.L, reAggregated.C, reAggregated.V);
    }

    /// <summary>
    /// Verify historical archive data quality and coverage
    /// </summary>
    [Test]
    public async Task Verify_Historical_Archive_Data_Quality()
    {
        _logger.LogInformation("🔍 Verifying historical archive data quality");

        if (!File.Exists(_archivePath))
        {
            Assert.Fail($"Historical archive not found: {_archivePath}");
        }

        var backtestLogger = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information))
            .CreateLogger<HistoricalArchiveBacktestRunner>();
        var runner = new HistoricalArchiveBacktestRunner(_archivePath, backtestLogger);
        
        // This will validate the data range and count
        try
        {
            var result = await runner.RunSixMonthBacktestAsync();
            
            _logger.LogInformation("✅ Data quality verification completed");
            _logger.LogInformation("📊 Backtest used data from {StartDate} to {EndDate}", 
                result.StartDate.ToString("yyyy-MM-dd"), result.EndDate.ToString("yyyy-MM-dd"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Data quality verification failed");
            Assert.Fail($"Data quality verification failed: {ex.Message}");
        }
    }
}

// Support for NUnit testing
[SetUpFixture]
public class TestSetup
{
    [OneTimeSetUp]
    public void Setup()
    {
        // Global test setup if needed
    }
}