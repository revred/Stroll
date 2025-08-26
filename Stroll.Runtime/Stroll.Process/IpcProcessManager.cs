using Microsoft.Extensions.Logging;
using System.IO.Pipes;

namespace Stroll.Process;

/// <summary>
/// Specialized process manager for IPC server processes with connection validation
/// </summary>
public sealed class IpcProcessManager : IDisposable
{
    private readonly ProcessManager _processManager;
    private readonly ILogger<IpcProcessManager> _logger;

    public IpcProcessManager(ILogger<IpcProcessManager>? logger = null)
    {
        _processManager = new ProcessManager();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<IpcProcessManager>.Instance;
    }

    /// <summary>
    /// Ensure an IPC server process is running and accepting connections
    /// </summary>
    public async Task<ProcessHandle> EnsureIpcServerAsync(string processId, string executablePath, string pipeName, string[]? arguments = null, string? workingDirectory = null)
    {
        var spec = new ProcessSpec
        {
            ExecutablePath = executablePath,
            Arguments = arguments != null ? string.Join(" ", arguments) : "",
            WorkingDirectory = workingDirectory,
            StartupValidation = async (process, timeout) => await ValidateIpcConnectionAsync(pipeName, timeout),
            HealthCheckInterval = TimeSpan.FromSeconds(15)
        };

        _logger.LogInformation("Ensuring IPC server {ProcessId} is running with pipe {PipeName}", processId, pipeName);
        
        var handle = await _processManager.EnsureProcessAsync(processId, spec);
        
        _logger.LogInformation("IPC server {ProcessId} is running (PID: {PID})", processId, handle.ProcessId);
        return handle;
    }

    /// <summary>
    /// Test if an IPC pipe is available and responsive
    /// </summary>
    public async Task<bool> TestIpcConnectionAsync(string pipeName, TimeSpan timeout = default)
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(5);
        
        return await ValidateIpcConnectionAsync(pipeName, timeout);
    }

    /// <summary>
    /// Kill all processes and clean up
    /// </summary>
    public void KillAllProcesses()
    {
        var statuses = _processManager.GetProcessStatuses();
        foreach (var processId in statuses.Keys)
        {
            _processManager.KillProcess(processId);
        }
    }

    /// <summary>
    /// Get status of all managed processes
    /// </summary>
    public IReadOnlyDictionary<string, ProcessStatus> GetProcessStatuses() => _processManager.GetProcessStatuses();

    private async Task<bool> ValidateIpcConnectionAsync(string pipeName, TimeSpan timeout)
    {
        _logger.LogDebug("Validating IPC connection to pipe {PipeName}", pipeName);
        
        var endTime = DateTime.UtcNow.Add(timeout);
        var attempt = 0;
        
        while (DateTime.UtcNow < endTime)
        {
            attempt++;
            
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                await client.ConnectAsync(1000); // 1 second timeout per attempt
                
                if (client.IsConnected)
                {
                    _logger.LogDebug("Successfully validated IPC connection to {PipeName} on attempt {Attempt}", pipeName, attempt);
                    return true;
                }
            }
            catch (TimeoutException)
            {
                _logger.LogDebug("Connection timeout to {PipeName} on attempt {Attempt}", pipeName, attempt);
            }
            catch (IOException ex) when (ex.Message.Contains("All pipe instances are busy"))
            {
                _logger.LogDebug("Pipe {PipeName} busy on attempt {Attempt}", pipeName, attempt);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Connection failed to {PipeName} on attempt {Attempt}: {Error}", pipeName, attempt, ex.Message);
            }
            
            // Wait before retry, but don't wait longer than remaining timeout
            var remainingTime = endTime - DateTime.UtcNow;
            var waitTime = TimeSpan.FromMilliseconds(Math.Min(500, remainingTime.TotalMilliseconds / 2));
            
            if (waitTime.TotalMilliseconds > 10)
            {
                await Task.Delay(waitTime);
            }
        }
        
        _logger.LogWarning("Failed to validate IPC connection to {PipeName} after {Attempts} attempts in {Timeout}", 
            pipeName, attempt, timeout);
        return false;
    }

    public void Dispose()
    {
        _processManager.Dispose();
    }
}

/// <summary>
/// Builder for creating IPC process specifications with common defaults
/// </summary>
public static class IpcProcessBuilder
{
    /// <summary>
    /// Create a Stroll.History IPC server process specification
    /// </summary>
    public static (string processId, string executablePath, string pipeName, string[] arguments) ForStrollHistoryIpcServer(
        string? dataPath = null, 
        int port = 0)
    {
        var processId = "stroll.history.ipc";
        var pipeName = port > 0 ? $"stroll.history.{port}" : "stroll.history.server";
        
        // Try to find the Stroll.Historical executable
        var (executablePath, isDotnetRun) = FindStrollHistoricalExecutable();
        
        var arguments = new List<string>();
        
        if (isDotnetRun)
        {
            // When using dotnet run, we need to include run and project arguments
            arguments.AddRange(["run", "--project", "Stroll.Historical.csproj", "--"]);
        }
        
        arguments.Add("ipc-server");
        
        if (dataPath != null)
        {
            arguments.AddRange(["--data-path", dataPath]);
        }
        if (port > 0)
        {
            arguments.AddRange(["--port", port.ToString()]);
        }
        
        return (processId, executablePath, pipeName, arguments.ToArray());
    }

    private static (string executablePath, bool isDotnetRun) FindStrollHistoricalExecutable()
    {
        // Prefer dotnet run approach for test environments (more reliable)
        // This ensures we always use the latest code
        return ("dotnet", true);
    }
}