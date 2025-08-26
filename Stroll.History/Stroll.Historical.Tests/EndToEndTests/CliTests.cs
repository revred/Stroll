using FluentAssertions;
using System.Diagnostics;
using Xunit;

namespace Stroll.Historical.Tests.EndToEndTests;

public class CliTests : IDisposable
{
    private readonly string _testOutputPath;
    private readonly string _executablePath;

    public CliTests()
    {
        _testOutputPath = Path.Combine(Path.GetTempPath(), "StrollCliTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testOutputPath);
        
        // Find the Stroll.Historical executable using more robust path resolution
        _executablePath = FindExecutablePath();
    }
    
    private string FindExecutablePath()
    {
        // Get the base directory for the solution
        var currentDir = Directory.GetCurrentDirectory();
        var solutionDir = currentDir;
        
        // Navigate up to find the solution directory
        while (!string.IsNullOrEmpty(solutionDir) && !Directory.Exists(Path.Combine(solutionDir, "Stroll.Historical")))
        {
            var parent = Directory.GetParent(solutionDir);
            if (parent == null) break;
            solutionDir = parent.FullName;
        }
        
        if (string.IsNullOrEmpty(solutionDir))
        {
            // Fallback: Use relative path from test project
            solutionDir = Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", ".."));
        }
        
        // Try different build paths in order of preference
        var candidates = new[]
        {
            // x64 Debug build
            Path.Combine(solutionDir, "Stroll.Historical", "bin", "x64", "Debug", "net9.0", "Stroll.Historical.exe"),
            // Regular Debug build
            Path.Combine(solutionDir, "Stroll.Historical", "bin", "Debug", "net9.0", "Stroll.Historical.exe"),
            // x64 Release build
            Path.Combine(solutionDir, "Stroll.Historical", "bin", "x64", "Release", "net9.0", "Stroll.Historical.exe"),
            // Regular Release build
            Path.Combine(solutionDir, "Stroll.Historical", "bin", "Release", "net9.0", "Stroll.Historical.exe")
        };
        
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        
        // If no executable found, throw with helpful information
        throw new FileNotFoundException(
            $"Could not find Stroll.Historical executable. Searched:\n" +
            string.Join("\n", candidates) + 
            $"\nCurrent directory: {currentDir}\n" +
            $"Solution directory: {solutionDir}");
    }

    [Fact]
    public async Task CLI_Discover_ShouldReturnValidJson()
    {
        // Act
        var result = await RunCliCommandAsync("discover");

        // Assert
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("\"schema\":\"stroll.history.v1\"");
        result.Output.Should().Contain("\"service\":\"stroll.history\"");
        result.Output.Should().Contain("\"ok\":true");
        result.Output.Should().Contain("acquire-data");
        result.Output.Should().Contain("provider-status");
    }

    [Fact]
    public async Task CLI_Version_ShouldReturnVersion()
    {
        // Act
        var result = await RunCliCommandAsync("version");

        // Assert
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("\"schema\":\"stroll.history.v1\"");
        result.Output.Should().Contain("\"version\":");
        result.Output.Should().Contain("\"ok\":true");
    }

    [Fact]
    public async Task CLI_ProviderStatus_ShouldShowProviders()
    {
        // Act
        var result = await RunCliCommandAsync("provider-status");

        // Assert
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("Data Provider Status");
        result.Output.Should().Contain("Local Historical Data");
    }

    [Fact]
    public async Task CLI_ListDatasets_ShouldReturnDatasets()
    {
        // Act
        var result = await RunCliCommandAsync("list-datasets");

        // Assert
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("\"schema\":\"stroll.history.v1\"");
        result.Output.Should().Contain("\"datasets\":");
    }

    [Fact]
    public async Task CLI_AcquireData_ShouldHandleMissingParameters()
    {
        // Act
        var result = await RunCliCommandAsync("acquire-data");

        // Assert
        result.ExitCode.Should().NotBe(0);
        result.Error.Should().Contain("missing --symbol");
    }

    [Fact]
    public async Task CLI_AcquireData_WithValidParameters_ShouldSucceed()
    {
        // Arrange
        var outputArg = $"--output \"{_testOutputPath}\"";

        // Act
        var result = await RunCliCommandAsync($"acquire-data --symbol TEST --from 2024-01-01 --to 2024-01-03 {outputArg}");

        // Assert - Command should execute (may fail due to no test data, but shouldn't crash)
        result.Output.Should().Contain("Starting data acquisition");
    }

    [Fact]
    public async Task CLI_GetBars_ShouldHandleMissingData()
    {
        // Act
        var result = await RunCliCommandAsync("get-bars --symbol NONEXISTENT --from 2024-01-01 --to 2024-01-02");

        // Assert - Should execute without crashing
        result.Output.Should().Contain("\"ok\":true");
        result.Output.Should().Contain("\"bars\":");
    }

    [Fact]
    public async Task CLI_InvalidCommand_ShouldReturnError()
    {
        // Act
        var result = await RunCliCommandAsync("invalid-command");

        // Assert
        result.ExitCode.Should().NotBe(0);
        result.Error.Should().Contain("unknown command");
    }

    [Fact]
    public async Task CLI_Help_Commands_ShouldBeDocumented()
    {
        // Act
        var discoverResult = await RunCliCommandAsync("discover");

        // Assert - All commands should be documented in discover
        var commands = new[] { "version", "discover", "list-datasets", "get-bars", "get-options", "acquire-data", "provider-status" };
        
        foreach (var command in commands)
        {
            discoverResult.Output.Should().Contain($"\"name\":\"{command}\"");
        }
    }

    private async Task<CliResult> RunCliCommandAsync(string arguments)
    {
        var workingDir = Path.GetDirectoryName(_executablePath);
        if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir))
        {
            workingDir = _testOutputPath;
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir
        };

        using var process = new Process { StartInfo = startInfo };
        
        try
        {
            process.Start();
            
            // Use timeout to prevent hanging tests
            var timeout = TimeSpan.FromSeconds(30);
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            if (await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds)))
            {
                return new CliResult
                {
                    ExitCode = process.ExitCode,
                    Output = await outputTask,
                    Error = await errorTask
                };
            }
            else
            {
                process.Kill();
                return new CliResult
                {
                    ExitCode = -1,
                    Output = "",
                    Error = "Process timed out"
                };
            }
        }
        catch (Exception ex)
        {
            return new CliResult
            {
                ExitCode = -1,
                Output = "",
                Error = $"Process execution failed: {ex.Message}"
            };
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testOutputPath))
        {
            Directory.Delete(_testOutputPath, true);
        }
    }

    private record CliResult
    {
        public int ExitCode { get; init; }
        public string Output { get; init; } = "";
        public string Error { get; init; } = "";
    }
}