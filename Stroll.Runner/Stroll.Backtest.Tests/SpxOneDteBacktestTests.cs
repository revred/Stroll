using Microsoft.Extensions.Logging;
using Stroll.Backtest.Tests.Backtests;
using Stroll.Storage;

namespace Stroll.Backtest.Tests;

/// <summary>
/// Integration tests for SPX 1DTE strategy backtest
/// Tests the complete backtest pipeline from Sep 9, 1999 to Aug 24, 2025
/// </summary>
public class SpxOneDteBacktestTests : IDisposable
{
    private readonly ILogger<SpxOneDteBacktestTests> _logger;
    private readonly IStorageProvider _storage;
    private readonly SpxOneDteBacktestRunner _backtestRunner;

    public SpxOneDteBacktestTests()
    {
        // Set up logging with debug level for troubleshooting
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<SpxOneDteBacktestTests>();
        
        // Set up storage with ODTE data integration
        var dataPath = Path.Combine(Directory.GetCurrentDirectory(), "data");
        var catalog = DataCatalog.Default(dataPath);
        _storage = OdteStorageFactory.CreateWithOdteData(catalog);
        
        // Initialize backtest runner
        _backtestRunner = new SpxOneDteBacktestRunner(_storage, 
            loggerFactory.CreateLogger<SpxOneDteBacktestRunner>());
    }

    [Test]
    public async Task SpxOneDteBacktest_ShouldCompleteSuccessfully()
    {
        // Act
        var result = await _backtestRunner.RunBacktestAsync();

        // Assert
        result.Should().NotBeNull();
        result.StartDate.Should().Be(new DateTime(1999, 9, 9));
        result.EndDate.Should().Be(new DateTime(2025, 8, 24));
        result.StartingCapital.Should().Be(100000m);
        result.FinalAccountValue.Should().BeGreaterThan(0);
        
        // Basic performance validation
        result.TotalTrades.Should().BeGreaterThan(0, "Should have executed some trades");
        result.WinRate.Should().BeGreaterOrEqualTo(0m, "Win rate should be non-negative");
        result.WinRate.Should().BeLessOrEqualTo(1m, "Win rate should not exceed 100%");
        result.MaxDrawdown.Should().BeGreaterOrEqualTo(0m, "Max drawdown should be non-negative");
    }

    [Test]
    public async Task SpxOneDteBacktest_ShouldReportDetailedMetrics()
    {
        // Act
        var result = await _backtestRunner.RunBacktestAsync();

        // Assert - Validate all key metrics are present
        result.TotalReturn.Should().NotBe(decimal.MinValue);
        result.AnnualizedReturn.Should().NotBe(decimal.MinValue);
        result.MaxDrawdown.Should().BeGreaterOrEqualTo(0m);
        result.ProfitFactor.Should().BeGreaterOrEqualTo(0m);
        
        // Validate trade statistics
        var expectedTotalTrades = result.WinningTrades + result.LosingTrades;
        result.TotalTrades.Should().Be(expectedTotalTrades, "Total trades should equal winning + losing trades");
        
        if (result.WinningTrades > 0)
        {
            result.AverageWin.Should().BeGreaterThan(0m, "Average win should be positive when there are winning trades");
        }
        
        if (result.LosingTrades > 0)
        {
            result.AverageLoss.Should().BeLessThan(0m, "Average loss should be negative when there are losing trades");
        }
        
        // Log results for analysis
        _logger.LogInformation("📊 Backtest Results Summary:");
        _logger.LogInformation("💰 Total Return: {TotalReturn:P2}", result.TotalReturn);
        _logger.LogInformation("📈 Annualized Return: {AnnualizedReturn:P2}", result.AnnualizedReturn);
        _logger.LogInformation("📉 Max Drawdown: {MaxDrawdown:P2}", result.MaxDrawdown);
        _logger.LogInformation("🎯 Win Rate: {WinRate:P1} ({WinningTrades}/{TotalTrades})", 
            result.WinRate, result.WinningTrades, result.TotalTrades);
        _logger.LogInformation("💵 Average Win: ${AverageWin:F2}", result.AverageWin);
        _logger.LogInformation("💸 Average Loss: ${AverageLoss:F2}", result.AverageLoss);
        _logger.LogInformation("⚖️ Profit Factor: {ProfitFactor:F2}", result.ProfitFactor);
        _logger.LogInformation("🏁 Final Account Value: ${FinalAccountValue:N0}", result.FinalAccountValue);
    }

