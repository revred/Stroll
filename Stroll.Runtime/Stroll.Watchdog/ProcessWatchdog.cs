using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stroll.Process;

namespace Stroll.Watchdog;

/// <summary>
/// Background service that monitors process health and automatically restarts failed processes
/// </summary>
public sealed class ProcessWatchdog : BackgroundService
{
    private readonly ProcessManager _processManager;
    private readonly ILogger<ProcessWatchdog> _logger;
    private readonly Dictionary<string, WatchedProcess> _watchedProcesses = new();
    private readonly object _lock = new();

    public ProcessWatchdog(ProcessManager processManager, ILogger<ProcessWatchdog> logger)
    {
        _processManager = processManager;
        _logger = logger;
    }

    /// <summary>
    /// Register a process to be monitored by the watchdog
    /// </summary>
    public void WatchProcess(string processId, ProcessSpec spec, WatchdogConfig? config = null)
    {
        config ??= new WatchdogConfig();
        
        lock (_lock)
        {
            _watchedProcesses[processId] = new WatchedProcess(processId, spec, config);
            _logger.LogInformation("Process {ProcessId} registered for watchdog monitoring", processId);
        }
    }

    /// <summary>
    /// Unregister a process from monitoring
    /// </summary>
    public void UnwatchProcess(string processId)
    {
        lock (_lock)
        {
            if (_watchedProcesses.Remove(processId))
            {
                _logger.LogInformation("Process {ProcessId} unregistered from watchdog monitoring", processId);
            }
        }
    }

    /// <summary>
    /// Ensure a watched process is running (start if not running, restart if unhealthy)
    /// </summary>
    public async Task<ProcessHandle> EnsureProcessRunningAsync(string processId)
    {
        WatchedProcess? watched;
        lock (_lock)
        {
            if (!_watchedProcesses.TryGetValue(processId, out watched))
            {
                throw new InvalidOperationException($"Process {processId} is not registered for watchdog monitoring");
            }
        }

        try
        {
            var handle = await _processManager.EnsureProcessAsync(processId, watched.Spec);
            
            lock (_lock)
            {
                watched.LastSuccessfulStart = DateTime.UtcNow;
                watched.ConsecutiveFailures = 0;
            }
            
            return handle;
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                watched.ConsecutiveFailures++;
                watched.LastFailure = DateTime.UtcNow;
                watched.LastError = ex.Message;
            }
            
            _logger.LogError(ex, "Failed to ensure process {ProcessId} is running (failure #{Failures})", 
                processId, watched.ConsecutiveFailures);
            throw;
        }
    }

    /// <summary>
    /// Get watchdog status for all monitored processes
    /// </summary>
    public WatchdogStatus GetStatus()
    {
        var processStatuses = _processManager.GetProcessStatuses();
        
        lock (_lock)
        {
            var watchedStatuses = _watchedProcesses.ToDictionary(
                kvp => kvp.Key,
                kvp => new WatchedProcessStatus
                {
                    ProcessId = kvp.Key,
                    IsWatched = true,
                    ConsecutiveFailures = kvp.Value.ConsecutiveFailures,
                    LastSuccessfulStart = kvp.Value.LastSuccessfulStart,
                    LastFailure = kvp.Value.LastFailure,
                    LastError = kvp.Value.LastError,
                    ProcessStatus = processStatuses.GetValueOrDefault(kvp.Key)
                }
            );

            return new WatchdogStatus
            {
                WatchedProcesses = watchedStatuses,
                TotalWatched = _watchedProcesses.Count,
                HealthyCount = watchedStatuses.Values.Count(s => s.ProcessStatus?.IsRunning == true && s.ProcessStatus?.IsResponding == true),
                UnhealthyCount = watchedStatuses.Values.Count(s => s.ProcessStatus?.IsRunning != true || s.ProcessStatus?.IsResponding != true)
            };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProcessWatchdog started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorProcessesAsync();
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in watchdog monitoring loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("ProcessWatchdog stopped");
    }

    private async Task MonitorProcessesAsync()
    {
        Dictionary<string, WatchedProcess> processesToCheck;
        
        lock (_lock)
        {
            processesToCheck = new Dictionary<string, WatchedProcess>(_watchedProcesses);
        }

        foreach (var (processId, watched) in processesToCheck)
        {
            try
            {
                await CheckAndRestartIfNeeded(processId, watched);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking process {ProcessId}", processId);
            }
        }
    }

    private async Task CheckAndRestartIfNeeded(string processId, WatchedProcess watched)
    {
        var isHealthy = _processManager.IsProcessHealthy(processId);
        
        if (!isHealthy)
        {
            _logger.LogWarning("Process {ProcessId} is unhealthy, attempting restart", processId);
            
            if (watched.ConsecutiveFailures >= watched.Config.MaxConsecutiveFailures)
            {
                _logger.LogError("Process {ProcessId} has failed {Failures} consecutive times, giving up", 
                    processId, watched.ConsecutiveFailures);
                return;
            }

            try
            {
                await _processManager.EnsureProcessAsync(processId, watched.Spec);
                
                lock (_lock)
                {
                    watched.LastSuccessfulStart = DateTime.UtcNow;
                    watched.ConsecutiveFailures = 0;
                }
                
                _logger.LogInformation("Successfully restarted process {ProcessId}", processId);
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    watched.ConsecutiveFailures++;
                    watched.LastFailure = DateTime.UtcNow;
                    watched.LastError = ex.Message;
                }
                
                _logger.LogError(ex, "Failed to restart process {ProcessId} (failure #{Failures})", 
                    processId, watched.ConsecutiveFailures);
            }
        }
    }
}

/// <summary>
/// Configuration for process watching behavior
/// </summary>
public record WatchdogConfig
{
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxConsecutiveFailures { get; init; } = 5;
    public TimeSpan RestartDelay { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Internal tracking for a watched process
/// </summary>
internal class WatchedProcess
{
    public WatchedProcess(string processId, ProcessSpec spec, WatchdogConfig config)
    {
        ProcessId = processId;
        Spec = spec;
        Config = config;
    }

    public string ProcessId { get; }
    public ProcessSpec Spec { get; }
    public WatchdogConfig Config { get; }
    
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastSuccessfulStart { get; set; }
    public DateTime? LastFailure { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// Status of the watchdog and all monitored processes
/// </summary>
public record WatchdogStatus
{
    public required Dictionary<string, WatchedProcessStatus> WatchedProcesses { get; init; }
    public int TotalWatched { get; init; }
    public int HealthyCount { get; init; }
    public int UnhealthyCount { get; init; }
}

/// <summary>
/// Status of a specific watched process
/// </summary>
public record WatchedProcessStatus
{
    public required string ProcessId { get; init; }
    public bool IsWatched { get; init; }
    public int ConsecutiveFailures { get; init; }
    public DateTime? LastSuccessfulStart { get; init; }
    public DateTime? LastFailure { get; init; }
    public string? LastError { get; init; }
    public ProcessStatus? ProcessStatus { get; init; }
}