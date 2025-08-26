# Stroll.History CLI/IPC Contract Specification

## Overview

This document defines the **frozen contract** between Stroll.History and Stroll.Runner to ensure stability and prevent regressions. The contract consists of CLI commands, IPC protocols, data formats, and performance guarantees.

## 🔒 **Contract Freeze Policy**

- **Breaking changes prohibited** without major version bump
- **Backward compatibility required** for all contract elements
- **Performance SLOs must be maintained** or improved
- **Data schema changes require migration path**

## 1. 🖥️ **CLI Contract Specification**

### **1.1 Command Line Interface**

#### **Executable Location**
```bash
# Standard installation path
./Stroll.Historical.exe

# Development path
C:\code\Stroll\Stroll.History\Stroll.Historical\bin\Release\net9.0\Stroll.Historical.exe
```

#### **Exit Codes (FROZEN)**
```
0  = Success
1  = General error
2  = Invalid arguments  
3  = Data not found
4  = Network/connectivity error
5  = Authentication/authorization error
10 = Internal server error
```

### **1.2 Core Commands (FROZEN)**

#### **discover** - Service Discovery
```bash
./Stroll.Historical.exe discover
```

**Required Output Format:**
```json
{
  "schema": "stroll.history.v1",
  "ok": true,
  "data": {
    "service": "stroll.history",
    "version": "string",
    "commands": [...]
  }
}
```

**SLO Requirements:**
- Latency: <2ms P95
- Memory: <50MB working set
- Always succeeds (no network dependencies)

#### **get-bars** - Historical Bar Data
```bash
./Stroll.Historical.exe get-bars --symbol SPY --from 2024-01-01 --to 2024-01-31 --granularity 1d
```

**Parameters (FROZEN):**
- `--symbol` (required): Stock/ETF symbol (string, 1-10 chars)
- `--from` (required): Start date (yyyy-MM-dd format)
- `--to` (required): End date (yyyy-MM-dd format) 
- `--granularity` (optional): 1m|5m|1h|1d (default: 1d)
- `--format` (optional): json|jsonl (default: json)
- `--output` (optional): File path (default: stdout)

**Required Output Schema:**
```json
{
  "schema": "stroll.history.v1",
  "ok": true,
  "data": {
    "symbol": "SPY",
    "granularity": "1d", 
    "from": "2024-01-01",
    "to": "2024-01-31",
    "bars": [
      {
        "t": "2024-01-02T13:30:00.000Z",
        "o": 475.23,
        "h": 477.89,
        "l": 474.15,
        "c": 476.44,
        "v": 45123456,
        "symbol": "SPY",
        "g": "1d"
      }
    ]
  },
  "meta": {
    "count": 21,
    "timestamp": "2024-08-25T16:30:00.000Z"
  }
}
```

**SLO Requirements:**
- Latency: <3ms typical, <10ms P95, <25ms P99
- Single day: <3ms
- Single month: <15ms  
- Single year: <100ms
- Data must be complete and accurate (>99.9% quality score)

#### **get-options** - Options Chain Data  
```bash
./Stroll.Historical.exe get-options --symbol SPY --date 2024-01-19
```

**Parameters (FROZEN):**
- `--symbol` (required): Underlying symbol
- `--date` (required): Expiry or quote date (yyyy-MM-dd)
- `--format` (optional): json|jsonl (default: json)

**Required Output Schema:**
```json
{
  "schema": "stroll.history.v1", 
  "ok": true,
  "data": {
    "symbol": "SPY",
    "expiry": "2024-01-19",
    "chain": [
      {
        "symbol": "SPY",
        "expiry": "2024-01-19",
        "right": "PUT",
        "strike": 470.0,
        "bid": 2.15,
        "ask": 2.25, 
        "mid": 2.20,
        "delta": -0.35,
        "gamma": 0.08
      }
    ]
  },
  "meta": {
    "count": 156,
    "timestamp": "2024-08-25T16:30:00.000Z"
  }
}
```

**SLO Requirements:**
- Latency: <5ms typical, <15ms P95, <40ms P99
- Weekly chains: <10ms
- Monthly chains: <20ms
- 0DTE: <30ms (acceptable for high-volume scenarios)

#### **version** - Service Version
```bash
./Stroll.Historical.exe version
```

**Required Output:**
```json
{
  "schema": "stroll.history.v1",
  "ok": true, 
  "data": {
    "service": "stroll.history",
    "version": "1.2.3"
  }
}
```

