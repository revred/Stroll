# Stroll.History - Historical Data Management System

High-performance historical data acquisition, storage, and serving system for options backtesting.

## 📦 Components

### Stroll.Historical
Main service application providing:
- **MCP Server**: Model Context Protocol server for AI assistant integration
- **Data Acquisition**: Alpha Vantage integration with rate limiting
- **SQLite Archive**: Compressed storage of intraday bars
- **Migration Tools**: CSV to SQLite data migration

### Stroll.Dataset
Data packaging and distribution:
- **Packager**: Creates distributable data packages
- **Compression**: Efficient data compression algorithms

### Stroll.Storage
Flexible storage layer:
- **CompositeStorage**: Combines multiple storage backends
- **OdteStorage**: Specialized storage for ODTE strategies
- **Storage Interface**: Abstraction for different storage types

## 🚀 Quick Start

### Running the MCP Server
```bash
cd Stroll.Historical
dotnet run -- mcp-server
```

This starts the Model Context Protocol server, enabling AI assistants to interact with the backtesting system.

### Acquiring Historical Data
```bash
dotnet run -- acquire --symbol SPY --start 2020-01-01 --end 2024-12-31
```

### Migrating CSV Data to SQLite
```bash
dotnet run -- migrate-to-sqlite --data-path "../../../Data"
```

## 📊 Performance

- **Data Loading**: ~50ms for 35,931 bars
- **Query Speed**: <1ms for date range queries
- **Storage Size**: ~15MB for 4 years of 5-minute data
- **Compression Ratio**: ~3:1 vs CSV

## 🗄️ Database Schema

### intraday_bars Table
```sql
CREATE TABLE intraday_bars (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL,
    timestamp DATETIME NOT NULL,
    open REAL NOT NULL,
    high REAL NOT NULL,
    low REAL NOT NULL,
    close REAL NOT NULL,
    volume INTEGER,
    UNIQUE(symbol, timestamp)
);
CREATE INDEX idx_symbol_timestamp ON intraday_bars(symbol, timestamp);
```

## 🔧 Configuration

### Alpha Vantage API
Set environment variable:
```bash
export ALPHA_VANTAGE_API_KEY=your_key_here
```

Or in code:
```csharp
Environment.SetEnvironmentVariable("ALPHA_VANTAGE_API_KEY", "your_key");
```

### Data Paths
Configure in `configs/column_hints.yml`:
```yaml
data_paths:
  historical: "./Data/Historical"
  staging: "./Data/Staging"
  archive: "./historical_archive"
```

## 📈 Data Coverage

Current archive includes:
- **SPY**: 2020-01-02 to 2024-02-29 (35,931 bars)
- **Granularity**: 5-minute bars
- **Fields**: Open, High, Low, Close, Volume

## 🧪 Testing

Run integrity tests:
```bash
cd Stroll.History.Integrity.Tests
dotnet test
```

## 📝 API Examples

### MCP Tool Usage
```json
{
  "tool": "get_historical_data",
  "parameters": {
    "symbol": "SPY",
    "startDate": "2024-01-01",
    "endDate": "2024-01-31"
  }
}
```

### Direct SQLite Access
```csharp
var storage = new SqliteHistoricalStorage(archivePath);
var data = await storage.GetIntradayBarsAsync("SPY", start, end);
```

## 🔐 Data Integrity

- Cryptographic hashing for audit trails
- Duplicate detection on insert
- Transaction-based updates
- Automatic backup on migration