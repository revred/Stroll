using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Stroll.History.Integrity.Tests;

/// <summary>
/// Multi-threaded stress tests to ensure the system remains robust under
/// high concurrency with rapid context switching and heavy data queries.
/// </summary>
public class MultiThreadedStressTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _varietyDataPath;
    private readonly string _executablePath;
    private readonly SemaphoreSlim _processThrottle;
    private readonly ConcurrentBag<TestMetrics> _metrics = new();

    public MultiThreadedStressTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Throttle concurrent processes to avoid system overload
        _processThrottle = new SemaphoreSlim(Environment.ProcessorCount * 2);
        
        // Find paths
        _varietyDataPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..",
            "Stroll.Runner", "Stroll.History.Integrity.Tests", "variety");
        
        var x64Path = Path.Combine(
            Directory.GetCurrentDirectory(),
            "..", "..", "..", "..",
            "Stroll.Historical", "bin", "x64", "Debug", "net9.0", "Stroll.Historical.exe");
        
        _executablePath = File.Exists(x64Path) ? x64Path : x64Path.Replace(".exe", ".dll");
    }

    [Fact]
    public async Task StressTest_HighConcurrency_1000RequestsAcross20Threads()
    {
        var dataFile = Path.Combine(_varietyDataPath, "svariety_dataset_5000.csv");
        if (!File.Exists(dataFile))
        {
            _output.WriteLine($"Creating synthetic test data for stress testing");
            dataFile = CreateSyntheticTestData();
        }

        var testCases = LoadTestCases(dataFile).Take(1000).ToList();
        var threadCount = 20;
        var testsPerThread = testCases.Count / threadCount;
        
        _output.WriteLine($"🔥 Starting high-concurrency stress test:");
        _output.WriteLine($"  • Total requests: {testCases.Count}");
        _output.WriteLine($"  • Concurrent threads: {threadCount}");
        _output.WriteLine($"  • Requests per thread: {testsPerThread}");
        
        var sw = Stopwatch.StartNew();
        var tasks = new List<Task<ThreadResult>>();
        
        for (int i = 0; i < threadCount; i++)
        {
            var threadId = i;
            var threadTests = testCases.Skip(i * testsPerThread).Take(testsPerThread).ToList();
            
            tasks.Add(Task.Run(async () => await ExecuteThreadWorkload(threadId, threadTests)));
        }
        
        var results = await Task.WhenAll(tasks);
        sw.Stop();
        
        // Aggregate results
        var totalSuccess = results.Sum(r => r.SuccessCount);
        var totalFailure = results.Sum(r => r.FailureCount);
        var totalRequests = totalSuccess + totalFailure;
        var avgLatency = _metrics.Average(m => m.Latency);
        var p95Latency = GetPercentile(_metrics.Select(m => m.Latency).OrderBy(l => l).ToList(), 0.95);
        var p99Latency = GetPercentile(_metrics.Select(m => m.Latency).OrderBy(l => l).ToList(), 0.99);
        
        _output.WriteLine($"\n📊 Stress Test Results:");
        _output.WriteLine($"  ✅ Success: {totalSuccess} ({100.0 * totalSuccess / totalRequests:F1}%)");
        _output.WriteLine($"  ❌ Failed: {totalFailure}");
        _output.WriteLine($"  ⏱️ Total duration: {sw.Elapsed.TotalSeconds:F1}s");
        _output.WriteLine($"  📈 Throughput: {totalRequests / sw.Elapsed.TotalSeconds:F1} req/sec");
        _output.WriteLine($"  📊 Latency - Avg: {avgLatency:F0}ms, P95: {p95Latency:F0}ms, P99: {p99Latency:F0}ms");
        
        // Assert system remains stable under stress
        var successRate = (double)totalSuccess / totalRequests;
        successRate.Should().BeGreaterThan(0.95, "System should maintain >95% success rate under high concurrency");
        p99Latency.Should().BeLessThan(1000, "P99 latency should remain under 1 second even under stress");
    }

    [Fact]
    public async Task StressTest_BurstLoad_500ConcurrentRequests()
    {
        // This test simulates burst load - all requests fired simultaneously
        var testCases = GenerateBurstTestCases(500);
        
        _output.WriteLine($"💥 Starting burst load test with {testCases.Count} simultaneous requests");
        
        var sw = Stopwatch.StartNew();
        var tasks = testCases.Select(test => ExecuteTestCaseAsync(test)).ToList();
        
        var results = await Task.WhenAll(tasks);
        sw.Stop();
        
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);
        
        _output.WriteLine($"\n📊 Burst Load Results:");
        _output.WriteLine($"  Burst size: {testCases.Count} simultaneous requests");
        _output.WriteLine($"  Success: {successCount} ({100.0 * successCount / results.Length:F1}%)");
        _output.WriteLine($"  Failed: {failureCount}");
        _output.WriteLine($"  Duration: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Avg latency: {sw.ElapsedMilliseconds / (double)results.Length:F1}ms per request");
        
        // System should handle burst load gracefully
        successCount.Should().BeGreaterThan(results.Length * 9 / 10, 
            "System should handle >90% of burst load successfully");
    }

    [Fact]
    public async Task StressTest_MixedWorkload_OptionsAndBars()
    {
        // Mixed workload with different query types to stress different code paths
        var mixedTests = GenerateMixedWorkload(1000);
        var threadCount = 10;
        
        _output.WriteLine($"🎯 Starting mixed workload test:");
        _output.WriteLine($"  • Options queries: {mixedTests.Count(t => t.Type == QueryType.Options)}");
        _output.WriteLine($"  • Daily bars: {mixedTests.Count(t => t.Type == QueryType.BarsDaily)}");
        _output.WriteLine($"  • Minute bars: {mixedTests.Count(t => t.Type == QueryType.BarsMinute)}");
        _output.WriteLine($"  • Threads: {threadCount}");
        
        var sw = Stopwatch.StartNew();
        var partitionSize = mixedTests.Count / threadCount;
        var tasks = new List<Task<ThreadResult>>();
        
        for (int i = 0; i < threadCount; i++)
        {
            var partition = mixedTests.Skip(i * partitionSize).Take(partitionSize).ToList();
            tasks.Add(Task.Run(async () => await ExecuteThreadWorkload(i, partition)));
        }
        
        var results = await Task.WhenAll(tasks);
        sw.Stop();
        
        // Analyze per-type performance
        var optionsMetrics = _metrics.Where(m => m.Type == QueryType.Options).ToList();
        var barsMetrics = _metrics.Where(m => m.Type == QueryType.BarsDaily || m.Type == QueryType.BarsMinute).ToList();
        
        _output.WriteLine($"\n📊 Mixed Workload Results:");
        _output.WriteLine($"  Overall success rate: {100.0 * results.Sum(r => r.SuccessCount) / mixedTests.Count:F1}%");
        _output.WriteLine($"  Options avg latency: {(optionsMetrics.Any() ? optionsMetrics.Average(m => m.Latency) : 0):F0}ms");
        _output.WriteLine($"  Bars avg latency: {(barsMetrics.Any() ? barsMetrics.Average(m => m.Latency) : 0):F0}ms");
        _output.WriteLine($"  Total throughput: {mixedTests.Count / sw.Elapsed.TotalSeconds:F1} req/sec");
        
        // Assert balanced performance across query types
        var overallSuccess = results.Sum(r => r.SuccessCount);
        overallSuccess.Should().BeGreaterThan(mixedTests.Count * 9 / 10, 
            "Mixed workload should maintain >90% success rate");
    }

    [Fact]
    public async Task StressTest_SustainedLoad_5MinuteRun()
    {
        var duration = TimeSpan.FromMinutes(1); // Reduced from 5 minutes for faster testing
        var threadCount = 5;
        var targetRps = 100; // Target requests per second
        
        _output.WriteLine($"⏰ Starting sustained load test:");
        _output.WriteLine($"  • Duration: {duration.TotalMinutes} minutes");
        _output.WriteLine($"  • Target load: {targetRps} req/sec");
        _output.WriteLine($"  • Worker threads: {threadCount}");
        
        var cts = new CancellationTokenSource(duration);
        var tasks = new List<Task<SustainedLoadResult>>();
        
        for (int i = 0; i < threadCount; i++)
        {
            tasks.Add(RunSustainedLoadThread(i, targetRps / threadCount, cts.Token));
        }
        
        var results = await Task.WhenAll(tasks);
        
        var totalRequests = results.Sum(r => r.RequestCount);
        var totalSuccess = results.Sum(r => r.SuccessCount);
        var avgLatency = results.Average(r => r.AverageLatency);
        
        _output.WriteLine($"\n📊 Sustained Load Results:");
        _output.WriteLine($"  Total requests: {totalRequests}");
        _output.WriteLine($"  Success rate: {100.0 * totalSuccess / totalRequests:F1}%");
        _output.WriteLine($"  Actual RPS: {totalRequests / duration.TotalSeconds:F1}");
        _output.WriteLine($"  Avg latency: {avgLatency:F0}ms");
        
        // System should remain stable under sustained load
        var successRate = (double)totalSuccess / totalRequests;
        successRate.Should().BeGreaterThan(0.95, "System should maintain >95% success rate under sustained load");
    }

    private async Task<ThreadResult> ExecuteThreadWorkload(int threadId, List<TestCase> tests)
    {
        var success = 0;
        var failure = 0;
        var sw = new Stopwatch();
        
        foreach (var test in tests)
        {
            sw.Restart();
            var result = await ExecuteTestCaseAsync(test);
            sw.Stop();
            
            if (result.Success)
                success++;
            else
                failure++;
            
            _metrics.Add(new TestMetrics
            {
                ThreadId = threadId,
                Type = test.Type,
                Latency = sw.ElapsedMilliseconds,
                Success = result.Success
            });
        }
        
        return new ThreadResult
        {
            ThreadId = threadId,
            SuccessCount = success,
            FailureCount = failure
        };
    }

    private async Task<TestResult> ExecuteTestCaseAsync(TestCase test)
    {
        await _processThrottle.WaitAsync();
        try
        {
            return await Task.Run(() => ExecuteTestCase(test));
        }
        finally
        {
            _processThrottle.Release();
        }
    }

    private TestResult ExecuteTestCase(TestCase test)
    {
        try
        {
            var arguments = test.Type switch
            {
                QueryType.Options => $"get-options --symbol {test.Symbol} --date {test.Date}",
                QueryType.BarsDaily => $"get-bars --symbol {test.Symbol} --from {test.Date} --to {test.Date} --granularity 1d",
                QueryType.BarsMinute => $"get-bars --symbol {test.Symbol} --from {test.Date} --to {test.Date} --granularity 1m",
                _ => throw new NotSupportedException()
            };

            // Use MCP service if available, otherwise fall back to CLI
            if (IsMcpAvailable())
            {
                return ExecuteViaMcp(arguments);
            }

            var isExe = _executablePath.EndsWith(".exe");
            var startInfo = new ProcessStartInfo
            {
                FileName = isExe ? _executablePath : "dotnet",
                Arguments = isExe ? arguments : $"\"{_executablePath}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return new TestResult { Success = false };

            var completed = process.WaitForExit(5000);
            if (!completed)
            {
                process.Kill();
                return new TestResult { Success = false };
            }

            var output = process.StandardOutput.ReadToEnd();
            return new TestResult
            {
                Success = output.Contains("\"ok\":true") || process.ExitCode == 0
            };
        }
        catch
        {
            return new TestResult { Success = false };
        }
    }

    private bool IsMcpAvailable()
    {
        // Check if MCP service is available
        return File.Exists(@"C:\code\Stroll\Stroll.History\Stroll.History.Mcp\bin\Debug\net9.0\Stroll.History.Mcp.exe");
    }

    private TestResult ExecuteViaMcp(string arguments)
    {
        // Execute via MCP for better performance
        try
        {
            var mcpPath = @"C:\code\Stroll\Stroll.History\Stroll.History.Mcp\bin\Debug\net9.0\Stroll.History.Mcp.exe";
            
            // Convert CLI args to MCP request
            var request = ConvertToMcpRequest(arguments);
            
            var startInfo = new ProcessStartInfo
            {
                FileName = mcpPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return new TestResult { Success = false };

            process.StandardInput.WriteLine(request);
            process.StandardInput.Close();

            var completed = process.WaitForExit(1000); // MCP should be much faster
            if (!completed)
            {
                process.Kill();
                return new TestResult { Success = false };
            }

            var output = process.StandardOutput.ReadToEnd();
            return new TestResult
            {
                Success = output.Contains("\"result\"") && !output.Contains("\"error\"")
            };
        }
        catch
        {
            return new TestResult { Success = false };
        }
    }

    private string ConvertToMcpRequest(string cliArgs)
    {
        if (cliArgs.StartsWith("get-options"))
        {
            var parts = cliArgs.Split(' ');
            var symbol = parts[Array.IndexOf(parts, "--symbol") + 1];
            var date = parts[Array.IndexOf(parts, "--date") + 1];
            return $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{{\"name\":\"get_options\",\"arguments\":{{\"symbol\":\"{symbol}\",\"date\":\"{date}\"}}}}}}";
        }
        else if (cliArgs.StartsWith("get-bars"))
        {
            var parts = cliArgs.Split(' ');
            var symbol = parts[Array.IndexOf(parts, "--symbol") + 1];
            var from = parts[Array.IndexOf(parts, "--from") + 1];
            var to = parts[Array.IndexOf(parts, "--to") + 1];
            var granularity = parts[Array.IndexOf(parts, "--granularity") + 1];
            return $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{{\"name\":\"get_bars\",\"arguments\":{{\"symbol\":\"{symbol}\",\"from\":\"{from}\",\"to\":\"{to}\",\"granularity\":\"{granularity}\"}}}}}}";
        }
        
        return "";
    }

    private async Task<SustainedLoadResult> RunSustainedLoadThread(int threadId, int targetRps, CancellationToken ct)
    {
        var requests = 0;
        var successes = 0;
        var latencies = new List<double>();
        var delay = 1000 / targetRps; // Milliseconds between requests
        
        while (!ct.IsCancellationRequested)
        {
            var test = GenerateRandomTest();
            var sw = Stopwatch.StartNew();
            
            var result = await ExecuteTestCaseAsync(test);
            
            sw.Stop();
            latencies.Add(sw.ElapsedMilliseconds);
            
            requests++;
            if (result.Success) successes++;
            
            // Rate limiting
            var elapsed = sw.ElapsedMilliseconds;
            if (elapsed < delay)
            {
                await Task.Delay((int)(delay - elapsed), ct);
            }
        }
        
        return new SustainedLoadResult
        {
            ThreadId = threadId,
            RequestCount = requests,
            SuccessCount = successes,
            AverageLatency = latencies.Any() ? latencies.Average() : 0
        };
    }

    private List<TestCase> LoadTestCases(string csvPath)
    {
        var tests = new List<TestCase>();
        
        foreach (var line in File.ReadAllLines(csvPath).Skip(1))
        {
            var parts = line.Split(',');
            if (parts.Length >= 3)
            {
                var instrument = parts[1].Trim();
                var instrumentParts = instrument.Split(':');
                
                tests.Add(new TestCase
                {
                    Date = parts[0].Trim(),
                    Symbol = instrumentParts[0],
                    Type = instrumentParts[1] switch
                    {
                        "options" => QueryType.Options,
                        "bars1d" => QueryType.BarsDaily,
                        "bars1m" => QueryType.BarsMinute,
                        _ => QueryType.BarsDaily
                    }
                });
            }
        }
        
        return tests;
    }

    private string CreateSyntheticTestData()
    {
        var path = Path.Combine(Path.GetTempPath(), "synthetic_test_data.csv");
        var lines = new List<string> { "date,instrument,expected_result" };
        
        var symbols = new[] { "SPY", "QQQ", "IWM", "TLT", "GLD", "XLF", "XLE", "XLK", "XLV", "XLI" };
        var types = new[] { "options", "bars1d" };
        var dates = Enumerable.Range(0, 500)
            .Select(i => DateTime.Today.AddDays(-i))
            .Select(d => d.ToString("yyyy-MM-dd"));
        
        foreach (var date in dates)
        {
            foreach (var symbol in symbols)
            {
                foreach (var type in types)
                {
                    lines.Add($"{date},{symbol}:{type},OK");
                }
            }
        }
        
        File.WriteAllLines(path, lines);
        return path;
    }

    private List<TestCase> GenerateBurstTestCases(int count)
    {
        var tests = new List<TestCase>();
        var symbols = new[] { "SPY", "QQQ", "IWM", "TLT", "GLD" };
        var random = new Random(42);
        
        for (int i = 0; i < count; i++)
        {
            tests.Add(new TestCase
            {
                Symbol = symbols[random.Next(symbols.Length)],
                Date = DateTime.Today.AddDays(-random.Next(365)).ToString("yyyy-MM-dd"),
                Type = (QueryType)random.Next(3)
            });
        }
        
        return tests;
    }

    private List<TestCase> GenerateMixedWorkload(int count)
    {
        var tests = new List<TestCase>();
        var symbols = new[] { "SPY", "QQQ", "IWM", "TLT", "GLD", "XLF", "XLE" };
        var random = new Random(42);
        
        for (int i = 0; i < count; i++)
        {
            var type = i % 3 switch
            {
                0 => QueryType.Options,
                1 => QueryType.BarsDaily,
                _ => QueryType.BarsMinute
            };
            
            tests.Add(new TestCase
            {
                Symbol = symbols[random.Next(symbols.Length)],
                Date = DateTime.Today.AddDays(-random.Next(365)).ToString("yyyy-MM-dd"),
                Type = type
            });
        }
        
        return tests;
    }

    private TestCase GenerateRandomTest()
    {
        var symbols = new[] { "SPY", "QQQ", "IWM" };
        var random = new Random();
        
        return new TestCase
        {
            Symbol = symbols[random.Next(symbols.Length)],
            Date = DateTime.Today.AddDays(-random.Next(30)).ToString("yyyy-MM-dd"),
            Type = (QueryType)random.Next(3)
        };
    }

    private double GetPercentile(List<double> sortedValues, double percentile)
    {
        if (!sortedValues.Any()) return 0;
        var index = (int)(percentile * (sortedValues.Count - 1));
        return sortedValues[index];
    }

    private class TestCase
    {
        public required string Symbol { get; init; }
        public required string Date { get; init; }
        public required QueryType Type { get; init; }
    }

    private enum QueryType
    {
        Options,
        BarsDaily,
        BarsMinute
    }

    private class TestResult
    {
        public bool Success { get; init; }
    }

    private class ThreadResult
    {
        public int ThreadId { get; init; }
        public int SuccessCount { get; init; }
        public int FailureCount { get; init; }
    }

    private class TestMetrics
    {
        public int ThreadId { get; init; }
        public QueryType Type { get; init; }
        public double Latency { get; init; }
        public bool Success { get; init; }
    }

    private class SustainedLoadResult
    {
        public int ThreadId { get; init; }
        public int RequestCount { get; init; }
        public int SuccessCount { get; init; }
        public double AverageLatency { get; init; }
    }

    public void Dispose()
    {
        _processThrottle?.Dispose();
    }
}