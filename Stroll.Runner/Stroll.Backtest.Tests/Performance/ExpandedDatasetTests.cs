using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Stroll.Backtest.Tests.Performance;

/// <summary>
/// Performance tests for expanded 22-month dataset
/// </summary>
[TestFixture]
[Category("Performance")]
public class ExpandedDatasetTests
{
    private ILogger<ExpandedDatasetTests>? _logger;
    
    [SetUp]
    public void Setup()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<ExpandedDatasetTests>();
    }

    [Test]
    [Category("Performance")]
    public async Task Expanded_Dataset_Performance_Test()
    {
        // Arrange
        var runner = new ExpandedDatasetRunner();
        
        // Act
        var result = await runner.RunPerformanceComparisonAsync();
        
        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ExpandedResult, Is.Not.Null, "Expanded dataset result should be available");
        
        // Verify expanded dataset performance
        var expanded = result.ExpandedResult!;
        Assert.That(expanded.BarCount, Is.GreaterThan(80000), "Should process at least 80,000 bars");
        Assert.That(expanded.TimeMs, Is.LessThan(5000), "Should complete in under 5 seconds");
        
        // Verify processing speed
        var yearsProcessed = expanded.BarCount / 19656.0; // ~19,656 bars per year
        var processingSpeed = yearsProcessed / (expanded.TimeMs / 1000.0);
        Assert.That(processingSpeed, Is.GreaterThan(2.0), "Should process at least 2 years per second");
        
        // Log performance metrics
        _logger?.LogInformation("📊 Expanded Dataset Performance Test Results:");
        _logger?.LogInformation("   • Bars Processed: {BarCount:N0}", expanded.BarCount);
        _logger?.LogInformation("   • Processing Time: {TimeMs}ms", expanded.TimeMs);
        _logger?.LogInformation("   • Processing Speed: {Speed:F2} years/second", processingSpeed);
        
        // If comparison available, verify scaling efficiency
        if (result.EfficiencyRatio.HasValue)
        {
            Assert.That(result.EfficiencyRatio, Is.GreaterThan(0.8), 
                "Should maintain at least 80% efficiency when scaling");
            
            _logger?.LogInformation("   • Scaling Efficiency: {Efficiency:F2}", result.EfficiencyRatio);
        }
    }

    [Test]
    [Category("Performance")]
    [Category("Benchmark")]
    public async Task Compare_Against_ChatGPT_Benchmark()
    {
        // Arrange
        const double chatGptBenchmark = 3.33; // ChatGPT claims 3.33 years/second
        var runner = new ExpandedDatasetRunner();
        
        // Act
        var result = await runner.RunPerformanceComparisonAsync();
        
        // Assert
        Assert.That(result.ExpandedResult, Is.Not.Null);
        
        var expanded = result.ExpandedResult!;
        var yearsProcessed = expanded.BarCount / 19656.0;
        var processingSpeed = yearsProcessed / (expanded.TimeMs / 1000.0);
        var percentOfChatGpt = (processingSpeed / chatGptBenchmark) * 100;
        
        // We should achieve at least 50% of ChatGPT's claimed performance
        Assert.That(percentOfChatGpt, Is.GreaterThan(50), 
            $"Should achieve at least 50% of ChatGPT's {chatGptBenchmark} years/second benchmark");
        
        _logger?.LogInformation("🎯 ChatGPT Benchmark Comparison:");
        _logger?.LogInformation("   • Our Speed: {Speed:F2} years/second", processingSpeed);
        _logger?.LogInformation("   • ChatGPT Claimed: {ChatGpt:F2} years/second", chatGptBenchmark);
        _logger?.LogInformation("   • Achievement: {Percent:F1}% of ChatGPT speed", percentOfChatGpt);
        
        if (percentOfChatGpt > 80)
        {
            _logger?.LogInformation("   🏆 EXCELLENT: Achieved over 80% of ChatGPT's performance!");
        }
        else if (percentOfChatGpt > 60)
        {
            _logger?.LogInformation("   ✅ GOOD: Solid performance relative to ChatGPT benchmark");
        }
    }

    [Test]
    [Category("DataIntegrity")]
    public async Task Verify_Expanded_Dataset_Integrity()
    {
        // Arrange
        var runner = new ExpandedDatasetRunner();
        
        // Act
        var result = await runner.RunPerformanceComparisonAsync();
        
        // Assert
        Assert.That(result.ExpandedResult, Is.Not.Null);
        
        var expanded = result.ExpandedResult!;
        
        // Verify data integrity
        Assert.That(expanded.BarCount, Is.EqualTo(88610).Within(100), 
            "Should have approximately 88,610 bars as expected");
        
        // Verify date range (approximately 22 months)
        var monthsCovered = (expanded.EndDate - expanded.StartDate).Days / 30.0;
        Assert.That(monthsCovered, Is.GreaterThan(21).And.LessThan(24), 
            "Should cover approximately 22 months of data");
        
        // Verify final portfolio value is reasonable
        Assert.That(expanded.FinalValue, Is.GreaterThan(50000).And.LessThan(200000),
            "Final portfolio value should be within reasonable range");
        
        _logger?.LogInformation("✅ Data Integrity Verification:");
        _logger?.LogInformation("   • Bar Count: {BarCount:N0} (expected ~88,610)", expanded.BarCount);
        _logger?.LogInformation("   • Date Range: {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}", 
            expanded.StartDate, expanded.EndDate);
        _logger?.LogInformation("   • Months Covered: {Months:F1}", monthsCovered);
    }
}