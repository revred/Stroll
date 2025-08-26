# MCP (Model Context Protocol) Integration

## Overview

The Model Context Protocol (MCP) enables seamless integration between the Stroll backtesting system and AI assistants like Claude, ChatGPT, and others. This allows AI assistants to directly execute backtests, analyze results, and optimize strategies through a standardized protocol.

## 🎯 Purpose

MCP solves the challenge of connecting AI assistants to external tools and data sources by providing:
- **Standardized Communication**: Common protocol for all AI assistants
- **Tool Discovery**: Automatic discovery of available backtesting functions
- **Type Safety**: Strongly-typed parameters and return values
- **Streaming Support**: Real-time progress updates during long-running backtests
- **Context Management**: Efficient handling of large datasets without overwhelming context windows

## 🔧 Architecture

### MCP Server (Stroll.History.Mcp)
```
AI Assistant <--> MCP Protocol <--> Stroll MCP Server <--> Backtesting Engine
                                            |
                                            v
                                    SQLite Database
```

### Available MCP Tools

#### 1. RunBacktest
Execute a complete backtest with specified parameters.
```json
{
  "tool": "run_backtest",
  "parameters": {
    "symbol": "SPY",
    "startDate": "2020-01-01",
    "endDate": "2020-06-30",
    "strategy": "SPX_1DTE_IronCondor"
  }
}
```

#### 2. GetHistoricalData
Retrieve historical price data for analysis.
```json
{
  "tool": "get_historical_data",
  "parameters": {
    "symbol": "SPY",
    "startDate": "2024-01-01",
    "endDate": "2024-01-31",
    "granularity": "5min"
  }
}
```

#### 3. OptimizeStrategy
Run parameter optimization for a strategy.
```json
{
  "tool": "optimize_strategy",
  "parameters": {
    "strategy": "SPX_1DTE_IronCondor",
    "optimizeFor": "sharpe_ratio",
    "parameterRanges": {
      "deltaTarget": [0.10, 0.20],
      "stopLoss": [50, 200]
    }
  }
}
```

#### 4. GetPerformanceMetrics
Calculate detailed performance metrics for a backtest.
```json
{
  "tool": "get_performance_metrics",
  "parameters": {
    "backtestId": "12345-abcde",
    "metrics": ["sharpe", "sortino", "maxDrawdown", "winRate"]
  }
}
```

## 🚀 Usage Examples

### From Claude Desktop
```yaml
# claude_desktop_config.yaml
mcpServers:
  stroll:
    command: "dotnet"
    args: ["run", "--project", "C:/code/Stroll/Stroll.History/Stroll.Historical", "--", "mcp-server"]
    env:
      ALPHA_VANTAGE_API_KEY: "your_key"
```

### From Python Client
```python
import mcp_client

client = mcp_client.connect("stroll-mcp-server")
result = await client.call_tool("run_backtest", {
    "symbol": "SPY",
    "startDate": "2020-01-01",
    "endDate": "2020-12-31",
    "strategy": "SPX_1DTE_IronCondor"
})
print(f"Final P&L: ${result['finalValue']:,.2f}")
```

### From TypeScript
```typescript
import { MCPClient } from '@modelcontextprotocol/sdk';

const client = new MCPClient('stroll-mcp-server');
const result = await client.callTool('run_backtest', {
  symbol: 'SPY',
  startDate: '2020-01-01',
  endDate: '2020-12-31',
  strategy: 'SPX_1DTE_IronCondor'
});
```

## 📊 Performance

MCP adds minimal overhead to backtest execution:
- **Protocol overhead**: < 1ms per tool call
- **Serialization**: < 5ms for typical result sets
- **Streaming updates**: Every 100ms during execution

## 🔐 Security

- **Authentication**: Optional API key authentication
- **Rate Limiting**: Configurable rate limits per client
- **Sandboxing**: Backtests run in isolated context
- **Data Access**: Read-only access to historical data

## 🧪 Testing MCP Integration

### Test Server Startup
```bash
dotnet run -- mcp-server --test
```

### Test Tool Discovery
```bash
curl http://localhost:5000/mcp/tools
```

### Test Backtest Execution
```bash
curl -X POST http://localhost:5000/mcp/call \
  -H "Content-Type: application/json" \
  -d '{"tool": "run_backtest", "parameters": {...}}'
```

## 🔧 Configuration

### Server Configuration
```json
{
  "mcp": {
    "port": 5000,
    "maxConcurrentBacktests": 4,
    "timeoutSeconds": 300,
    "enableStreaming": true,
    "authentication": {
      "enabled": false,
      "apiKeyHeader": "X-API-Key"
    }
  }
}
```

### Client Registration
```json
{
  "clients": [
    {
      "name": "claude-desktop",
      "apiKey": "optional-key",
      "rateLimit": 100,
      "allowedTools": ["*"]
    }
  ]
}
```

## 📈 Monitoring

MCP server provides metrics:
- Total tool calls
- Average execution time
- Error rates
- Active connections
- Memory usage

Access metrics at: `http://localhost:5000/mcp/metrics`

## 🚧 Troubleshooting

### Common Issues

1. **Connection Refused**
   - Ensure MCP server is running
   - Check firewall settings
   - Verify port availability

2. **Tool Not Found**
   - Update tool registry
   - Check tool naming conventions
   - Verify permissions

3. **Timeout Errors**
   - Increase timeout settings
   - Optimize backtest queries
   - Check database performance

## 🔮 Future Enhancements

- WebSocket support for real-time streaming
- Multi-strategy parallel execution
- Distributed backtesting support
- Real-time data feed integration
- Advanced caching layer

## 📚 References

- [MCP Specification](https://modelcontextprotocol.io/specification)
- [Stroll Documentation](../README.md)
- [Performance Guide](../../Stroll.Runner/Stroll.Backtest.Tests/PERFORMANCE.md)