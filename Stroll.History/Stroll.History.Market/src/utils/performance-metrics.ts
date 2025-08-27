/**
 * Performance Metrics Collection for Stroll History MCP Service
 * 
 * Tracks and reports performance metrics to demonstrate improvements over IPC.
 * Key metrics:
 * - Tool call latency (target: <5ms vs 200ms+ IPC)
 * - Throughput (target: 1000+ req/sec)
 * - Success rate (target: >99.5%)
 * - Memory usage patterns
 */

interface ToolCallMetric {
  name: string;
  duration: number;
  success: boolean;
  timestamp: number;
}

interface PerformanceStats {
  totalRequests: number;
  successfulRequests: number;
  failedRequests: number;
  averageLatency: number;
  p50Latency: number;
  p95Latency: number;
  p99Latency: number;
  successRate: number;
  requestsPerSecond: number;
  toolStats: Map<string, {
    count: number;
    averageLatency: number;
    successRate: number;
  }>;
}

export class PerformanceMetrics {
  private metrics: ToolCallMetric[] = [];
  private readonly maxMetrics = 10000; // Keep last 10k metrics
  private startTime: number;

  constructor() {
    this.startTime = Date.now();
  }

  recordToolCall(name: string, duration: number, success: boolean): void {
    const metric: ToolCallMetric = {
      name,
      duration,
      success,
      timestamp: Date.now(),
    };

    this.metrics.push(metric);

    // Keep only recent metrics to prevent memory growth
    if (this.metrics.length > this.maxMetrics) {
      this.metrics = this.metrics.slice(-this.maxMetrics);
    }
  }

  getStats(): PerformanceStats {
    if (this.metrics.length === 0) {
      return {
        totalRequests: 0,
        successfulRequests: 0,
        failedRequests: 0,
        averageLatency: 0,
        p50Latency: 0,
        p95Latency: 0,
        p99Latency: 0,
        successRate: 0,
        requestsPerSecond: 0,
        toolStats: new Map(),
      };
    }

    const totalRequests = this.metrics.length;
    const successfulRequests = this.metrics.filter(m => m.success).length;
    const failedRequests = totalRequests - successfulRequests;

    // Calculate latency percentiles
    const sortedDurations = this.metrics
      .map(m => m.duration)
      .sort((a, b) => a - b);

    const averageLatency = sortedDurations.reduce((a, b) => a + b, 0) / sortedDurations.length;
    const p50Latency = this.getPercentile(sortedDurations, 0.5);
    const p95Latency = this.getPercentile(sortedDurations, 0.95);
    const p99Latency = this.getPercentile(sortedDurations, 0.99);

    // Calculate requests per second
    const now = Date.now();
    const recentMetrics = this.metrics.filter(m => now - m.timestamp < 60000); // Last minute
    const requestsPerSecond = recentMetrics.length / 60;

    // Calculate per-tool statistics
    const toolStats = new Map<string, { count: number; averageLatency: number; successRate: number }>();
    const toolGroups = new Map<string, ToolCallMetric[]>();

    for (const metric of this.metrics) {
      if (!toolGroups.has(metric.name)) {
        toolGroups.set(metric.name, []);
      }
      toolGroups.get(metric.name)!.push(metric);
    }

    for (const [toolName, toolMetrics] of toolGroups) {
      const count = toolMetrics.length;
      const averageLatency = toolMetrics.reduce((sum, m) => sum + m.duration, 0) / count;
      const successCount = toolMetrics.filter(m => m.success).length;
      const successRate = (successCount / count) * 100;

      toolStats.set(toolName, { count, averageLatency, successRate });
    }

    return {
      totalRequests,
      successfulRequests,
      failedRequests,
      averageLatency,
      p50Latency,
      p95Latency,
      p99Latency,
      successRate: (successfulRequests / totalRequests) * 100,
      requestsPerSecond,
      toolStats,
    };
  }

  private getPercentile(sortedArray: number[], percentile: number): number {
    const index = percentile * (sortedArray.length - 1);
    const lower = Math.floor(index);
    const upper = Math.ceil(index);
    
    if (lower === upper) {
      return sortedArray[lower];
    }
    
    return sortedArray[lower] * (upper - index) + sortedArray[upper] * (index - lower);
  }

  getPerformanceReport(): string {
    const stats = this.getStats();
    const uptime = (Date.now() - this.startTime) / 1000;

    let report = `
🚀 Stroll History MCP Performance Report
========================================

Uptime: ${uptime.toFixed(1)}s
Total Requests: ${stats.totalRequests}
Success Rate: ${stats.successRate.toFixed(2)}%
Requests/sec: ${stats.requestsPerSecond.toFixed(1)}

Latency Metrics:
- Average: ${stats.averageLatency.toFixed(2)}ms
- P50: ${stats.p50Latency.toFixed(2)}ms  
- P95: ${stats.p95Latency.toFixed(2)}ms
- P99: ${stats.p99Latency.toFixed(2)}ms

Per-Tool Performance:
`;

    for (const [toolName, toolStat] of stats.toolStats) {
      report += `- ${toolName}: ${toolStat.count} calls, ${toolStat.averageLatency.toFixed(2)}ms avg, ${toolStat.successRate.toFixed(1)}% success\n`;
    }

    // Performance comparison with IPC
    report += `
Performance vs Previous IPC:
- Latency: ${stats.averageLatency.toFixed(2)}ms (vs 200ms+ IPC) = ${(200 / stats.averageLatency).toFixed(1)}x faster
- Success Rate: ${stats.successRate.toFixed(1)}% (vs ~55% IPC) = ${(stats.successRate / 55).toFixed(1)}x more reliable
- Throughput: ${stats.requestsPerSecond.toFixed(1)} req/sec (vs ~5 req/sec IPC) = ${(stats.requestsPerSecond / 5).toFixed(1)}x higher
`;

    return report;
  }

  exportMetrics(): ToolCallMetric[] {
    return [...this.metrics]; // Return copy
  }

  reset(): void {
    this.metrics = [];
    this.startTime = Date.now();
  }
}