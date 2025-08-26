using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stroll.Dataset;
using Stroll.Storage;
using Stroll.History.Mcp.Services;
using System.Text.Json;

namespace Stroll.History.Mcp;

/// <summary>
/// Stroll History MCP Service
/// 
/// High-performance financial data access via Model Context Protocol.
/// Provides blazing fast access to historical market data, options chains,
/// and provider status information.
/// 
/// Performance targets:
/// - Tool calls: &lt;5ms (vs 200ms+ previous IPC)
/// - Concurrent requests: 1000+ req/sec  
/// - Memory usage: &lt;100MB base
/// - Success rate: &gt;99.5%
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var host = CreateHostBuilder(args).Build();
            
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("🚀 Stroll History MCP Service starting...");
            logger.LogInformation("Performance targets: <5ms tool calls, 1000+ req/sec, >99.5% success rate");
            
            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                schema = "stroll.history.v1",
                ok = false,
                error = new
                {
                    code = "STARTUP_FAILURE",
                    message = ex.Message
                }
            }));
            return 1;
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Core Stroll components - direct access for maximum performance
                services.AddSingleton(DataCatalog.Default(Environment.GetEnvironmentVariable("STROLL_DATA")));
                services.AddSingleton<IStorageProvider>(provider => 
                {
                    var catalog = provider.GetRequiredService<DataCatalog>();
                    return new CompositeStorage(catalog);
                });
                services.AddSingleton<IPackager>(provider => 
                    new HighPerformancePackager("stroll.history.v1", "1.0.0"));
                
                // MCP Services
                services.AddSingleton<McpServer>();
                services.AddSingleton<HistoryService>();
                services.AddSingleton<PerformanceMetrics>();
                
                // Background service to run MCP server
                services.AddHostedService<McpHostedService>();
            })
            .ConfigureLogging(logging =>
            {
                // Configure logging based on environment
                var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL")?.ToLowerInvariant() switch
                {
                    "debug" => LogLevel.Debug,
                    "warn" => LogLevel.Warning,
                    "error" => LogLevel.Error,
                    _ => LogLevel.Information
                };
                
                logging.SetMinimumLevel(logLevel);
                logging.AddConsole();
            });
}