using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Stroll.Process;

/// <summary>
/// High-reliability process manager for managing external processes with automatic restart and health monitoring
/// </summary>
public sealed class ProcessManager : IDisposable
{
    private readonly ILogger<ProcessManager> _logger;
    private readonly Dictionary<string, ManagedProcess> _processes = new();
    private readonly object _lock = new();

    public ProcessManager(ILogger<ProcessManager>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProcessManager>.Instance;
    }

    /// <summary>
    /// Start or ensure a process is running
    /// </summary>
    public async Task<ProcessHandle> EnsureProcessAsync(string processId, ProcessSpec spec)
    {
        lock (_lock)
        {
            if (_processes.TryGetValue(processId, out var existing))
            {
                if (existing.IsHealthy())
                {
                    _logger.LogDebug("Process {ProcessId} already running and healthy", processId);
                    return new ProcessHandle(existing);
                }
                
                _logger.LogWarning("Process {ProcessId} unhealthy, will restart", processId);
                existing.Kill();
                _processes.Remove(processId);
            }
        }

        _logger.LogInformation("Starting process {ProcessId}: {Command} {Arguments}", processId, spec.ExecutablePath, spec.Arguments);
        
        var process = await StartProcessAsync(spec);
        var managedProcess = new ManagedProcess(processId, process, spec, _logger);
        
        lock (_lock)
        {
            _processes[processId] = managedProcess;
        }

        // Wait for startup validation if specified
        if (spec.StartupValidation != null)
        {
            _logger.LogDebug("Validating startup for {ProcessId}", processId);
            var isValid = await spec.StartupValidation(managedProcess, TimeSpan.FromSeconds(10));
            if (!isValid)
            {
                throw new InvalidOperationException($"Process {processId} failed startup validation");
            }
        }

        return new ProcessHandle(managedProcess);
    }

    /// <summary>
    /// Check if a process is running and healthy
    /// </summary>
    public bool IsProcessHealthy(string processId)
    {
        lock (_lock)
        {
            return _processes.TryGetValue(processId, out var process) && process.IsHealthy();
        }
    }

    /// <summary>
    /// Kill a process
    /// </summary>
    public void KillProcess(string processId)
    {
        lock (_lock)
        {
            if (_processes.TryGetValue(processId, out var process))
            {
                _logger.LogInformation("Killing process {ProcessId}", processId);
                process.Kill();
                _processes.Remove(processId);
            }
        }
    }

    /// <summary>
    /// Get all managed processes
    /// </summary>
    public IReadOnlyDictionary<string, ProcessStatus> GetProcessStatuses()
    {
        lock (_lock)
        {
            return _processes.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.GetStatus()
            );
        }
    }

    private static async Task<System.Diagnostics.Process> StartProcessAsync(ProcessSpec spec)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.ExecutablePath,
            Arguments = spec.Arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        if (spec.WorkingDirectory != null)
        {
            psi.WorkingDirectory = spec.WorkingDirectory;
        }

        foreach (var env in spec.EnvironmentVariables)
        {
            psi.Environment[env.Key] = env.Value;
        }

        var process = System.Diagnostics.Process.Start(psi) 
            ?? throw new InvalidOperationException($"Failed to start process: {spec.ExecutablePath}");

        // Give process a moment to start
        await Task.Delay(100);
        
        return process;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var process in _processes.Values)
            {
                process.Kill();
            }
            _processes.Clear();
        }
    }
}

/// <summary>
/// Specification for a process to be managed
/// </summary>
public record ProcessSpec
{
    public required string ExecutablePath { get; init; }
    public string Arguments { get; init; } = "";
    public string? WorkingDirectory { get; init; }
    public Dictionary<string, string> EnvironmentVariables { get; init; } = new();
    public Func<ManagedProcess, TimeSpan, Task<bool>>? StartupValidation { get; init; }
    public TimeSpan HealthCheckInterval { get; init; } = TimeSpan.FromSeconds(30);
    public int MaxRestartAttempts { get; init; } = 3;
}

/// <summary>
/// A managed process with health monitoring
/// </summary>
public sealed class ManagedProcess : IDisposable
{
    private readonly string _processId;
    private readonly System.Diagnostics.Process _process;
    private readonly ProcessSpec _spec;
    private readonly ILogger _logger;
    private readonly DateTime _startTime;
    private int _restartAttempts;

    public ManagedProcess(string processId, System.Diagnostics.Process process, ProcessSpec spec, ILogger logger)
    {
        _processId = processId;
        _process = process;
        _spec = spec;
        _logger = logger;
        _startTime = DateTime.UtcNow;
    }

    public int ProcessId => _process.Id;
    public DateTime StartTime => _startTime;
    public TimeSpan Uptime => DateTime.UtcNow - _startTime;

    public bool IsHealthy()
    {
        try
        {
            return !_process.HasExited && _process.Responding;
        }
        catch
        {
            return false;
        }
    }

    public ProcessStatus GetStatus()
    {
        try
        {
            return new ProcessStatus
            {
                ProcessId = _processId,
                SystemProcessId = _process.Id,
                IsRunning = !_process.HasExited,
                IsResponding = _process.Responding,
                StartTime = _startTime,
                Uptime = Uptime,
                RestartAttempts = _restartAttempts,
                WorkingSet = _process.WorkingSet64,
                CPUTime = _process.TotalProcessorTime
            };
        }
        catch (Exception ex)
        {
            return new ProcessStatus
            {
                ProcessId = _processId,
                SystemProcessId = -1,
                IsRunning = false,
                IsResponding = false,
                StartTime = _startTime,
                Uptime = Uptime,
                RestartAttempts = _restartAttempts,
                ErrorMessage = ex.Message
            };
        }
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error killing process {ProcessId}", _processId);
        }
    }

    public void Dispose()
    {
        Kill();
        _process.Dispose();
    }
}

/// <summary>
/// Handle to a managed process
/// </summary>
public sealed class ProcessHandle
{
    private readonly ManagedProcess _managedProcess;

    internal ProcessHandle(ManagedProcess managedProcess)
    {
        _managedProcess = managedProcess;
    }

    public int ProcessId => _managedProcess.ProcessId;
    public bool IsHealthy => _managedProcess.IsHealthy();
    public ProcessStatus Status => _managedProcess.GetStatus();
}

/// <summary>
/// Status information for a process
/// </summary>
public record ProcessStatus
{
    public required string ProcessId { get; init; }
    public int SystemProcessId { get; init; }
    public bool IsRunning { get; init; }
    public bool IsResponding { get; init; }
    public DateTime StartTime { get; init; }
    public TimeSpan Uptime { get; init; }
    public int RestartAttempts { get; init; }
    public long WorkingSet { get; init; }
    public TimeSpan CPUTime { get; init; }
    public string? ErrorMessage { get; init; }
}