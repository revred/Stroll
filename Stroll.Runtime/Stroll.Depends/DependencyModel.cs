namespace Stroll.Depends;

/// <summary>
/// Root configuration model for process dependencies
/// </summary>
public class DependencyConfiguration
{
    public List<ProcessDefinition> Processes { get; set; } = new();
    public DependencyRules DependencyRules { get; set; } = new();
    public LifecycleConfiguration Lifecycle { get; set; } = new();
}

/// <summary>
/// Definition of a process and its dependencies
/// </summary>
public class ProcessDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Dependencies { get; set; } = new();
    public ProcessType Type { get; set; } = ProcessType.Service;
}

/// <summary>
/// Type of process for lifecycle management
/// </summary>
public enum ProcessType
{
    /// <summary>Long-running service process</summary>
    Service,
    
    /// <summary>Short-lived application process</summary>
    Application,
    
    /// <summary>Batch job or script</summary>
    Batch
}

/// <summary>
/// Rules for handling dependency relationships
/// </summary>
public class DependencyRules
{
    public TerminationRules OnTermination { get; set; } = new();
    public StartupRules OnStartup { get; set; } = new();
}

/// <summary>
/// Rules for when a process is terminated
/// </summary>
public class TerminationRules
{
    /// <summary>Whether to kill processes that depend on the terminated process</summary>
    public bool KillDependents { get; set; } = true;
    
    /// <summary>Grace period before force-killing dependents (seconds)</summary>
    public int GracePeriod { get; set; } = 10;
    
    /// <summary>Whether to wait for dependents to shut down gracefully</summary>
    public bool WaitForGracefulShutdown { get; set; } = true;
}

/// <summary>
/// Rules for when a process is starting
/// </summary>
public class StartupRules
{
    /// <summary>Whether to automatically start required dependencies</summary>
    public bool AutoStartDependencies { get; set; } = true;
    
    /// <summary>Maximum time to wait for dependencies to start (seconds)</summary>
    public int DependencyStartupTimeout { get; set; } = 30;
    
    /// <summary>Whether to fail startup if dependencies can't be started</summary>
    public bool FailOnDependencyFailure { get; set; } = true;
}

/// <summary>
/// Process lifecycle configuration
/// </summary>
public class LifecycleConfiguration
{
    public HealthCheckConfiguration HealthCheck { get; set; } = new();
    public RestartPolicyConfiguration RestartPolicy { get; set; } = new();
}

/// <summary>
/// Health check configuration for processes
/// </summary>
public class HealthCheckConfiguration
{
    /// <summary>How often to check process health (seconds)</summary>
    public int Interval { get; set; } = 15;
    
    /// <summary>How many failed checks before considering process unhealthy</summary>
    public int FailureThreshold { get; set; } = 3;
    
    /// <summary>Timeout for individual health checks (seconds)</summary>
    public int Timeout { get; set; } = 5;
}

/// <summary>
/// Restart policy configuration for failed processes
/// </summary>
public class RestartPolicyConfiguration
{
    /// <summary>Whether to automatically restart failed processes</summary>
    public bool AutoRestart { get; set; } = true;
    
    /// <summary>Maximum number of restart attempts</summary>
    public int MaxRestarts { get; set; } = 5;
    
    /// <summary>Time window for counting restarts (seconds)</summary>
    public int RestartWindow { get; set; } = 300;
    
    /// <summary>Base delay for exponential backoff (seconds)</summary>
    public int RestartDelayBase { get; set; } = 2;
    
    /// <summary>Maximum restart delay (seconds)</summary>
    public int RestartDelayMax { get; set; } = 60;
}