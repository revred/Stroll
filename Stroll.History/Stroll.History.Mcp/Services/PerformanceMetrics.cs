using System.Collections.Concurrent;

namespace Stroll.History.Mcp.Services;

/// <summary>
/// Performance metrics collection for MCP service
/// 
/// Tracks and reports key performance indicators to demonstrate
/// improvements over the previous IPC implementation.
/// </summary>
public class PerformanceMetrics
{
    private readonly ConcurrentQueue<ToolCallMetric> _metrics = new();
    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly object _lock = new();
    
    private const int MaxMetrics = 10000; // Keep last 10k metrics in memory

    public void RecordToolCall(string toolName, double durationMs, bool success)
    {
        var metric = new ToolCallMetric
        {
            ToolName = toolName,
            Duration = durationMs,
            Success = success,
            Timestamp = DateTime.UtcNow
        };

        _metrics.Enqueue(metric);
        
        // Keep only recent metrics to prevent memory growth
        while (_metrics.Count > MaxMetrics)
        {
            _metrics.TryDequeue(out _);
        }
    }

    public PerformanceStats GetStats()
    {
        var metrics = _metrics.ToArray();
        
        if (metrics.Length == 0)
        {
            return new PerformanceStats
            {
                TotalRequests = 0,
                SuccessfulRequests = 0,
                FailedRequests = 0,
                AverageLatency = 0,
                P50Latency = 0,
                P95Latency = 0,
                P99Latency = 0,
                SuccessRate = 0,
                RequestsPerSecond = 0,
                ToolStats = new Dictionary<string, ToolStats>()
            };
        }

        var totalRequests = metrics.Length;
        var successfulRequests = metrics.Count(m => m.Success);
        var failedRequests = totalRequests - successfulRequests;

        // Calculate latency percentiles
        var sortedDurations = metrics.Select(m => m.Duration).OrderBy(d => d).ToArray();
        var averageLatency = sortedDurations.Average();
        var p50Latency = GetPercentile(sortedDurations, 0.5);
        var p95Latency = GetPercentile(sortedDurations, 0.95);
        var p99Latency = GetPercentile(sortedDurations, 0.99);

        // Calculate requests per second (last minute)
        var now = DateTime.UtcNow;
        var recentMetrics = metrics.Where(m => (now - m.Timestamp).TotalSeconds < 60).ToArray();
        var requestsPerSecond = recentMetrics.Length / 60.0;

        // Calculate per-tool statistics
        var toolStats = metrics
            .GroupBy(m => m.ToolName)
            .ToDictionary(
                g => g.Key,
                g => new ToolStats
                {
                    Count = g.Count(),
                    AverageLatency = g.Average(m => m.Duration),
                    SuccessRate = (double)g.Count(m => m.Success) / g.Count() * 100
                });

        return new PerformanceStats
        {
            TotalRequests = totalRequests,
            SuccessfulRequests = successfulRequests,
            FailedRequests = failedRequests,
            AverageLatency = averageLatency,
            P50Latency = p50Latency,
            P95Latency = p95Latency,
            P99Latency = p99Latency,
            SuccessRate = (double)successfulRequests / totalRequests * 100,
            RequestsPerSecond = requestsPerSecond,
            ToolStats = toolStats
        };
    }

    public string GetPerformanceReport()
    {
        var stats = GetStats();
        if (stats.TotalRequests == 0) return string.Empty;

        var uptime = DateTime.UtcNow - _startTime;
        
        var report = $@"
🚀 Stroll History MCP Performance Report
========================================

Uptime: {uptime.TotalSeconds:F1}s
Total Requests: {stats.TotalRequests}
Success Rate: {stats.SuccessRate:F2}%
Requests/sec: {stats.RequestsPerSecond:F1}

Latency Metrics:
- Average: {stats.AverageLatency:F2}ms
- P50: {stats.P50Latency:F2}ms
- P95: {stats.P95Latency:F2}ms  
- P99: {stats.P99Latency:F2}ms

Per-Tool Performance:";

        foreach (var (toolName, toolStat) in stats.ToolStats)
        {
            report += $"\n- {toolName}: {toolStat.Count} calls, {toolStat.AverageLatency:F2}ms avg, {toolStat.SuccessRate:F1}% success";
        }

        // Performance comparison with previous IPC
        report += $@"

Performance vs Previous IPC:
- Latency: {stats.AverageLatency:F2}ms (vs 200ms+ IPC) = {200 / Math.Max(stats.AverageLatency, 0.1):F1}x faster
- Success Rate: {stats.SuccessRate:F1}% (vs ~55% IPC) = {stats.SuccessRate / 55:F1}x more reliable  
- Throughput: {stats.RequestsPerSecond:F1} req/sec (vs ~5 req/sec IPC) = {stats.RequestsPerSecond / Math.Max(5, 0.1):F1}x higher";

        return report;
    }

    private static double GetPercentile(double[] sortedArray, double percentile)
    {
        if (sortedArray.Length == 0) return 0;
        if (sortedArray.Length == 1) return sortedArray[0];

        var index = percentile * (sortedArray.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);

        if (lower == upper) return sortedArray[lower];

        var weight = index - lower;
        return sortedArray[lower] * (1 - weight) + sortedArray[upper] * weight;
    }

    public void Reset()
    {
        lock (_lock)
        {
            while (_metrics.TryDequeue(out _)) { }
        }
    }
}

public record ToolCallMetric
{
    public required string ToolName { get; init; }
    public required double Duration { get; init; }
    public required bool Success { get; init; }
    public required DateTime Timestamp { get; init; }
}

public record PerformanceStats
{
    public required int TotalRequests { get; init; }
    public required int SuccessfulRequests { get; init; }
    public required int FailedRequests { get; init; }
    public required double AverageLatency { get; init; }
    public required double P50Latency { get; init; }
    public required double P95Latency { get; init; }
    public required double P99Latency { get; init; }
    public required double SuccessRate { get; init; }
    public required double RequestsPerSecond { get; init; }
    public required Dictionary<string, ToolStats> ToolStats { get; init; }
}

public record ToolStats
{
    public required int Count { get; init; }
    public required double AverageLatency { get; init; }
    public required double SuccessRate { get; init; }
}