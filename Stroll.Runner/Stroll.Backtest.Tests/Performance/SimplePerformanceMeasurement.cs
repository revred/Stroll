using Microsoft.Extensions.Logging;
using Stroll.Backtest.Tests.Backtests;
using System.Diagnostics;

namespace Stroll.Backtest.Tests.Performance;

/// <summary>
/// Simple standalone performance measurement to show actual results
/// </summary>
public class SimplePerformanceMeasurement
{
    private readonly string _archivePath;

    public SimplePerformanceMeasurement()
    {
        _archivePath = Path.GetFullPath(@"C:\code\Stroll\Stroll.History\Stroll.Historical\historical_archive\historical_archive.db");
    }

    [Test]
    public async Task Show_Performance_Results()
    {
        Console.WriteLine("🚀 Real Performance Measurement - Historical Data Processing");
        Console.WriteLine("Target: Beat ChatGPT's 20 years in 6 seconds (3.33 years/sec)");
        Console.WriteLine($"Archive: {_archivePath}");
        Console.WriteLine();

        if (!File.Exists(_archivePath))
        {
            Assert.Fail($"Archive not found: {_archivePath}");
        }

        // Test baseline performance
        Console.WriteLine("📊 Running Baseline Test...");
        var baselineTime = await MeasureBaseline();
        Console.WriteLine($"✅ Baseline: {baselineTime}ms");
        Console.WriteLine();

        // Test optimized performance  
        Console.WriteLine("📊 Running Optimized Test...");
        var optimizedTime = await MeasureOptimized();
        Console.WriteLine($"✅ Optimized: {optimizedTime}ms");
        Console.WriteLine();

        // Calculate improvement
        var speedup = (double)baselineTime / optimizedTime;
        var improvement = ((double)(baselineTime - optimizedTime) / baselineTime) * 100;
        
        Console.WriteLine("🏁 PERFORMANCE RESULTS");
        Console.WriteLine("====================");
        Console.WriteLine($"⚡ Baseline:    {baselineTime:N0}ms");
        Console.WriteLine($"⚡ Optimized:   {optimizedTime:N0}ms");
        Console.WriteLine($"⚡ Speedup:     {speedup:F2}x faster");
        Console.WriteLine($"⚡ Improvement: {improvement:F1}%");
        Console.WriteLine();

        // Compare to ChatGPT target
        var chatGptRate = 20.0 / 6.0; // 20 years in 6 seconds = 3.33 years/second
        var ourRate6Months = 0.5 / (optimizedTime / 1000.0); // 6 months = 0.5 years
        var competitiveness = ourRate6Months / chatGptRate * 100;
        
        Console.WriteLine("📊 CHATGPT COMPARISON");
        Console.WriteLine("====================");
        Console.WriteLine($"📊 ChatGPT rate:     {chatGptRate:F2} years/second");
        Console.WriteLine($"📊 Our rate:         {ourRate6Months:F2} years/second");  
        Console.WriteLine($"📊 Competitiveness:  {competitiveness:F1}% of ChatGPT speed");
        Console.WriteLine();

        if (competitiveness > 100)
        {
            Console.WriteLine("🎉 SUCCESS: We beat ChatGPT's performance target!");
        }
        else if (competitiveness > 50)
        {
            Console.WriteLine("🎯 GOOD: We achieved significant performance, approaching ChatGPT levels");
        }
        else
        {
            Console.WriteLine("⚠️  ROOM FOR IMPROVEMENT: Performance gap remains vs ChatGPT");
        }

        Assert.That(optimizedTime, Is.LessThan(baselineTime), "Optimization should improve performance");
    }

    private async Task<long> MeasureBaseline()
    {
        var logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Error)).CreateLogger<HistoricalArchiveBacktestRunner>();
        var runner = new HistoricalArchiveBacktestRunner(_archivePath, logger);
        
        var stopwatch = Stopwatch.StartNew();
        var result = await runner.RunSixMonthBacktestAsync();
        stopwatch.Stop();
        
        Console.WriteLine($"   Baseline executed {result.TotalTrades} trades, final value: ${result.FinalAccountValue:N0}");
        return stopwatch.ElapsedMilliseconds;
    }

    private async Task<long> MeasureOptimized()
    {
        var optimizedTest = new OptimizedArchiveBacktest();
        
        var stopwatch = Stopwatch.StartNew();
        var result = await optimizedTest.RunWithAllOptimizations();
        stopwatch.Stop();
        
        Console.WriteLine($"   Optimized executed {result.TotalTrades} trades, final value: ${result.FinalAccountValue:N0}");
        return stopwatch.ElapsedMilliseconds;
    }
}