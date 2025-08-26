# Stroll.History Integrity Tests

## Overview

The **Stroll.History.Integrity.Tests** project provides comprehensive validation of the foundational dataset and CLI/IPC contract between **Stroll.History** and **Stroll.Runner**. These tests ensure that the historical data service maintains reliability, performance, and accuracy according to the frozen contract specification.

## 🎯 Purpose & Scope

### **Primary Objectives**
1. **Contract Validation**: Ensure CLI and IPC interfaces remain stable and compliant
2. **Data Quality Assurance**: Validate financial data integrity and completeness  
3. **Performance Regression Detection**: Monitor and prevent performance degradation
4. **Process Lifecycle Testing**: Verify startup, shutdown, and error recovery behaviors

### **Critical Dependencies**
- **Stroll.Runner**: Primary consumer requiring stable CLI/IPC contract
- **Production Trading Systems**: Depend on sub-3ms response times and 99.9% accuracy
- **Historical Data Sources**: SPY, QQQ, oil/energy ETFs spanning 2005-2025

## 🏗️ Test Architecture

### **Test Categories**

#### **1. ContractValidationTests.cs**
- **Purpose**: Validates CLI commands against frozen contract specification
- **Coverage**: Command syntax, response schemas, exit codes, error handling
- **SLOs**: Performance thresholds (P50/P95/P99 latency requirements)

```csharp
[Fact]
public async Task CLI_GetBars_SingleDay_MustMeetPerformanceSLO()
{
    // Validates <15ms P99 SLO for single day bars
    result.ExecutionTimeMs.Should().BeLessOrEqualTo(15, "single day bars must complete in <15ms (P99 SLO)");
}
```

#### **2. PerformanceRegressionTests.cs**
- **Purpose**: Detects performance degradation through baseline comparison
- **Coverage**: Load testing, concurrency stress, memory usage, cold start performance
- **Monitoring**: NBomber integration for realistic workload simulation

#### **3. DataIntegrityTests.cs**
- **Purpose**: Validates financial data quality across all supported symbols
- **Coverage**: OHLC invariants, timestamp consistency, completeness checks
- **Symbols**: SPY, QQQ, XLE, USO, and 11 oil/energy ETFs

```csharp
// CRITICAL FINANCIAL INVARIANTS (FROZEN CONTRACT)
low.Should().BeLessOrEqualTo(Math.Min(open, close), "OHLC invariant violated");
high.Should().BeGreaterOrEqualTo(Math.Max(open, close), "OHLC invariant violated");
```

#### **4. ProcessLifecycleTests.cs**
- **Purpose**: Ensures robust process management and resource handling
- **Coverage**: Cold start performance, graceful shutdown, error recovery, resource limits
- **SLOs**: <5s cold start, <10s graceful shutdown, <500MB memory limit

#### **5. IpcContractTests.cs**
- **Purpose**: Validates Named Pipes IPC protocol compliance
- **Coverage**: Binary framing, concurrent connections, large payload handling
- **Protocol**: Length-prefixed binary messages for high-performance communication

## 📋 Contract Specification (FROZEN)

### **CLI Commands (Immutable Interface)**

#### **Core Commands**
```bash
# Service discovery and health
./Stroll.Historical.exe discover      # <2ms P95
./Stroll.Historical.exe version       # <1ms (cached)
./Stroll.Historical.exe provider-status # <10ms

# Historical data retrieval
./Stroll.Historical.exe get-bars --symbol SPY --from 2024-01-01 --to 2024-01-31 --granularity 1d
./Stroll.Historical.exe get-options --symbol SPY --date 2024-01-19
```

#### **Exit Codes (FROZEN)**
- `0` = Success
- `1` = General error  
- `2` = Invalid arguments
- `3` = Data not found
- `4` = Network/connectivity error
- `5` = Authentication/authorization error
- `10` = Internal server error

#### **Response Schema (FROZEN)**
```json
{
  "schema": "stroll.history.v1",
  "ok": true,
  "data": {
    "symbol": "SPY",
    "bars": [
      {
        "t": "2024-01-02T13:30:00.000Z",
        "o": 475.23, "h": 477.89, "l": 474.15, "c": 476.44,
        "v": 45123456, "symbol": "SPY", "g": "1d"
      }
    ]
  },
  "meta": {
    "count": 21,
    "timestamp": "2024-08-25T16:30:00.000Z"
  }
}
```

### **Performance SLOs (FROZEN)**

| Operation | Typical | P95 | P99 | Notes |
|-----------|---------|-----|-----|-------|
| discover | <1ms | <2ms | <5ms | Always cached |
| get-bars (1 day) | <3ms | <8ms | <15ms | Single day |
| get-bars (1 month) | <10ms | <20ms | <40ms | Month range |
| get-options (weekly) | <5ms | <15ms | <30ms | Weekly expiry |
| **Mixed workload** | **<3ms avg** | **<10ms P95** | **<25ms P99** | **Production target** |

### **Data Quality Requirements (FROZEN)**
- **OHLC Invariants**: Low ≤ min(Open, Close) ≤ max(Open, Close) ≤ High
- **Timestamp Monotonicity**: Strictly increasing order, no gaps except holidays
- **Completeness**: >99% expected trading days present
- **Accuracy**: >99.9% data quality score for all financial metrics

## 🚀 Running the Tests

### **Prerequisites**
1. **Build Stroll.Historical**: Ensure executable exists at expected path
2. **Test Data**: Verify historical data is available for test symbols
3. **.NET 9.0**: Required runtime for test execution

### **Execution Commands**

#### **Run All Tests**
```bash
cd C:\code\Stroll\Stroll.History\Stroll.History.Integrity.Tests
dotnet test --logger "console;verbosity=detailed"
```

