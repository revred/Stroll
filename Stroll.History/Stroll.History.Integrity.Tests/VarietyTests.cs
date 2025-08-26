using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Stroll.History.Integrity.Tests;

/// <summary>
/// High-variety integrity tests using 5,000+ data points to stress test
/// the storage layer with rapid context switching, large symbol pools,
/// and wide date ranges.
/// </summary>
public class VarietyTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _varietyDataPath;
    private readonly string _executablePath;
    private readonly Random _random = new(42); // Deterministic seed for reproducibility

    public VarietyTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Find the variety test data
        _varietyDataPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..",
            "Stroll.Runner", "Stroll.History.Integrity.Tests", "variety");
        
        // Find the Stroll.Historical executable
        var x64Path = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..",
            "Stroll.Historical", "bin", "x64", "Debug", "net9.0", "Stroll.Historical.exe");
        
        var regularPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..",
            "Stroll.Historical", "bin", "Debug", "net9.0", "Stroll.Historical.exe");
        
        _executablePath = File.Exists(x64Path) ? x64Path : regularPath;
    }

    [Fact]
    public async Task Variety_SmokTest_200RandomDataPoints()
    {
        // Load the standard variety dataset
        var dataFile = Path.Combine(_varietyDataPath, "svariety_dataset_5000.csv");
        if (!File.Exists(dataFile))
        {
            _output.WriteLine($"Variety dataset not found at: {dataFile}");
            return; // Skip if data not available
        }

        var allTests = LoadTestCases(dataFile);
        var randomTests = allTests.OrderBy(_ => _random.Next()).Take(200).ToList();
        
        _output.WriteLine($"Running smoke test with {randomTests.Count} random data points");
        
        var results = new List<TestResult>();
        var sw = Stopwatch.StartNew();
        
        foreach (var test in randomTests)
        {
            var result = await ExecuteTestCase(test);
            results.Add(result);
            
            if (!result.Success)
            {
                _output.WriteLine($"❌ Failed: {test.Date} {test.Instrument} - {result.Error}");
            }
        }
        
        sw.Stop();
        
        // Report results
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);
        
        _output.WriteLine($"\n📊 Smoke Test Results:");
        _output.WriteLine($"  Total: {results.Count}");
        _output.WriteLine($"  ✅ Passed: {successCount} ({100.0 * successCount / results.Count:F1}%)");
        _output.WriteLine($"  ❌ Failed: {failureCount}");
        _output.WriteLine($"  ⏱️ Duration: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"  📈 Avg per test: {sw.ElapsedMilliseconds / results.Count}ms");
        
        // Assert high success rate
        var successRate = (double)successCount / results.Count;
        successRate.Should().BeGreaterThan(0.95, "Expected >95% success rate for variety tests");
    }

    [Fact]
    public async Task Variety_FullSuite_AllDataPoints()
    {
        // Load the standard variety dataset
        var dataFile = Path.Combine(_varietyDataPath, "svariety_dataset_5000.csv");
        if (!File.Exists(dataFile))
        {
            _output.WriteLine($"Variety dataset not found at: {dataFile}");
            return; // Skip if data not available
        }

        var allTests = LoadTestCases(dataFile);
        _output.WriteLine($"Running full suite with {allTests.Count} data points");
        
        var results = new Dictionary<string, List<TestResult>>();
        var sw = Stopwatch.StartNew();
        
        // Group tests by instrument type for better reporting
        var groupedTests = allTests.GroupBy(t => t.Instrument.Split(':')[1]);
        
        foreach (var group in groupedTests)
        {
            var groupResults = new List<TestResult>();
            
            foreach (var test in group)
            {
                var result = await ExecuteTestCase(test);
                groupResults.Add(result);
            }
            
            results[group.Key] = groupResults;
        }
        
        sw.Stop();
        
        // Report results by category
        _output.WriteLine($"\n📊 Full Suite Results by Category:");
        
        foreach (var (category, categoryResults) in results)
        {
            var success = categoryResults.Count(r => r.Success);
            var total = categoryResults.Count;
            _output.WriteLine($"  {category}: {success}/{total} ({100.0 * success / total:F1}%)");
        }
        
        var totalSuccess = results.Values.SelectMany(r => r).Count(r => r.Success);
        var totalTests = results.Values.SelectMany(r => r).Count();
        
        _output.WriteLine($"\n📊 Overall Results:");
        _output.WriteLine($"  Total: {totalTests}");
        _output.WriteLine($"  ✅ Passed: {totalSuccess} ({100.0 * totalSuccess / totalTests:F1}%)");
        _output.WriteLine($"  ❌ Failed: {totalTests - totalSuccess}");
        _output.WriteLine($"  ⏱️ Duration: {sw.Elapsed.TotalSeconds:F1}s");
        _output.WriteLine($"  📈 Throughput: {totalTests / sw.Elapsed.TotalSeconds:F1} tests/sec");
        
        // Assert reasonable success rate
        var successRate = (double)totalSuccess / totalTests;
        successRate.Should().BeGreaterThan(0.90, "Expected >90% success rate for full variety suite");
    }

    [Fact]
    public async Task Variety_StressTest_RapidContextSwitching()
    {
        // This test specifically targets rapid switching between different
        // instrument types and symbols to stress the storage layer
        
        var dataFile = Path.Combine(_varietyDataPath, "svariety_dataset_5000.csv");
        if (!File.Exists(dataFile))
        {
            _output.WriteLine($"Variety dataset not found at: {dataFile}");
            return;
        }

        var allTests = LoadTestCases(dataFile);
        
        // Create a sequence that maximizes context switches
        var stressSequence = new List<TestCase>();
        var symbols = allTests.Select(t => t.Instrument.Split(':')[0]).Distinct().ToList();
        var types = allTests.Select(t => t.Instrument.Split(':')[1]).Distinct().ToList();
        
        // Alternate between different symbols and types
        for (int i = 0; i < Math.Min(500, allTests.Count); i++)
        {
            var symbol = symbols[i % symbols.Count];
            var type = types[i % types.Count];
            
            var test = allTests.FirstOrDefault(t => 
                t.Instrument.StartsWith(symbol) && 
                t.Instrument.EndsWith(type));
            
            if (test != null)
                stressSequence.Add(test);
        }
        
        _output.WriteLine($"Running stress test with {stressSequence.Count} rapid context switches");
        
        var sw = Stopwatch.StartNew();
        var results = new List<TestResult>();
        
        foreach (var test in stressSequence)
        {
            var result = await ExecuteTestCase(test);
            results.Add(result);
        }
        
        sw.Stop();
        
        var successCount = results.Count(r => r.Success);
        _output.WriteLine($"\n📊 Stress Test Results:");
        _output.WriteLine($"  Context switches: {stressSequence.Count}");
        _output.WriteLine($"  Success rate: {100.0 * successCount / results.Count:F1}%");
        _output.WriteLine($"  Avg latency: {sw.ElapsedMilliseconds / results.Count}ms");
        _output.WriteLine($"  Throughput: {results.Count / sw.Elapsed.TotalSeconds:F1} ops/sec");
        
        // Even under stress, we should maintain good performance
        successCount.Should().BeGreaterThan(results.Count * 9 / 10, 
            "Expected >90% success rate even under rapid context switching");
    }

    [Theory]
    [InlineData("SPY", 100)]
    [InlineData("QQQ", 100)]
    [InlineData("IWM", 50)]
    [InlineData("TLT", 50)]
    [InlineData("GLD", 50)]
    public async Task Variety_PerSymbol_ValidationTests(string symbol, int sampleSize)
    {
        var dataFile = Path.Combine(_varietyDataPath, "svariety_dataset_5000.csv");
        if (!File.Exists(dataFile))
        {
            _output.WriteLine($"Variety dataset not found at: {dataFile}");
            return;
        }

        var allTests = LoadTestCases(dataFile);
        var symbolTests = allTests
            .Where(t => t.Instrument.StartsWith(symbol))
            .Take(sampleSize)
            .ToList();
        
        _output.WriteLine($"Testing {symbol} with {symbolTests.Count} data points");
        
        var results = new List<TestResult>();
        foreach (var test in symbolTests)
        {
            var result = await ExecuteTestCase(test);
            results.Add(result);
        }
        
        var successRate = (double)results.Count(r => r.Success) / results.Count;
        _output.WriteLine($"  {symbol} success rate: {successRate:P1}");
        
        successRate.Should().BeGreaterThan(0.90, 
            $"Expected >90% success rate for {symbol} variety tests");
    }

    private List<TestCase> LoadTestCases(string csvPath)
    {
        var tests = new List<TestCase>();
        
        foreach (var line in File.ReadAllLines(csvPath).Skip(1)) // Skip header
        {
            var parts = line.Split(',');
            if (parts.Length >= 3)
            {
                tests.Add(new TestCase
                {
                    Date = parts[0].Trim(),
                    Instrument = parts[1].Trim(),
                    ExpectedResult = parts[2].Trim()
                });
            }
        }
        
        return tests;
    }

    private async Task<TestResult> ExecuteTestCase(TestCase test)
    {
        try
        {
            var parts = test.Instrument.Split(':');
            var symbol = parts[0];
            var type = parts[1];
            
            string arguments = type switch
            {
                "options" => $"get-options --symbol {symbol} --date {test.Date}",
                "bars1d" => $"get-bars --symbol {symbol} --from {test.Date} --to {test.Date} --granularity 1d",
                "bars1m" => $"get-bars --symbol {symbol} --from {test.Date} --to {test.Date} --granularity 1m",
                "bars5m" => $"get-bars --symbol {symbol} --from {test.Date} --to {test.Date} --granularity 5m",
                _ => throw new NotSupportedException($"Unknown instrument type: {type}")
            };

            var startInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new TestResult { Success = false, Error = "Failed to start process" };
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            var completed = await Task.Run(() => process.WaitForExit(5000)); // 5 second timeout
            
            if (!completed)
            {
                process.Kill();
                return new TestResult { Success = false, Error = "Timeout" };
            }

            var output = await outputTask;
            var error = await errorTask;

            // Check for expected result
            bool success = test.ExpectedResult switch
            {
                "OK" => output.Contains("\"ok\":true") && !output.Contains("\"error\""),
                "OK-EMPTY" => output.Contains("\"ok\":true") || output.Contains("\"data\":[]"),
                _ => false
            };

            return new TestResult
            {
                Success = success,
                Error = success ? null : $"Exit: {process.ExitCode}, Error: {error}"
            };
        }
        catch (Exception ex)
        {
            return new TestResult { Success = false, Error = ex.Message };
        }
    }

    private class TestCase
    {
        public required string Date { get; init; }
        public required string Instrument { get; init; }
        public required string ExpectedResult { get; init; }
    }

    private class TestResult
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
    }

    public void Dispose()
    {
        // Cleanup if needed
    }
}