using Microsoft.Extensions.Logging;
using Stroll.Process;

namespace Stroll.Depends;

/// <summary>
/// Process manager that understands and enforces process dependencies
/// </summary>
public sealed class DependencyAwareProcessManager : IDisposable
{
    private readonly ProcessManager _processManager;
    private readonly DependencyResolver _dependencyResolver;
    private readonly DependencyConfiguration _config;
    private readonly ILogger<DependencyAwareProcessManager> _logger;
    private readonly Dictionary<string, ProcessHandle> _processHandles = new();

    public DependencyAwareProcessManager(
        DependencyConfiguration config, 
        ProcessManager? processManager = null,
        ILogger<DependencyAwareProcessManager>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _dependencyResolver = new DependencyResolver(config);
        _processManager = processManager ?? new ProcessManager();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DependencyAwareProcessManager>.Instance;
    }

    /// <summary>
    /// Start a process and all its required dependencies
    /// </summary>
    public async Task<ProcessHandle> StartProcessWithDependenciesAsync(string processName)
    {
        _logger.LogInformation("Starting process '{ProcessName}' with dependencies", processName);

        var processesToStart = _dependencyResolver.GetProcessesToStartWith(processName);
        var startedProcesses = new List<string>();

        try
        {
            foreach (var process in processesToStart)
            {
                if (IsProcessRunning(process))
                {
                    _logger.LogDebug("Process '{ProcessName}' is already running", process);
                    continue;
                }

                _logger.LogInformation("Starting dependency process '{ProcessName}'", process);
                
                var definition = _dependencyResolver.GetProcessDefinition(process);
                var processSpec = CreateProcessSpec(definition);
                
                var handle = await _processManager.EnsureProcessAsync(process, processSpec);
                _processHandles[process] = handle;
                startedProcesses.Add(process);
                
                _logger.LogInformation("Successfully started process '{ProcessName}' (PID: {PID})", 
                    process, handle.ProcessId);
            }

            // Return the handle for the main process
            if (!_processHandles.TryGetValue(processName, out var mainHandle))
            {
                throw new InvalidOperationException($"Failed to get handle for main process '{processName}'");
            }

            _logger.LogInformation("Successfully started '{ProcessName}' with {DependencyCount} dependencies", 
                processName, processesToStart.Count - 1);
            
            return mainHandle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process '{ProcessName}' with dependencies", processName);
            
            // Clean up any processes we started before the failure
            await CleanupStartedProcessesAsync(startedProcesses);
            throw;
        }
    }

    /// <summary>
    /// Stop a process and all its dependents
    /// </summary>
    public async Task StopProcessWithDependentsAsync(string processName)
    {
        _logger.LogInformation("Stopping process '{ProcessName}' with dependents", processName);

        var processesToStop = _dependencyResolver.GetProcessesToStopWith(processName);
        var gracePeriod = TimeSpan.FromSeconds(_config.DependencyRules.OnTermination.GracePeriod);

        foreach (var process in processesToStop)
        {
            if (!IsProcessRunning(process))
            {
                _logger.LogDebug("Process '{ProcessName}' is not running", process);
                continue;
            }

            _logger.LogInformation("Stopping dependent process '{ProcessName}'", process);

            if (_config.DependencyRules.OnTermination.WaitForGracefulShutdown)
            {
                // For now, just kill the process (graceful shutdown can be added later)
                _processManager.KillProcess(process);
                _processHandles.Remove(process);
            }
            else
            {
                _processManager.KillProcess(process);
            }

            _logger.LogInformation("Successfully stopped process '{ProcessName}'", process);
        }

        _logger.LogInformation("Successfully stopped '{ProcessName}' with {DependentCount} dependents", 
            processName, processesToStop.Count - 1);
    }