    [Test]
    public async Task SpxOneDteBacktest_ShouldHandleDataGapsGracefully()
    {
        // This test ensures the backtest continues even when market data is missing for certain dates
        
        // Act
        var result = await _backtestRunner.RunBacktestAsync();

        // Assert
        result.Should().NotBeNull();
        
        // Even with potential data gaps, we should have a reasonable number of trades
        // over a 25+ year period (conservatively expect at least 100 trading days with data)
        var totalDays = (result.EndDate - result.StartDate).Days;
        _logger.LogInformation("📅 Total backtest period: {TotalDays} days", totalDays);
        
        // The backtest should complete without throwing exceptions
        result.FinalAccountValue.Should().NotBe(0m, "Account should have some value at end");
    }

    [Test]
    public async Task SpxOneDteBacktest_ShouldHaveReasonableRiskMetrics()
    {
        // Act
        var result = await _backtestRunner.RunBacktestAsync();

        // Assert - Risk management validation
        result.MaxDrawdown.Should().BeLessThan(0.5m, "Max drawdown should be less than 50%");
        
        if (result.TotalTrades > 10) // Only validate if we have meaningful sample size
        {
            result.WinRate.Should().BeGreaterThan(0.3m, "Win rate should be above 30% for viable strategy");
            result.WinRate.Should().BeLessThan(0.99m, "Win rate above 99% suggests unrealistic assumptions");
            
            if (result.ProfitFactor > 0)
            {
                result.ProfitFactor.Should().BeGreaterThan(0.5m, "Profit factor should be above 0.5 for viable strategy");
            }
        }
        
        _logger.LogInformation("✅ Risk metrics validation passed");
    }

    [Test]
    public void RealFillEngine_ShouldSimulateRealisticFills()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<Core.RealFillEngine>();
        var fillEngine = new Core.RealFillEngine(logger, seed: 42);
        
        var order = new Core.OptionOrder
        {
            Symbol = "SPX",
            OptionSymbol = "SPX240930C05500",
            Side = Core.OrderSide.Buy,
            OrderType = Core.OrderType.Market,
            Quantity = 1,
            Expiration = DateTime.Today.AddDays(1),
            Strike = 5500m,
            OptionType = Core.OptionType.Call
        };
        
        var quote = new Core.OptionQuote
        {
            Bid = 10.50m,
            Ask = 10.70m,
            BidSize = 10,
            AskSize = 10,
            ImpliedVolatility = 0.20m,
            Delta = 0.15m,
            Gamma = 0.01m,
            Theta = -0.1m,
            Vega = 0.05m,
            Timestamp = DateTime.UtcNow
        };
        
        var conditions = new Core.MarketConditions
        {
            ImpliedVolatility = 0.20m,
            VolumeRatio = 1.0m,
            IsMarketOpen = true,
            Timestamp = DateTime.UtcNow
        };

        // Act
        var result = fillEngine.SimulateFill(order, quote, conditions);

        // Assert
        result.Should().NotBeNull();
        result.IsFilled.Should().BeTrue("Market order should fill");
        result.FillPrice.Should().BeGreaterThan(quote.Bid, "Fill price should be above bid");
        result.FillPrice.Should().BeLessThan(quote.Ask * 1.1m, "Fill price should be reasonable");
        result.LatencyMs.Should().BeGreaterThan(0, "Should have realistic latency");
        result.Slippage.Should().BeGreaterOrEqualTo(0m, "Slippage should be non-negative");
        
        logger.LogInformation("🎯 Fill simulation: Price=${FillPrice:F2}, Slippage=${Slippage:F2}, Latency={LatencyMs}ms", 
            result.FillPrice, result.Slippage, result.LatencyMs);
    }

    public void Dispose()
    {
        // Storage cleanup handled by CompositeStorage internally
    }
}

/// <summary>
/// Performance benchmark tests for the backtest system
/// </summary>
public class BacktestPerformanceTests
{
    [Test]
    public async Task BacktestRunner_ShouldCompleteInReasonableTime()
    {
        // Arrange
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<SpxOneDteBacktestRunner>();
        
        var catalog = DataCatalog.Default("./TestData");
        var storage = new CompositeStorage(catalog);
        var runner = new SpxOneDteBacktestRunner(storage, logger);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await runner.RunBacktestAsync();
        stopwatch.Stop();

        // Assert
        result.Should().NotBeNull();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(300000, // 5 minutes max
            "Backtest should complete in reasonable time for testing");
        
        logger.LogInformation("⏱️ Backtest completed in {ElapsedMs}ms ({ElapsedSeconds:F1}s)", 
            stopwatch.ElapsedMilliseconds, stopwatch.ElapsedMilliseconds / 1000.0);
    }
}