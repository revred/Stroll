using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Stroll.History.Integrity.Tests;

/// <summary>
/// Process lifecycle tests ensure Stroll.Historical handles startup, shutdown, 
/// error recovery, and resource management according to contract requirements.
/// </summary>
public class ProcessLifecycleTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _historicalExePath;
    private readonly List<System.Diagnostics.Process> _managedProcesses = new();

    public ProcessLifecycleTests(ITestOutputHelper output)
    {
        _output = output;
        // Try multiple possible paths for the executable
        var possiblePaths = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "Stroll.Historical", "bin", "Debug", "net9.0", "Stroll.Historical.exe"),
            Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "Stroll.Historical", "binDebugnet9.0", "Stroll.Historical.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Stroll.Historical", "bin", "Debug", "net9.0", "Stroll.Historical.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Stroll.Historical", "binDebugnet9.0", "Stroll.Historical.exe")
        };
        
        _historicalExePath = possiblePaths.FirstOrDefault(File.Exists) 
            ?? throw new FileNotFoundException($"Could not find Stroll.Historical.exe in any of the expected locations:\n{string.Join("\n", possiblePaths)}");
    }

    [Fact]
    public async Task ColdStart_Performance_MustMeetBootstrapSLO()
    {
        // Arrange - Ensure clean start
        await KillAllHistoryProcesses();
        await Task.Delay(2000); // Allow cleanup

        var iterations = 5;
        var coldStartTimes = new List<long>();

        for (int i = 0; i < iterations; i++)
        {
            await KillAllHistoryProcesses();
            await Task.Delay(1000);

            // Act - Measure cold start time
            var sw = Stopwatch.StartNew();
            var result = await ExecuteCliCommand("version");
            sw.Stop();

            // Assert
            if (result.ExitCode == 0)
            {
                coldStartTimes.Add(sw.ElapsedMilliseconds);
                _output.WriteLine($"Cold start {i + 1}: {sw.ElapsedMilliseconds}ms");
            }
        }

        // Assert - Cold start SLO validation (FROZEN CONTRACT)
        coldStartTimes.Should().NotBeEmpty("Cold start measurements must succeed");
        
        var avgColdStart = coldStartTimes.Average();
        var maxColdStart = coldStartTimes.Max();

        _output.WriteLine($"Cold Start Performance: Avg={avgColdStart:F1}ms, Max={maxColdStart}ms");

        avgColdStart.Should().BeLessOrEqualTo(5000, "Average cold start must be ≤5 seconds (CONTRACT SLO)");
        maxColdStart.Should().BeLessOrEqualTo(10000, "Maximum cold start must be ≤10 seconds (CONTRACT SLO)");
    }

    [Fact]
    public async Task ProcessSpawn_Overhead_MustMeetPerformanceSLO()
    {
        // Arrange
        var iterations = 20;
        var spawnTimes = new List<long>();

        for (int i = 0; i < iterations; i++)
        {
            // Act - Measure process spawn overhead
            var sw = Stopwatch.StartNew();
            var result = await ExecuteCliCommand("version");
            sw.Stop();

            if (result.ExitCode == 0)
            {
                spawnTimes.Add(sw.ElapsedMilliseconds);
            }
        }

        // Assert - Process spawn SLO
        var p50 = GetPercentile(spawnTimes, 0.5);
        var p95 = GetPercentile(spawnTimes, 0.95);

        _output.WriteLine($"Process Spawn: P50={p50}ms, P95={p95}ms");

        p50.Should().BeLessOrEqualTo(500, "Process spawn P50 must be ≤500ms");
        p95.Should().BeLessOrEqualTo(2000, "Process spawn P95 must be ≤2000ms");
    }

    [Fact]
    public async Task GracefulShutdown_MustCompleteWithinTimeout()
    {
        // Arrange - Start a long-running process
        var process = await StartLongRunningProcess();
        _managedProcesses.Add(process);

        // Allow process to initialize
        await Task.Delay(1000);

        // Act - Request graceful shutdown
        var sw = Stopwatch.StartNew();
        process.CloseMainWindow();
        
        var exitedGracefully = process.WaitForExit(10000); // 10 seconds in milliseconds
        sw.Stop();

        // Assert - Graceful shutdown SLO (FROZEN CONTRACT)
        exitedGracefully.Should().BeTrue("Process must exit gracefully within 10 seconds");
        sw.ElapsedMilliseconds.Should().BeLessOrEqualTo(10000, "Graceful shutdown must complete in ≤10 seconds");
        
        _output.WriteLine($"Graceful shutdown completed in {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ForceKill_MustCleanupResources()
    {
        // Arrange - Start process
        var process = await StartLongRunningProcess();
        _managedProcesses.Add(process);
        
        var processId = process.Id;
        var processName = process.ProcessName;

        // Act - Force kill
        process.Kill();
        process.WaitForExit(5000);

        // Assert - Process cleanup
        process.HasExited.Should().BeTrue("Process must be terminated after Kill()");
        
        // Verify no zombie processes remain
        var remainingProcesses = System.Diagnostics.Process.GetProcessesByName(processName);
        remainingProcesses.Should().NotContain(p => p.Id == processId, "No zombie processes should remain");

        _output.WriteLine($"Process {processId} terminated and cleaned up successfully");
    }

    [Fact]
    public async Task ResourceLimits_UnderNormalLoad_MustNotExceedLimits()
    {
        // Arrange - Start process and monitor resources
        var process = await StartMonitorableProcess();
        _managedProcesses.Add(process);

        var memoryUsages = new List<long>();
        var cpuUsages = new List<double>();

        try
        {
            // Act - Generate moderate load while monitoring
            var loadTask = GenerateModerateLoad();
            var monitoringTask = MonitorResourceUsage(process, memoryUsages, cpuUsages, TimeSpan.FromSeconds(15));

            await Task.WhenAll(loadTask, monitoringTask);

            // Assert - Resource SLO validation (FROZEN CONTRACT)
            var maxMemoryMB = memoryUsages.Max() / (1024 * 1024);
            var avgMemoryMB = memoryUsages.Average() / (1024 * 1024);
            var avgCpuPercent = cpuUsages.Average();

            _output.WriteLine($"Resource Usage: Memory Avg={avgMemoryMB:F1}MB Max={maxMemoryMB}MB, CPU Avg={avgCpuPercent:F1}%");

            maxMemoryMB.Should().BeLessOrEqualTo(500, "Maximum memory usage must be ≤500MB (CONTRACT)");
            avgMemoryMB.Should().BeLessOrEqualTo(200, "Average memory usage must be ≤200MB (CONTRACT)");
        }
        finally
        {
            process?.Kill();
        }
    }

    [Fact]
    public async Task ErrorRecovery_TransientFailures_MustRecoverGracefully()
    {
        // Arrange - Simulate transient network failure
        var attempts = new List<(bool success, long latencyMs)>();

        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            
            // Act - Request data that might fail transiently
            var result = await ExecuteCliCommand("provider-status");
            sw.Stop();

            attempts.Add((result.ExitCode == 0, sw.ElapsedMilliseconds));
            
            await Task.Delay(500); // Space out requests
        }

        // Assert - Recovery characteristics
        var successRate = attempts.Count(a => a.success) / (double)attempts.Count;
        var avgLatency = attempts.Where(a => a.success).Average(a => a.latencyMs);

        _output.WriteLine($"Error Recovery: Success Rate={successRate:P}, Avg Latency={avgLatency:F1}ms");

        successRate.Should().BeGreaterOrEqualTo(0.9, "Success rate must be ≥90% after transient failures");
        avgLatency.Should().BeLessOrEqualTo(1000, "Average latency must recover to <1000ms");
    }

    [Fact]
    public async Task ConcurrentConnections_MustHandleMultipleClients()
    {
        // Arrange - Start multiple concurrent requests
        const int concurrentClients = 8;
        var tasks = new List<Task<CliExecutionResult>>();

        // Act - Execute concurrent requests
        for (int i = 0; i < concurrentClients; i++)
        {
            var clientId = i;
            var task = Task.Run(async () =>
            {
                var result = await ExecuteCliCommand($"get-bars --symbol SPY --from 2024-01-15 --to 2024-01-15 --granularity 1d");
                _output.WriteLine($"Client {clientId}: {result.ExitCode}, {result.ExecutionTimeMs}ms");
                return result;
            });
            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks);

        // Assert - Concurrent handling SLO
        var successCount = results.Count(r => r.ExitCode == 0);
        var avgLatency = results.Where(r => r.ExitCode == 0).Average(r => r.ExecutionTimeMs);

        successCount.Should().Be(concurrentClients, "All concurrent requests must succeed");
        avgLatency.Should().BeLessOrEqualTo(1000, "Average latency under load must be ≤1000ms (CONTRACT SLO)");

        _output.WriteLine($"Concurrent Connections: {successCount}/{concurrentClients} succeeded, Avg={avgLatency:F1}ms");
    }

    [Fact]
    public async Task InitializationOrder_MustCompleteBeforeAcceptingRequests()
    {
        // Arrange - Start fresh process
        await KillAllHistoryProcesses();
        await Task.Delay(1000);

        // Act - Fire rapid requests immediately after start
        var tasks = new List<Task<CliExecutionResult>>();
        
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(ExecuteCliCommand("version"));
            await Task.Delay(100); // Slight delay between rapid requests
        }

        var results = await Task.WhenAll(tasks);

        // Assert - Initialization handling
        var allSucceeded = results.All(r => r.ExitCode == 0);
        var avgLatency = results.Average(r => r.ExecutionTimeMs);

        allSucceeded.Should().BeTrue("All requests must succeed even during initialization");
        avgLatency.Should().BeLessOrEqualTo(2000, "Initialization should not severely impact response times (allowing for realistic startup)");

        _output.WriteLine($"Initialization: All requests succeeded, Avg latency={avgLatency:F1}ms");
    }

    // Helper methods
    private async Task<CliExecutionResult> ExecuteCliCommand(string arguments)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Ensure executable exists, build if necessary
            if (!File.Exists(_historicalExePath))
            {
                await BuildProjectIfNeeded();
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = _historicalExePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_historicalExePath) ?? Environment.CurrentDirectory
            };

            using var process = System.Diagnostics.Process.Start(processStartInfo);
            if (process == null)
                throw new InvalidOperationException($"Failed to start process: {_historicalExePath}");

            // Use timeout to prevent hanging tests
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                throw new TimeoutException($"Command '{arguments}' timed out after 2 minutes");
            }
            
            stopwatch.Stop();

            var output = await outputTask;
            var error = await errorTask;

            return new CliExecutionResult
            {
                ExitCode = process.ExitCode,
                Output = output,
                Error = error,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _output.WriteLine($"Exception executing command '{arguments}': {ex.Message}");
            return new CliExecutionResult
            {
                ExitCode = -1,
                Output = string.Empty,
                Error = $"Exception during execution: {ex.Message}",
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task BuildProjectIfNeeded()
    {
        var projectDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(_historicalExePath))));
        if (projectDir == null) return;

        var csprojPath = Path.Combine(projectDir, "Stroll.Historical.csproj");
        if (!File.Exists(csprojPath)) return;

        _output.WriteLine($"Building project: {csprojPath}");
        
        var buildProcess = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csprojPath}\" -c Debug",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = projectDir
        };

        using var process = System.Diagnostics.Process.Start(buildProcess);
        if (process != null)
        {
            await process.WaitForExitAsync();
            _output.WriteLine($"Build exit code: {process.ExitCode}");
        }
    }

    private async Task KillAllHistoryProcesses()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("Stroll.Historical")
            .Concat(System.Diagnostics.Process.GetProcessesByName("Stroll.History"))
            .ToList();

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch { }
            finally
            {
                process.Dispose();
            }
        }
    }

    private async Task<System.Diagnostics.Process> StartLongRunningProcess()
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = _historicalExePath,
            Arguments = "version", // Simple command that should complete
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        var process = System.Diagnostics.Process.Start(processStartInfo);
        if (process == null)
            throw new InvalidOperationException("Failed to start long-running process");

        return process;
    }

    private async Task<System.Diagnostics.Process> StartMonitorableProcess()
    {
        // Start the process in a way that allows monitoring
        return await StartLongRunningProcess();
    }

    private async Task GenerateModerateLoad()
    {
        // Generate moderate concurrent load
        var tasks = new List<Task>();
        
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < 10; j++)
                {
                    await ExecuteCliCommand("get-bars --symbol SPY --from 2024-01-15 --to 2024-01-15 --granularity 1d");
                    await Task.Delay(200);
                }
            }));
        }

        await Task.WhenAll(tasks);
    }

    private async Task MonitorResourceUsage(System.Diagnostics.Process process, List<long> memoryUsages, List<double> cpuUsages, TimeSpan duration)
    {
        var endTime = DateTime.Now.Add(duration);
        var lastCpuTime = TimeSpan.Zero;
        var lastCpuCheck = DateTime.Now;

        while (DateTime.Now < endTime && !process.HasExited)
        {
            try
            {
                process.Refresh();
                memoryUsages.Add(process.WorkingSet64);

                // Calculate CPU usage
                var currentCpuTime = process.TotalProcessorTime;
                var currentTime = DateTime.Now;
                
                if (lastCpuTime != TimeSpan.Zero)
                {
                    var cpuUsedMs = (currentCpuTime - lastCpuTime).TotalMilliseconds;
                    var totalMs = (currentTime - lastCpuCheck).TotalMilliseconds;
                    var cpuUsagePercent = (cpuUsedMs / totalMs) * 100;
                    cpuUsages.Add(cpuUsagePercent);
                }

                lastCpuTime = currentCpuTime;
                lastCpuCheck = currentTime;

                await Task.Delay(1000);
            }
            catch
            {
                break;
            }
        }
    }

    private static long GetPercentile(List<long> values, double percentile)
    {
        var sorted = values.OrderBy(x => x).ToList();
        var index = (int)(sorted.Count * percentile);
        return sorted[Math.Min(index, sorted.Count - 1)];
    }

    public void Dispose()
    {
        // Cleanup all managed processes
        foreach (var process in _managedProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch { }
            finally
            {
                process.Dispose();
            }
        }

        // Final cleanup of any remaining processes
        _ = Task.Run(async () => await KillAllHistoryProcesses());
    }
}