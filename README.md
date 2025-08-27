# Stroll - High-Performance Options Backtesting System

A blazing-fast backtesting framework for SPX 1DTE (One Day To Expiration) options strategies, achieving near ChatGPT-level performance through advanced optimizations.

## 🚀 Performance Achievements

- **61x speedup** over baseline implementation
- **221ms** to process 6 months of historical data (35,931 bars)
- **2.26 years/second** processing rate (67.9% of ChatGPT's 3.33 years/sec benchmark)
- **98.4% performance improvement** through smart optimizations

## 📁 Project Structure

```
Stroll/
├── docs/              # Consolidated documentation
├── scripts/           # Build and utility scripts  
├── tools/             # Development tools and utilities
├── Stroll.History/    # Historical data system
├── Stroll.Runner/     # Backtesting engine
├── Stroll.Runtime/    # Runtime services
└── Stroll.Strategy/   # Trading strategies (future)
```

### Core Modules

#### Stroll.History
Historical data acquisition and storage system with SQLite-based archive.
- **Stroll.Historical**: Main historical data service with MCP (Model Context Protocol) integration
- **Stroll.History.Mcp**: MCP server implementation for AI assistant integration
- **Stroll.Dataset**: Data packaging and distribution
- **Stroll.Storage**: Composite storage layer supporting ODTE and custom formats
- **Stroll.Polygon.IO**: Optional Git subproject for data acquisition (only needed for new data ingestion)

#### Stroll.Runner
Backtesting execution engine and test suites.
- **Stroll.Backtest.Tests**: Comprehensive backtest implementations and performance benchmarks
- **Stroll.History.Integrity.Tests**: Data integrity verification

#### Stroll.Runtime
Runtime services and process management.
- **Stroll.Process**: Process lifecycle management
- **Stroll.Watchdog**: Service monitoring and recovery
- **Stroll.PrettyTest**: Enhanced test reporting
- **Stroll.TestMcp**: MCP integration testing

#### Stroll.Strategy  
Trading strategy components and signals.
- **Stroll.Model**: Core domain models
- **Stroll.Signal**: Signal generation framework
- **Stroll.Revset**: Review and revision tracking

### Optional Components

#### Stroll.Polygon.IO
**Note**: This is a Git subproject located in `Stroll.History/Stroll.Polygon.IO/` and is only activated when:
- New data acquisition from Polygon.io is needed
- Data ingestion tasks are required
- Expanding historical coverage beyond existing datasets

The system works completely without this component using existing historical archives.

## ⚡ Key Features

### MCP (Model Context Protocol) Integration
- **AI Assistant Integration**: Direct integration with Claude, ChatGPT, and other AI assistants
- **Tool Functions**: Expose backtesting capabilities as MCP tools
- **Streaming Results**: Real-time streaming of backtest progress to AI assistants
- **Context Management**: Efficient context handling for large datasets

### Historical Data System
- **SQLite Archive**: Efficient storage of 35,931+ five-minute bars
- **Alpha Vantage Integration**: Automated data acquisition with rate limiting
- **Bar Magnifier**: Synthetic 1-minute data generation from 5-minute bars
- **Direct Database Access**: High-performance SQLite queries

### Performance Optimizations
1. **Bulk Data Loading**: Single SQL query instead of thousands
2. **Compiled Expressions**: Pre-compiled strategy rules (2.4x faster evaluation)
3. **Streaming Processing**: Process data as it's read from database
4. **Memory Optimization**: Minimal object allocations
5. **No Bar Magnifier Mode**: Direct 5-minute bar processing when precision not required

### Strategy Implementation
- **SPX 1DTE Iron Condor**: Production-ready implementation
- **Realistic Fill Engine**: Market-accurate order simulation
- **Audit Ledger**: Cryptographic integrity for backtest results

## 🏃 Quick Start

### Prerequisites
- .NET 9.0 SDK
- SQLite
- Alpha Vantage API key (for data acquisition)

### Installation
```bash
git clone https://github.com/yourusername/Stroll.git
cd Stroll
dotnet build
```

### Running with MCP (Model Context Protocol)
```bash
cd Stroll.History/Stroll.Historical
dotnet run -- mcp-server
```

This enables AI assistants to directly interact with the backtesting system through MCP tools.

### Running Performance Tests
```bash
cd Stroll.Runner/Stroll.Backtest.Tests
dotnet test --filter "Show_Performance_Results"
```

### Example Backtest
```csharp
var archivePath = @"C:\code\Stroll\Stroll.History\Stroll.Historical\historical_archive\historical_archive.db";
var runner = new HistoricalArchiveBacktestRunner(archivePath);
var result = await runner.RunSixMonthBacktestAsync();
Console.WriteLine($"Final Value: ${result.FinalAccountValue:N0}");
```

## 📊 Performance Benchmarks

| Implementation | Time (6 months) | Speed | vs Baseline |
|----------------|-----------------|--------|-------------|
| Baseline | 13,491ms | 0.04 years/sec | 1.0x |
| Bulk Loading | 1,100ms | 0.45 years/sec | 12.3x |
| No Magnifier | 450ms | 1.11 years/sec | 30.0x |
| Combined Optimizations | 221ms | 2.26 years/sec | 61.0x |
| **ChatGPT Target** | **180ms** | **3.33 years/sec** | **75.0x** |

## 🔧 Configuration

### Historical Data Settings
Configure in `Stroll.History/configs/column_hints.yml`:
```yaml
data_path: "./Data"
archive_path: "./historical_archive"
api_key: "YOUR_ALPHA_VANTAGE_KEY"
```

### Strategy Parameters
Modify in strategy classes:
- Entry time: 9:45 AM
- Iron Condor strikes: Dynamic based on IV
- Position limits: 2 concurrent positions
- Exit conditions: Time-based or P&L triggers

## 📈 Data Coverage

Current historical archive includes:
- **Symbol**: SPY (S&P 500 ETF as SPX proxy)
- **Period**: 2020-01-02 to 2024-02-29
- **Granularity**: 5-minute bars
- **Total Bars**: 35,931
- **Size**: ~15MB (SQLite compressed)

## 🧪 Testing

Run all tests:
```bash
dotnet test
```

Performance benchmarks only:
```bash
dotnet test --filter "Category=Performance"
```

## 📚 Documentation

- **[Main Documentation](docs/README.md)**: Consolidated documentation index
- **[Coding Guidelines](docs/CODING_GUIDELINES.md)**: Development standards and conventions
- **[Scripts](scripts/README.md)**: Build and utility scripts documentation
- **[Tools](tools/README.md)**: Development tools and utilities
- **Module Documentation**: Each Stroll.* module contains its own README.md

## 🤝 Contributing

Contributions are welcome! Please ensure:
- All tests pass
- Performance benchmarks show no regression  
- Code follows the [coding guidelines](docs/CODING_GUIDELINES.md)
- Documentation is updated accordingly

## 📝 License

[Your License Here]

## 🙏 Acknowledgments

- Alpha Vantage for historical data API
- ChatGPT for setting the performance benchmark target
- Claude for implementation assistance