# Stroll.Depends - Process Dependency Management Integration Example

## Overview

This example shows how `Stroll.Depends` manages process dependencies as requested by the user:

> "Stroll.Backtest (when finished) will be dependent on both Stroll.Dataset inside Stroll.History and Stroll.Signal inside Stroll.Strategy"

## Configuration Example

The dependency relationships are defined in `process-dependencies.yml`:

```yaml
processes:
  # Core data services
  - name: "Stroll.Historical"
    path: "Stroll.History/Stroll.Historical"
    description: "Historical data provider service"
    dependencies: []
    type: "service"
    
  - name: "Stroll.Dataset" 
    path: "Stroll.History/Stroll.Dataset"
    description: "Dataset management and packaging"
    dependencies: 
      - "Stroll.Historical"
    type: "service"

  # Strategy components
  - name: "Stroll.Signal"
    path: "Stroll.Strategy/Stroll.Signal"  
    description: "Signal generation and processing"
    dependencies:
      - "Stroll.Dataset"
    type: "service"

  # Backtesting - depends on both data and strategy components  
  - name: "Stroll.Backtest"
    path: "Stroll.Runner/Stroll.Backtest"
    description: "Backtesting engine"
    dependencies:
      - "Stroll.Dataset"    # Needs historical data
      - "Stroll.Signal"     # Needs signal generation
    type: "application"
```

## Usage Example

```csharp
// Load dependency configuration
var dependencyManager = await DependencyManager.CreateDefaultAsync();

// Start Stroll.Backtest - this will automatically:
// 1. Start Stroll.Historical (no dependencies)
// 2. Start Stroll.Dataset (depends on Historical)
// 3. Start Stroll.Signal (depends on Dataset)  
// 4. Finally start Stroll.Backtest (depends on Dataset + Signal)
var backtestHandle = await dependencyManager.StartAsync("Stroll.Backtest");

// When we stop the backtest, it will:
// 1. Stop Stroll.Backtest first
// 2. Check if Signal/Dataset have other dependents
// 3. Stop Signal if no other dependents
// 4. Stop Dataset if no other dependents  
// 5. Stop Historical if no other dependents
await dependencyManager.StopAsync("Stroll.Backtest");
```

## Key Benefits

1. **Intelligent Startup Order**: Dependencies are started in the correct order automatically
2. **Dependency Validation**: Won't start a process unless its dependencies are healthy
3. **Clean Shutdown**: When killing a headless process, all its dependents are also terminated
4. **Process Health Monitoring**: Unhealthy dependencies trigger restarts
5. **Configuration-Driven**: Easy to modify dependencies without code changes

## Runtime Behavior

When `Stroll.Backtest` is started:
```
🚀 Starting Stroll.Backtest with dependencies...
   ✅ Starting dependency: Stroll.Historical (no deps)
   ✅ Starting dependency: Stroll.Dataset (depends on Historical)
   ✅ Starting dependency: Stroll.Signal (depends on Dataset)
   ✅ Starting main process: Stroll.Backtest
```

When `Stroll.Historical` is killed:
```
💀 Killing Stroll.Historical and dependents...
   🔄 Graceful shutdown: Stroll.Backtest (depends on Historical via Dataset)
   🔄 Graceful shutdown: Stroll.Signal (depends on Historical via Dataset)
   🔄 Graceful shutdown: Stroll.Dataset (depends on Historical)
   💀 Force kill: Stroll.Historical
```

## Integration with Existing Process Management

The `DependencyAwareProcessManager` builds on top of the existing `ProcessManager` and `IpcProcessManager`, adding:
- Dependency resolution and validation
- Intelligent startup/shutdown ordering
- YAML-based configuration
- Health monitoring with dependency awareness

This provides the "clear indication to the runtime as to which process to keep and which to kill based on killing the exe that maybe using a headless process" as requested.