**SLO Requirements:**
- Latency: <1ms (must be cached)
- Always succeeds

#### **provider-status** - Data Provider Health
```bash
./Stroll.Historical.exe provider-status
```

**Required Output Schema:**
```json
{
  "schema": "stroll.history.v1",
  "ok": true,
  "data": {
    "providers": [
      {
        "name": "Local Historical Data",
        "priority": 0,
        "available": true,
        "healthy": true, 
        "responseTimeMs": 2,
        "rateLimit": {
          "requestsRemaining": 2147483647,
          "requestsPerMinute": 2147483647
        }
      }
    ]
  }
}
```

### **1.3 Error Handling (FROZEN)**

**Error Response Format:**
```json
{
  "schema": "stroll.history.v1",
  "ok": false,
  "error": {
    "code": "DATA_NOT_FOUND", 
    "message": "No data found for symbol XYZ",
    "hint": "Check symbol spelling or date range"
  }
}
```

**Standard Error Codes:**
- `INVALID_SYMBOL`: Symbol not found or invalid format
- `INVALID_DATE_RANGE`: Date range invalid or too large  
- `DATA_NOT_FOUND`: No data available for request
- `RATE_LIMIT_EXCEEDED`: Too many requests
- `INTERNAL_ERROR`: Service internal error
- `INVALID_ARGUMENTS`: Command line argument error

## 2. 🔗 **IPC Contract Specification**

### **2.1 Named Pipe Protocol (Windows)**

**Pipe Name Convention:**
```
stroll.history.{instanceId}
```

**Protocol:** Binary length-prefixed JSON messages
```
[4 bytes length][JSON message bytes]
```

### **2.2 IPC Request/Response Format**

**Request Format:**
```json
{
  "Command": "get-bars",
  "Parameters": {
    "symbol": "SPY",
    "from": "2024-01-01", 
    "to": "2024-01-31",
    "granularity": "1d"
  }
}
```

**Response Format:**
```json
{
  "Success": true,
  "Data": "{\"schema\":\"stroll.history.v1\",...}",
  "Error": null
}
```

### **2.3 IPC SLO Requirements**
- **Connection establishment**: <5ms
- **Request processing**: Same as CLI SLOs
- **Concurrent connections**: Support 16+ simultaneous clients
- **Memory per connection**: <10MB
- **Connection persistence**: Reuse connections for multiple requests

## 3. 📋 **Data Schema Contract (FROZEN)**

### **3.1 Market Data Bar Schema**
```typescript
interface MarketDataBar {
  t: string;      // ISO 8601 timestamp (UTC)
  o: number;      // Open price (decimal)
  h: number;      // High price (decimal) 
  l: number;      // Low price (decimal)
  c: number;      // Close price (decimal)
  v: number;      // Volume (integer)
  symbol: string; // Symbol identifier
  g: string;      // Granularity (1m|5m|1h|1d)
}
```

**Data Quality Requirements:**
- Low ≤ min(Open, Close) ≤ max(Open, Close) ≤ High
- Volume ≥ 0
- Timestamps in strictly increasing order (no gaps except weekends/holidays)
- All prices > 0
- All timestamps in UTC

### **3.2 Options Chain Schema**
```typescript
interface OptionContract {
  symbol: string;    // Underlying symbol
  expiry: string;    // Expiry date (yyyy-MM-dd)
  right: "PUT"|"CALL"; // Option type
  strike: number;    // Strike price (decimal)
  bid: number;       // Bid price (decimal, ≥0)
  ask: number;       // Ask price (decimal, ≥0)  
  mid?: number;      // Mid price (optional)
  delta?: number;    // Delta (-1 to 1)
  gamma?: number;    // Gamma (≥0)
  theta?: number;    // Theta
  vega?: number;     // Vega
  iv?: number;       // Implied volatility
}
```

**Data Quality Requirements:**
- Bid ≤ Ask (when both > 0)
- Strike > 0
- Delta ∈ [-1, 1] for valid contracts
- Gamma ≥ 0 
- Expiry ≥ today (no expired contracts in live data)

