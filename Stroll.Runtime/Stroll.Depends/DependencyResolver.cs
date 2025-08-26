using Microsoft.Extensions.Logging;

namespace Stroll.Depends;

/// <summary>
/// Resolves process dependencies and determines startup/shutdown order
/// </summary>
public sealed class DependencyResolver
{
    private readonly ILogger<DependencyResolver> _logger;
    private readonly DependencyConfiguration _config;
    private readonly Dictionary<string, ProcessDefinition> _processMap;

    public DependencyResolver(DependencyConfiguration config, ILogger<DependencyResolver>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DependencyResolver>.Instance;
        _processMap = config.Processes.ToDictionary(p => p.Name, p => p);
    }

    /// <summary>
    /// Get all processes that depend on the specified process (direct and transitive)
    /// </summary>
    public List<string> GetAllDependents(string processName)
    {
        if (!_processMap.ContainsKey(processName))
        {
            throw new ArgumentException($"Process '{processName}' not found in configuration", nameof(processName));
        }

        var dependents = new HashSet<string>();
        CollectDependents(processName, dependents);
        
        var result = dependents.ToList();
        _logger.LogDebug("Process '{ProcessName}' has {DependentCount} dependents: {Dependents}", 
            processName, result.Count, string.Join(", ", result));
        
        return result;
    }

    /// <summary>
    /// Get all dependencies of the specified process (direct and transitive)
    /// </summary>
    public List<string> GetAllDependencies(string processName)
    {
        if (!_processMap.ContainsKey(processName))
        {
            throw new ArgumentException($"Process '{processName}' not found in configuration", nameof(processName));
        }

        var dependencies = new HashSet<string>();
        CollectDependencies(processName, dependencies);
        
        var result = dependencies.ToList();
        _logger.LogDebug("Process '{ProcessName}' has {DependencyCount} dependencies: {Dependencies}", 
            processName, result.Count, string.Join(", ", result));
        
        return result;
    }

    /// <summary>
    /// Determine the correct startup order for a set of processes
    /// Dependencies are started before their dependents
    /// </summary>
    public List<string> GetStartupOrder(IEnumerable<string> processNames)
    {
        var processes = processNames.ToHashSet();
        var result = new List<string>();
        var visited = new HashSet<string>();
        var inProgress = new HashSet<string>();

        foreach (var processName in processes)
        {
            if (!visited.Contains(processName))
            {
                VisitForStartup(processName, processes, visited, inProgress, result);
            }
        }

        _logger.LogInformation("Startup order for {ProcessCount} processes: {Order}", 
            result.Count, string.Join(" → ", result));
        
        return result;
    }

    /// <summary>
    /// Determine the correct shutdown order for a set of processes
    /// Dependents are stopped before their dependencies
    /// </summary>
    public List<string> GetShutdownOrder(IEnumerable<string> processNames)
    {
        var processes = processNames.ToHashSet();
        var result = new List<string>();
        var visited = new HashSet<string>();
        var inProgress = new HashSet<string>();

        foreach (var processName in processes)
        {
            if (!visited.Contains(processName))
            {
                VisitForShutdown(processName, processes, visited, inProgress, result);
            }
        }

        _logger.LogInformation("Shutdown order for {ProcessCount} processes: {Order}", 
            result.Count, string.Join(" → ", result));
        
        return result;
    }

    /// <summary>
    /// Get processes that should be started when the specified process starts
    /// </summary>
    public List<string> GetProcessesToStartWith(string processName)
    {
        if (!_config.DependencyRules.OnStartup.AutoStartDependencies)
        {
            return new List<string> { processName };
        }

        var dependencies = GetAllDependencies(processName);
        dependencies.Add(processName);
        
        var result = GetStartupOrder(dependencies);
        _logger.LogInformation("Starting '{ProcessName}' requires starting: {ProcessesToStart}", 
            processName, string.Join(", ", result));
        
        return result;
    }

    /// <summary>
    /// Get processes that should be stopped when the specified process stops
    /// </summary>
    public List<string> GetProcessesToStopWith(string processName)
    {
        var processesToStop = new List<string> { processName };
        
        if (_config.DependencyRules.OnTermination.KillDependents)
        {
            var dependents = GetAllDependents(processName);
            processesToStop.AddRange(dependents);
        }

        var result = GetShutdownOrder(processesToStop);
        _logger.LogInformation("Stopping '{ProcessName}' requires stopping: {ProcessesToStop}", 
            processName, string.Join(", ", result));
        
        return result;
    }

    /// <summary>
    /// Get the process definition for the specified process name
    /// </summary>
    public ProcessDefinition GetProcessDefinition(string processName)
    {
        if (!_processMap.TryGetValue(processName, out var definition))
        {
            throw new ArgumentException($"Process '{processName}' not found in configuration", nameof(processName));
        }
        return definition;
    }

    /// <summary>
    /// Check if one process depends on another (directly or transitively)
    /// </summary>
    public bool DependsOn(string processName, string dependencyName)
    {
        var dependencies = GetAllDependencies(processName);
        return dependencies.Contains(dependencyName);
    }

    // Private helper methods

    private void CollectDependents(string processName, HashSet<string> dependents)
    {
        foreach (var process in _config.Processes)
        {
            if (process.Dependencies.Contains(processName) && !dependents.Contains(process.Name))
            {
                dependents.Add(process.Name);
                CollectDependents(process.Name, dependents);
            }
        }
    }

    private void CollectDependencies(string processName, HashSet<string> dependencies)
    {
        if (_processMap.TryGetValue(processName, out var process))
        {
            foreach (var dependency in process.Dependencies)
            {
                if (!dependencies.Contains(dependency))
                {
                    dependencies.Add(dependency);
                    CollectDependencies(dependency, dependencies);
                }
            }
        }
    }

    private void VisitForStartup(string processName, HashSet<string> processes, 
        HashSet<string> visited, HashSet<string> inProgress, List<string> result)
    {
        if (inProgress.Contains(processName))
        {
            throw new InvalidOperationException($"Circular dependency detected at process '{processName}'");
        }

        if (visited.Contains(processName) || !processes.Contains(processName))
        {
            return;
        }

        inProgress.Add(processName);

        if (_processMap.TryGetValue(processName, out var process))
        {
            // Visit dependencies first (they must start before this process)
            foreach (var dependency in process.Dependencies)
            {
                VisitForStartup(dependency, processes, visited, inProgress, result);
            }
        }

        inProgress.Remove(processName);
        visited.Add(processName);
        result.Add(processName);
    }

    private void VisitForShutdown(string processName, HashSet<string> processes,
        HashSet<string> visited, HashSet<string> inProgress, List<string> result)
    {
        if (inProgress.Contains(processName))
        {
            throw new InvalidOperationException($"Circular dependency detected at process '{processName}'");
        }

        if (visited.Contains(processName) || !processes.Contains(processName))
        {
            return;
        }

        inProgress.Add(processName);

        // Visit dependents first (they must stop before this process)
        foreach (var otherProcess in _config.Processes)
        {
            if (otherProcess.Dependencies.Contains(processName) && processes.Contains(otherProcess.Name))
            {
                VisitForShutdown(otherProcess.Name, processes, visited, inProgress, result);
            }
        }

        inProgress.Remove(processName);
        visited.Add(processName);
        result.Add(processName);
    }
}