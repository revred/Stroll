# Stroll Backtest Tests

## Overview

This project implements a comprehensive backtesting framework for the **SPX 1DTE (1 Day To Expiration)** options strategy, covering the period from **September 9, 1999 to August 24, 2025**.

## Key Features

### 🎯 Strategy Implementation
- **1DTE Iron Condor Strategy**: Daily expiring SPX options with systematic entry/exit rules
- **Risk Management**: Profit targets, stop losses, position sizing, and drawdown controls
- **Market Hours Awareness**: Proper timing for entries (9:45 AM ET) and exits (3:45 PM ET)

### 🔧 RealFill Dynamics Engine
Borrowed from the ODTE project infrastructure:
- **Realistic Execution Modeling**: Based on bid-ask spreads, market impact, and slippage
- **Market Microstructure**: Simulates latency, execution quality, and fill probability
- **Zero-Simulation Overhead**: High-performance execution with deterministic results

### 📊 Performance Analytics
- **Comprehensive Metrics**: Total return, annualized return, Sharpe ratio, max drawdown
- **Trade Statistics**: Win rate, average win/loss, profit factor, trade frequency
- **Risk Analysis**: Drawdown analysis, risk-adjusted returns, volatility metrics

## Architecture

```
Stroll.Backtest.Tests/
├── Core/
│   └── RealFillEngine.cs          # ODTE-derived execution engine
├── Strategy/
│   └── SpxOneDteStrategy.cs       # 1DTE Iron Condor implementation
├── Backtests/
│   └── SpxOneDteBacktestRunner.cs # Main backtest orchestrator
└── SpxOneDteBacktestTests.cs      # Integration tests
```

## Integration Points

### Stroll.Storage Integration
- **Direct Database Access**: Uses Stroll.Storage for SPX historical data
- **High-Performance Queries**: Leverages optimized SQLite storage with caching
- **Data Integrity**: Handles missing data gracefully with proper error handling

### ODTE Infrastructure Adoption
- **RealisticFillEngine**: Market-microstructure-aware execution simulation
- **ExecutionProfile**: Configurable slippage and latency parameters
- **MarketConditions**: Volume, volatility, and timing-aware fill modeling

## Strategy Details

### Entry Rules
- **Timing**: Enter positions at 9:45 AM ET (15 minutes after market open)
- **Expiration**: Only trade 1DTE (next business day expiration)
- **Structure**: Short iron condor (short strangle + long wings for protection)
- **Strike Selection**: Target ~15 delta options based on expected move

### Exit Rules
- **Profit Target**: Close at 50% of credit received
- **Stop Loss**: Close at 3x credit received (loss)
- **Time-based**: Force close at 3:45 PM ET or 30 minutes before expiration
- **Risk Management**: Maximum 3 concurrent positions

### Risk Controls
- **Position Sizing**: Fixed contract size with account-based scaling
- **Drawdown Limits**: Maximum 50% drawdown threshold
- **Correlation Management**: Limit to SPX-only exposure
- **Execution Quality**: Market orders with realistic slippage modeling

## Usage

### Running the Backtest

```csharp
// Set up storage and logging
var catalog = DataCatalog.Default("./Data");
using var storage = new CompositeStorage(catalog);
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger<SpxOneDteBacktestRunner>();

// Initialize and run backtest
var runner = new SpxOneDteBacktestRunner(storage, logger);
var result = await runner.RunBacktestAsync();

// Analyze results
Console.WriteLine($"Total Return: {result.TotalReturn:P2}");
Console.WriteLine($"Annualized Return: {result.AnnualizedReturn:P2}");
Console.WriteLine($"Max Drawdown: {result.MaxDrawdown:P2}");
Console.WriteLine($"Win Rate: {result.WinRate:P1}");
Console.WriteLine($"Profit Factor: {result.ProfitFactor:F2}");
```

### Running Tests

```bash
dotnet test --logger "console;verbosity=detailed"
```

## Expected Performance Characteristics

Based on historical 1DTE options strategies:

### Target Metrics
- **Win Rate**: 60-80% (high-probability trades)
- **Profit Factor**: 1.2-2.0 (risk-adjusted profitability)
- **Max Drawdown**: <30% (controlled risk)
- **Annualized Return**: 15-25% (reasonable expectations)

### Risk Considerations
- **Tail Risk**: Black swan events can cause significant losses
- **Gamma Risk**: Short options positions sensitive to large moves
- **Time Decay**: Benefits from theta decay but vulnerable to gap risk
- **Liquidity Risk**: SPX options generally liquid but can widen in stress

## Data Requirements

### Historical Data Sources
- **SPX Daily Bars**: Close prices for underlying movement
- **Implied Volatility**: VIX data for volatility estimation
- **Economic Calendar**: Market holidays and early closes
- **Options Chain**: Strike prices and expiration dates

### Data Quality
- **Missing Data Handling**: Skip days with insufficient data
- **Holiday Adjustments**: Account for market closures
- **Corporate Actions**: SPX adjustments and special situations

## Performance Optimizations

### Execution Efficiency
- **Deterministic Fills**: Seeded random number generation for reproducible results
- **Vectorized Calculations**: Batch processing of option pricing
- **Memory Management**: Efficient data structures and garbage collection

### Database Performance
- **Cached Queries**: Leverage Stroll.Storage caching layer
- **Indexed Access**: Optimized date range queries
- **Connection Pooling**: Reuse database connections

## Testing Strategy

### Unit Tests
- **RealFillEngine**: Validate execution simulation accuracy
- **Strategy Logic**: Test entry/exit signal generation
- **Risk Management**: Verify position sizing and stops

### Integration Tests
- **Full Backtest**: End-to-end system validation
- **Performance Tests**: Execution time benchmarks
- **Data Integration**: Storage layer compatibility

### Validation Tests
- **Risk Metrics**: Reasonable drawdown and volatility
- **Trade Frequency**: Expected number of trades per period
- **Fill Quality**: Realistic execution costs and slippage

## Future Enhancements

### Strategy Extensions
- **Multi-DTE**: 0DTE, 2DTE, and weekly strategies
- **Dynamic Sizing**: Volatility-adjusted position sizing
- **Market Regime**: Bull/bear market adaptations
- **Correlation Filters**: Cross-asset risk management

### Infrastructure Improvements
- **Real-time Data**: Live market data integration
- **Options Pricing**: Black-Scholes and stochastic volatility models
- **Portfolio Analytics**: Multi-strategy performance attribution
- **Risk Monitoring**: Real-time P&L and Greeks tracking

## Dependencies

- **.NET 9.0**: Latest runtime for performance optimizations
- **Stroll.Storage**: High-performance market data access
- **FluentAssertions**: Comprehensive test validation
- **Microsoft.Extensions.Logging**: Structured logging framework

## License

Part of the Stroll trading system - internal use only.