#### **Run Specific Test Categories**
```bash
# Contract validation only
dotnet test --filter "FullyQualifiedName~ContractValidationTests"

# Performance regression only  
dotnet test --filter "FullyQualifiedName~PerformanceRegressionTests"

# Data integrity only
dotnet test --filter "FullyQualifiedName~DataIntegrityTests"

# Process lifecycle only
dotnet test --filter "FullyQualifiedName~ProcessLifecycleTests"

# IPC contract only
dotnet test --filter "FullyQualifiedName~IpcContractTests"
```

#### **Performance-Focused Test Run**
```bash
# Run performance-critical tests with detailed output
dotnet test --filter "Category=Performance" --logger "console;verbosity=detailed" --collect:"XPlat Code Coverage"
```

### **Test Configuration**

#### **Environment Variables**
```bash
# Optional: Override executable path
SET STROLL_HISTORICAL_PATH=C:\Custom\Path\Stroll.Historical.exe

# Optional: Configure test timeout
SET STROLL_TEST_TIMEOUT_MS=30000

# Optional: Enable verbose logging
SET STROLL_VERBOSE_LOGGING=true
```

#### **Test Data Requirements**
The tests expect the following data to be available:
- **SPY**: 2005-01-01 to present (primary test symbol)
- **QQQ**: 2005-01-01 to present (secondary test symbol)  
- **XLE, USO**: Oil/energy data as requested
- **Options Data**: SPY options for 2024-01-19 (weekly) and 2024-01-31 (monthly)

## 📊 Test Results & Monitoring

### **Success Criteria**
✅ **All contract validation tests pass** (CLI interface stability)  
✅ **Performance SLOs maintained** (no regression beyond 25% degradation threshold)  
✅ **Data quality score >99%** (financial data integrity)  
✅ **Error rate <1%** (reliability under normal conditions)  
✅ **IPC protocol compliance** (binary framing, concurrent connections)

### **Failure Investigation**

#### **Common Issues & Resolutions**

**❌ Test fails with "Stroll.Historical.exe not found"**
```bash
# Build the executable first
cd C:\code\Stroll\Stroll.History\Stroll.Historical  
dotnet build -c Debug
```

**❌ Performance SLO violations**
```bash
# Check system resource usage
# Review recent code changes for performance impact
# Validate test data size and access patterns
```

**❌ Data quality failures**
```bash
# Verify data source integrity
# Check for corrupted CSV files or database issues  
# Validate data acquisition pipeline
```

**❌ IPC connection failures**
```bash
# Verify Named Pipes support on Windows
# Check firewall and security policies
# Validate process permissions
```

### **Continuous Monitoring Integration**

#### **CI/CD Pipeline Integration**
```yaml
# Example Azure DevOps pipeline step
- task: DotNetCoreCLI@2
  displayName: 'Run Integrity Tests'
  inputs:
    command: 'test'
    projects: '**/Stroll.History.Integrity.Tests.csproj'
    arguments: '--configuration Release --logger trx --collect:"XPlat Code Coverage"'
```

#### **Performance Baseline Tracking**
```csharp
// Tests automatically update performance baselines
// Regression detection triggers alerts for >25% degradation
// Baseline files stored in source control for trending
```

## 🔧 Test Maintenance

### **Adding New Tests**
1. **Follow Naming Convention**: `[Category]_[Scenario]_[Expected]`
2. **Use Descriptive Assertions**: Include context and SLO requirements in failure messages
3. **Resource Cleanup**: Implement proper `IDisposable` pattern for process/connection management
4. **Performance Annotations**: Include timing measurements and SLO validation

### **Updating Contract Specification**
⚠️ **CRITICAL**: Contract changes require major version bump and migration path

1. Update `CONTRACT_SPECIFICATION.md` with new requirements
2. Add backward compatibility tests for previous contract version
3. Update test assertions to validate new contract elements
4. Coordinate with Stroll.Runner team for impact assessment

### **Performance Baseline Updates**
```csharp
// When performance improves, update baseline files
private static PerformanceBaseline LoadPerformanceBaseline()
{
    return new PerformanceBaseline
    {
        DiscoverP50 = 1,    // Improved from 2ms
        DiscoverP95 = 2,    // Maintained
        GetBarsP50 = 2      // Improved from 3ms
    };
}
```

## 🛡️ Security & Compliance

### **Data Security**
- **No Sensitive Data**: Tests use publicly available historical data only
- **Process Isolation**: Each test spawns isolated processes
- **Resource Limits**: Memory and CPU usage monitored and bounded
- **Cleanup Guarantees**: All processes and connections properly disposed

### **Audit & Compliance**
- **Test Execution Logs**: Detailed logging for audit trail
- **Performance Metrics**: SLO compliance tracking for regulatory requirements
- **Data Quality Reports**: Financial data integrity validation
- **Version Tracking**: Contract specification versioning for change management

## 📚 References

- **[CONTRACT_SPECIFICATION.md](CONTRACT_SPECIFICATION.md)**: Complete frozen contract documentation
- **[COMPREHENSIVE_PERFORMANCE_ANALYSIS.md](COMPREHENSIVE_PERFORMANCE_ANALYSIS.md)**: Detailed performance optimization analysis
- **Stroll.Runner Integration Guide**: (See Stroll.Runner documentation)
- **NBomber Load Testing**: [NBomber Documentation](https://nbomber.com/docs/)

---

**Contract Version**: 1.0.0  
**Last Updated**: 2024-08-25  
**Test Suite Version**: 1.0.0  
**Approval Required**: Stroll.Runner Team Lead