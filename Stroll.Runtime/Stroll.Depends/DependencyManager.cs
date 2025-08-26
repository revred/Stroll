using Microsoft.Extensions.Logging;
using Stroll.Process;

namespace Stroll.Depends;

/// <summary>
/// High-level manager for process dependencies
/// </summary>
public sealed class DependencyManager : IDisposable
{
    private readonly DependencyConfiguration _config;
    private readonly DependencyAwareProcessManager _processManager;
    private readonly ILogger<DependencyManager> _logger;

    private DependencyManager(DependencyConfiguration config, ILogger<DependencyManager>? logger = null)
    {
        _config = config;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DependencyManager>.Instance;
        _processManager = new DependencyAwareProcessManager(config, null, null);
    }

    /// <summary>
    /// Create a dependency manager from a YAML configuration file
    /// </summary>
    public static async Task<DependencyManager> FromFileAsync(string configFilePath, ILogger<DependencyManager>? logger = null)
    {
        var parser = new DependencyParser();
        var config = await parser.ParseFromFileAsync(configFilePath);
        return new DependencyManager(config, logger);
    }

    /// <summary>
    /// Create a dependency manager from YAML configuration string
    /// </summary>
    public static DependencyManager FromYaml(string yamlContent, ILogger<DependencyManager>? logger = null)
    {
        var parser = new DependencyParser();
        var config = parser.ParseFromYaml(yamlContent);
        return new DependencyManager(config, logger);
    }

    /// <summary>
    /// Create a dependency manager with the default configuration file location
    /// </summary>
    public static async Task<DependencyManager> CreateDefaultAsync(ILogger<DependencyManager>? logger = null)
    {
        var defaultConfigPath = Path.Combine(
            Environment.CurrentDirectory, 
            "Stroll.Runtime", 
            "Stroll.Depends", 
            "process-dependencies.yml");

        if (!File.Exists(defaultConfigPath))
        {
            throw new FileNotFoundException($"Default dependency configuration not found at: {defaultConfigPath}");
        }

        return await FromFileAsync(defaultConfigPath, logger);
    }

    /// <summary>
    /// Start a process and ensure all its dependencies are running
    /// </summary>
    public async Task<ProcessHandle> StartAsync(string processName)
    {
        _logger.LogInformation("Starting process '{ProcessName}' with dependency management", processName);
        return await _processManager.StartProcessWithDependenciesAsync(processName);
    }

    /// <summary>
    /// Stop a process and all processes that depend on it
    /// </summary>
    public async Task StopAsync(string processName)
    {
        _logger.LogInformation("Stopping process '{ProcessName}' with dependency management", processName);
        await _processManager.StopProcessWithDependentsAsync(processName);
    }

    /// <summary>
    /// Check if a process can be safely started (dependencies are available)
    /// </summary>
    public bool CanStart(string processName)
    {
        return _processManager.CanStartProcess(processName);
    }

    /// <summary>
    /// Check if a process can be safely stopped (no active dependents)
    /// </summary>
    public bool CanStop(string processName)
    {
        return _processManager.CanStopProcess(processName);
    }

    /// <summary>
    /// Get all processes that depend on the specified process
    /// </summary>
    public List<string> GetDependents(string processName)
    {
        var resolver = new DependencyResolver(_config);
        return resolver.GetAllDependents(processName);
    }

    /// <summary>
    /// Get all dependencies of the specified process
    /// </summary>
    public List<string> GetDependencies(string processName)
    {
        var resolver = new DependencyResolver(_config);
        return resolver.GetAllDependencies(processName);
    }

    /// <summary>
    /// Get the recommended startup order for multiple processes
    /// </summary>
    public List<string> GetStartupOrder(params string[] processNames)
    {
        return _processManager.GetStartupOrder(processNames);
    }

    /// <summary>
    /// Get the recommended shutdown order for multiple processes
    /// </summary>
    public List<string> GetShutdownOrder(params string[] processNames)
    {
        return _processManager.GetShutdownOrder(processNames);
    }

    /// <summary>
    /// Get status of all managed processes
    /// </summary>
    public IReadOnlyDictionary<string, ProcessStatus> GetStatuses()
    {
        return _processManager.GetProcessStatuses();
    }

    /// <summary>
    /// Kill all managed processes in dependency order
    /// </summary>
    public void KillAll()
    {
        _logger.LogInformation("Killing all managed processes in dependency order");
        _processManager.KillAllProcesses();
    }

    /// <summary>
    /// Get information about a specific process
    /// </summary>
    public ProcessInfo GetProcessInfo(string processName)
    {
        var resolver = new DependencyResolver(_config);
        var definition = resolver.GetProcessDefinition(processName);
        var statuses = GetStatuses();
        var status = statuses.TryGetValue(processName, out var s) ? s : null;

        return new ProcessInfo
        {
            Name = definition.Name,
            Description = definition.Description,
            Type = definition.Type,
            Path = definition.Path,
            DirectDependencies = definition.Dependencies.ToList(),
            AllDependencies = resolver.GetAllDependencies(processName),
            DirectDependents = _config.Processes
                .Where(p => p.Dependencies.Contains(processName))
                .Select(p => p.Name)
                .ToList(),
            AllDependents = resolver.GetAllDependents(processName),
            IsRunning = status?.IsRunning ?? false,
            IsHealthy = status?.IsResponding ?? false,
            ProcessId = status?.SystemProcessId
        };
    }

    /// <summary>
    /// Get information about all configured processes
    /// </summary>
    public List<ProcessInfo> GetAllProcessInfo()
    {
        return _config.Processes.Select(p => GetProcessInfo(p.Name)).ToList();
    }

    public void Dispose()
    {
        _processManager.Dispose();
    }
}

/// <summary>
/// Information about a process and its dependencies
/// </summary>
public class ProcessInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProcessType Type { get; set; }
    public string Path { get; set; } = string.Empty;
    public List<string> DirectDependencies { get; set; } = new();
    public List<string> AllDependencies { get; set; } = new();
    public List<string> DirectDependents { get; set; } = new();
    public List<string> AllDependents { get; set; } = new();
    public bool IsRunning { get; set; }
    public bool IsHealthy { get; set; }
    public int? ProcessId { get; set; }
}