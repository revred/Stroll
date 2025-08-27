using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Stroll.History.Market.Services;

/// <summary>
/// Hosted service wrapper for the MCP server
/// 
/// Manages the lifecycle of the MCP server within the .NET hosting model,
/// ensuring graceful startup and shutdown with proper error handling.
/// </summary>
public class McpHostedService : BackgroundService
{
    private readonly ILogger<McpHostedService> _logger;
    private readonly McpServer _mcpServer;

    public McpHostedService(
        ILogger<McpHostedService> logger,
        McpServer mcpServer)
    {
        _logger = logger;
        _mcpServer = mcpServer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Starting Stroll History MCP Service...");
        _logger.LogInformation("Protocol: Model Context Protocol over stdio");
        _logger.LogInformation("Performance target: <5ms tool calls, >99.5% success rate");
        
        try
        {
            await _mcpServer.StartAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("✅ MCP service stopped gracefully");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "💥 MCP service failed to start or crashed");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 Stopping MCP service...");
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("✅ MCP service stopped");
    }
}