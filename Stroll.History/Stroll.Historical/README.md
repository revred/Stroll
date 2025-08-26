# Stroll.Historical - Data Acquisition Engine

## Overview

Stroll.Historical provides comprehensive data acquisition capabilities for the Stroll platform, implementing the same multi-source data acquisition strategy as ODTE.Historical. This ensures consistent, reliable access to historical market data from multiple providers.

## Features

### Data Providers
- **Yahoo Finance**: Free, no API key required, reliable historical data
- **Alpha Vantage**: API-based provider with free tier (requires API key)
- **Extensible**: Easy to add new providers (Polygon, Twelve Data, etc.)

### Data Acquisition
- Multi-provider fallback system with priority-based selection
- Rate limiting and health monitoring for all providers
- Automatic retry and error handling
- Compatible data format with existing Stroll.Storage system

### Supported Data Types
- Historical OHLCV bars (Open, High, Low, Close, Volume)
- Multiple time intervals (1m, 5m, 15m, 30m, 1h, 1d)
- VWAP calculation for providers that don't provide it
- Future: Options chains (when API support is available)

## Usage

### CLI Commands

#### Check Provider Status
```bash
stroll.history provider-status
```
Shows health, availability, and rate limits for all configured providers.

#### Acquire Historical Data
```bash
stroll.history acquire-data --symbol SPY --from 2024-01-01 --to 2024-12-31
```

Options:
- `--symbol`: Stock/ETF symbol (required)
- `--from`: Start date in YYYY-MM-DD format (required)
- `--to`: End date in YYYY-MM-DD format (required)
- `--interval`: Data interval - 1d, 1h, 30m, 15m, 5m, 1m (default: 1d)
- `--output`: Output directory for acquired data (default: ./data)

### Environment Variables

Set these environment variables to enable additional providers:

```bash
# Alpha Vantage (free tier: 25 requests/day, 5/minute)
ALPHA_VANTAGE_API_KEY=your_alpha_vantage_key

# Future providers
POLYGON_API_KEY=your_polygon_key
TWELVE_DATA_API_KEY=your_twelve_data_key
```

## Architecture

### Provider Interface
All data providers implement `IDataProvider` with consistent methods:
- `GetHistoricalBarsAsync()`: Fetch OHLCV data
- `GetOptionsChainAsync()`: Fetch options data (future)
- `CheckHealthAsync()`: Provider health check
- `GetRateLimitStatus()`: Current rate limit status

### Data Acquisition Engine
The `DataAcquisitionEngine` coordinates multiple providers:
1. **Provider Selection**: Uses providers in priority order (lower number = higher priority)
2. **Health Checking**: Tests provider health before making requests
3. **Fallback Strategy**: Automatically tries next provider if current fails
4. **Rate Limiting**: Respects provider-specific rate limits
5. **Data Consolidation**: Removes duplicates and ensures consistent format

### Storage Integration
Acquired data is saved in CSV format compatible with Stroll.Storage:
```csv
timestamp,open,high,low,close,volume,vwap
2024-01-01 00:00:00,465.50,467.20,464.10,466.80,125000000,465.90
```

## Data Quality Features

### Provider Priority System
1. **Yahoo Finance** (Priority 1): Free, reliable, no API key
2. **Alpha Vantage** (Priority 2): API-based, limited free tier
3. **Future providers** (Priority 3+): Polygon, Twelve Data, etc.

### Error Handling
- Automatic retry on transient failures
- Graceful degradation when providers are unavailable
- Detailed error logging and reporting
- Rate limit respect to avoid API bans

### Data Validation
- Timestamp validation and sorting
- OHLCV data sanity checks
- Duplicate removal based on timestamp
- VWAP calculation when not provided

## Integration with ODTE.Historical

Stroll.Historical is designed to be a drop-in replacement for ODTE.Historical's data acquisition capabilities:

### Shared Patterns
- Multi-provider architecture with failover
- Rate limiting and health monitoring
- Consistent data formats and error handling
- Environment variable configuration

### Data Compatibility
- Same `MarketDataBar` structure
- Compatible timestamp formats
- Consistent OHLCV field naming
- VWAP calculation methodology

### Provider Strategy
Uses the same proven provider strategy as ODTE:
1. Free providers first (Yahoo Finance)
2. API providers as backup (Alpha Vantage)
3. Premium providers for advanced features
4. Extensible architecture for new sources

## Future Enhancements

### Planned Features
- **Options Data**: When provider support is available
- **Real-time Data**: Live market data integration
- **Advanced Intervals**: Tick data, custom intervals
- **Data Validation**: Enhanced quality checks
- **Caching**: Local data caching for performance

### Additional Providers
- **Polygon.io**: Professional market data
- **Twelve Data**: Comprehensive financial data
- **Stooq**: European market data
- **Custom Sources**: CSV import, database connections

## Development

### Adding New Providers
1. Implement `IDataProvider` interface
2. Add to `DataAcquisitionEngine.InitializeProviders()`
3. Set appropriate priority and rate limits
4. Add environment variable for API key
5. Update CLI discovery output

### Testing
```bash
# Test provider health
stroll.history provider-status

# Test data acquisition
stroll.history acquire-data --symbol SPY --from 2024-01-01 --to 2024-01-05 --interval 1d
```

### Logging
The engine uses Microsoft.Extensions.Logging for comprehensive logging:
- Info: Major operations and results
- Debug: Provider details and data processing
- Warning: Provider issues and fallbacks  
- Error: Critical failures and exceptions