    /// <summary>
    /// Check if a process can be safely started (all dependencies are available)
    /// </summary>
    public bool CanStartProcess(string processName)
    {
        var dependencies = _dependencyResolver.GetAllDependencies(processName);
        
        foreach (var dependency in dependencies)
        {
            if (!IsProcessHealthy(dependency))
            {
                _logger.LogDebug("Process '{ProcessName}' cannot start: dependency '{Dependency}' is not healthy", 
                    processName, dependency);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Check if a process can be safely stopped (no healthy dependents)
    /// </summary>
    public bool CanStopProcess(string processName)
    {
        if (!_config.DependencyRules.OnTermination.KillDependents)
        {
            var dependents = _dependencyResolver.GetAllDependents(processName);
            
            foreach (var dependent in dependents)
            {
                if (IsProcessHealthy(dependent))
                {
                    _logger.LogDebug("Process '{ProcessName}' cannot stop: dependent '{Dependent}' is still running", 
                        processName, dependent);
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Get the startup order for a set of processes considering dependencies
    /// </summary>
    public List<string> GetStartupOrder(IEnumerable<string> processNames)
    {
        return _dependencyResolver.GetStartupOrder(processNames);
    }

    /// <summary>
    /// Get the shutdown order for a set of processes considering dependencies
    /// </summary>
    public List<string> GetShutdownOrder(IEnumerable<string> processNames)
    {
        return _dependencyResolver.GetShutdownOrder(processNames);
    }

    /// <summary>
    /// Get status information for all managed processes
    /// </summary>
    public IReadOnlyDictionary<string, ProcessStatus> GetProcessStatuses()
    {
        return _processManager.GetProcessStatuses();
    }

    /// <summary>
    /// Kill all managed processes in the correct dependency order
    /// </summary>
    public void KillAllProcesses()
    {
        var allProcesses = _config.Processes.Select(p => p.Name);
        var shutdownOrder = GetShutdownOrder(allProcesses);

        foreach (var processName in shutdownOrder)
        {
            if (IsProcessRunning(processName))
            {
                _logger.LogInformation("Killing process '{ProcessName}'", processName);
                _processManager.KillProcess(processName);
                _processHandles.Remove(processName);
            }
        }
    }

    // Private helper methods

    private ProcessSpec CreateProcessSpec(ProcessDefinition definition)
    {
        var basePath = Environment.CurrentDirectory;
        var executablePath = Path.Combine(basePath, definition.Path);
        
        // Check if it's an executable or needs dotnet run
        var isDotnetProject = Directory.GetFiles(Path.GetDirectoryName(executablePath) ?? "", "*.csproj").Any();
        
        if (isDotnetProject)
        {
            return new ProcessSpec
            {
                ExecutablePath = "dotnet",
                Arguments = $"run --project \"{executablePath}.csproj\"",
                WorkingDirectory = Path.GetDirectoryName(executablePath),
                HealthCheckInterval = TimeSpan.FromSeconds(_config.Lifecycle.HealthCheck.Interval)
            };
        }
        else
        {
            return new ProcessSpec
            {
                ExecutablePath = executablePath + ".exe",
                Arguments = "",
                WorkingDirectory = Path.GetDirectoryName(executablePath),
                HealthCheckInterval = TimeSpan.FromSeconds(_config.Lifecycle.HealthCheck.Interval)
            };
        }
    }

    private bool IsProcessRunning(string processName)
    {
        var statuses = _processManager.GetProcessStatuses();
        return statuses.TryGetValue(processName, out var status) && status.IsRunning;
    }

    private bool IsProcessHealthy(string processName)
    {
        var statuses = _processManager.GetProcessStatuses();
        return statuses.TryGetValue(processName, out var status) && status.IsRunning && status.IsResponding;
    }

    private ProcessHandle? GetProcessHandle(string processName)
    {
        var statuses = _processManager.GetProcessStatuses();
        // ProcessHandle is internal to ProcessManager, we can't create it directly
        // This is a limitation - we'll need to track handles differently or modify ProcessManager
        return null;
    }

    private async Task CleanupStartedProcessesAsync(List<string> processNames)
    {
        foreach (var processName in processNames.AsEnumerable().Reverse())
        {
            try
            {
                if (IsProcessRunning(processName))
                {
                    _logger.LogInformation("Cleaning up process '{ProcessName}' due to startup failure", processName);
                    _processManager.KillProcess(processName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup process '{ProcessName}'", processName);
            }
        }
    }

    public void Dispose()
    {
        _processManager.Dispose();
    }
}