using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Stroll.Depends;

/// <summary>
/// Parses YAML dependency configuration files
/// </summary>
public sealed class DependencyParser
{
    private readonly ILogger<DependencyParser> _logger;
    private readonly IDeserializer _yamlDeserializer;

    public DependencyParser(ILogger<DependencyParser>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DependencyParser>.Instance;
        
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Parse dependency configuration from YAML file
    /// </summary>
    public async Task<DependencyConfiguration> ParseFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Dependency configuration file not found: {filePath}");
        }

        _logger.LogInformation("Loading dependency configuration from {FilePath}", filePath);

        try
        {
            var yamlContent = await File.ReadAllTextAsync(filePath);
            return ParseFromYaml(yamlContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse dependency configuration from {FilePath}", filePath);
            throw new InvalidOperationException($"Failed to parse dependency configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parse dependency configuration from YAML string
    /// </summary>
    public DependencyConfiguration ParseFromYaml(string yamlContent)
    {
        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new ArgumentException("YAML content cannot be null or empty", nameof(yamlContent));
        }

        try
        {
            var config = _yamlDeserializer.Deserialize<DependencyConfiguration>(yamlContent);
            
            ValidateConfiguration(config);
            LogConfigurationSummary(config);
            
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize YAML configuration");
            throw new InvalidOperationException($"Invalid YAML configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validate the parsed configuration for consistency and correctness
    /// </summary>
    private void ValidateConfiguration(DependencyConfiguration config)
    {
        if (config.Processes == null || !config.Processes.Any())
        {
            throw new InvalidOperationException("Configuration must contain at least one process definition");
        }

        var processNames = config.Processes.Select(p => p.Name).ToList();
        var duplicateNames = processNames.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key);
        
        if (duplicateNames.Any())
        {
            throw new InvalidOperationException($"Duplicate process names found: {string.Join(", ", duplicateNames)}");
        }

        // Validate dependencies exist
        foreach (var process in config.Processes)
        {
            if (string.IsNullOrWhiteSpace(process.Name))
            {
                throw new InvalidOperationException("All processes must have a name");
            }

            if (string.IsNullOrWhiteSpace(process.Path))
            {
                throw new InvalidOperationException($"Process '{process.Name}' must have a path");
            }

            foreach (var dependency in process.Dependencies)
            {
                if (!processNames.Contains(dependency))
                {
                    throw new InvalidOperationException(
                        $"Process '{process.Name}' depends on '{dependency}' which is not defined");
                }
            }
        }

        // Check for circular dependencies
        DetectCircularDependencies(config.Processes);
        
        _logger.LogInformation("Dependency configuration validation passed");
    }

    /// <summary>
    /// Detect circular dependencies using depth-first search
    /// </summary>
    private void DetectCircularDependencies(List<ProcessDefinition> processes)
    {
        var processMap = processes.ToDictionary(p => p.Name, p => p);
        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();

        foreach (var process in processes)
        {
            if (!visited.Contains(process.Name))
            {
                if (HasCircularDependency(process.Name, processMap, visiting, visited))
                {
                    throw new InvalidOperationException($"Circular dependency detected involving process '{process.Name}'");
                }
            }
        }
    }

    /// <summary>
    /// Recursive helper for circular dependency detection
    /// </summary>
    private bool HasCircularDependency(
        string processName, 
        Dictionary<string, ProcessDefinition> processMap,
        HashSet<string> visiting, 
        HashSet<string> visited)
    {
        if (visiting.Contains(processName))
        {
            return true; // Found a cycle
        }

        if (visited.Contains(processName))
        {
            return false; // Already processed
        }

        visiting.Add(processName);

        if (processMap.TryGetValue(processName, out var process))
        {
            foreach (var dependency in process.Dependencies)
            {
                if (HasCircularDependency(dependency, processMap, visiting, visited))
                {
                    return true;
                }
            }
        }

        visiting.Remove(processName);
        visited.Add(processName);

        return false;
    }

    /// <summary>
    /// Log a summary of the parsed configuration
    /// </summary>
    private void LogConfigurationSummary(DependencyConfiguration config)
    {
        _logger.LogInformation("Loaded dependency configuration:");
        _logger.LogInformation("  - {ProcessCount} processes defined", config.Processes.Count);
        
        foreach (var process in config.Processes)
        {
            var depCount = process.Dependencies.Count;
            var depText = depCount == 0 ? "no dependencies" : $"{depCount} dependencies: {string.Join(", ", process.Dependencies)}";
            
            _logger.LogInformation("    • {ProcessName} ({ProcessType}) - {Dependencies}", 
                process.Name, process.Type, depText);
        }
        
        _logger.LogInformation("  - Auto-start dependencies: {AutoStart}", 
            config.DependencyRules.OnStartup.AutoStartDependencies);
        _logger.LogInformation("  - Kill dependents on termination: {KillDependents}", 
            config.DependencyRules.OnTermination.KillDependents);
        _logger.LogInformation("  - Auto-restart failed processes: {AutoRestart}", 
            config.Lifecycle.RestartPolicy.AutoRestart);
    }
}