### **3.3 Metadata Schema (FROZEN)**
```typescript
interface ResponseMetadata {
  count: number;           // Number of records returned
  timestamp: string;       // Response generation time (ISO 8601 UTC)
  totalMs?: number;        // Processing time in milliseconds
  firstByteMs?: number;    // Time to first byte
  cache?: "hit"|"miss"|"warm"|"cold";
  source?: "csv"|"sqlite"|"parquet"|"api";
  quality?: {
    completeness: number;  // 0.0 - 1.0
    consistency: number;   // 0.0 - 1.0  
    accuracy: number;      // 0.0 - 1.0
    score: number;         // Overall 0.0 - 1.0
    violations: number;    // Count of data quality violations
  }
}
```

## 4. 🎯 **Performance SLO Contract**

### **4.1 Latency Requirements (FROZEN)**

| Operation | Typical | P95 | P99 | Notes |
|-----------|---------|-----|-----|-------|
| discover | <1ms | <2ms | <5ms | Always cached |
| version | <1ms | <2ms | <3ms | Always cached |
| provider-status | <2ms | <5ms | <10ms | Health checks |
| get-bars (1 day) | <3ms | <8ms | <15ms | Single day |
| get-bars (1 week) | <5ms | <12ms | <25ms | Week range |
| get-bars (1 month) | <10ms | <20ms | <40ms | Month range | 
| get-bars (1 year) | <50ms | <100ms | <200ms | Year range |
| get-options (weekly) | <5ms | <15ms | <30ms | Weekly expiry |
| get-options (monthly) | <10ms | <25ms | <50ms | Monthly expiry |
| get-options (0DTE) | <15ms | <40ms | <80ms | Same-day expiry |

### **4.2 Throughput Requirements**
- **Sequential requests**: 500+ req/sec sustained
- **Concurrent requests**: 1000+ req/sec with 8+ clients
- **Data transfer**: 50+ MB/sec for large payloads
- **Memory usage**: <200MB working set under normal load

### **4.3 Availability Requirements**
- **Uptime**: 99.9% (8.77 hours downtime/year maximum)
- **Error rate**: <0.1% for valid requests
- **Recovery time**: <30 seconds after transient failures

## 5. 🔄 **Process Lifecycle Contract**

### **5.1 Startup Behavior**
- **Cold start time**: <5 seconds to first request
- **Initialization**: Must complete data index building in background
- **Ready signal**: Return success on first `version` command
- **Resource limits**: <500MB memory during startup

### **5.2 Shutdown Behavior**
- **Graceful shutdown**: Handle SIGTERM/Ctrl+C within 10 seconds
- **Request completion**: Finish in-flight requests before shutdown
- **Resource cleanup**: Close all file handles and network connections
- **Exit code**: 0 for clean shutdown, non-zero for errors

### **5.3 Error Recovery**
- **Transient failures**: Retry with exponential backoff
- **Data corruption**: Fail fast with clear error message
- **Network issues**: Degrade gracefully to local data sources
- **Memory pressure**: Implement circuit breakers and backpressure

## 6. 🧪 **Testing Contract Requirements**

### **6.1 Acceptance Criteria**
- All CLI commands must pass functional tests
- All data quality checks must pass
- Performance SLOs must be met under load
- Error handling must be consistent and documented
- Backward compatibility must be maintained

### **6.2 Regression Testing**
- Baseline performance profiles must be maintained
- Data schema changes require migration testing
- API contract changes require cross-version compatibility testing
- Load testing with realistic Stroll.Runner workloads

### **6.3 Integration Testing**
- End-to-end testing with actual Stroll.Runner scenarios
- Multi-process testing with concurrent access
- Resource exhaustion and recovery testing
- Long-running stability testing (24+ hours)

## 7. 📚 **Documentation Requirements**

### **7.1 API Documentation**
- Complete command reference with examples
- Error code reference with resolution steps
- Performance characteristics and tuning guides
- Data schema documentation with validation rules

### **7.2 Integration Guide**
- Step-by-step Stroll.Runner integration examples
- Performance optimization recommendations
- Troubleshooting guide with common issues
- Migration guide for contract changes

## 8. 🔒 **Security & Compliance**

### **8.1 Data Security**
- No sensitive data logging
- Secure temporary file handling
- Process isolation and resource limits
- Input validation and sanitization

### **8.2 Compliance**
- Financial data handling compliance
- Audit logging for data access
- Data retention and cleanup policies
- Version tracking for regulatory requirements

---

**Contract Version**: 1.0.0  
**Last Updated**: 2024-08-25  
**Next Review**: 2024-11-25  
**Approval Required**: Stroll.Runner Team Lead