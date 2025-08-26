using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Spectre.Console;

namespace Stroll.PrettyTest;

/// <summary>
/// Advanced test runner with elegant output formatting and parallel execution
/// </summary>
public partial class TestRunner
{
    private readonly TestConfiguration _config;
    private readonly string _rootPath;

    public TestRunner(TestConfiguration config, string rootPath)
    {
        _config = config;
        _rootPath = rootPath;
    }

    /// <summary>
    /// Run all configured test suites
    /// </summary>
    public async Task<TestRunSummary> RunAllAsync(string[]? filter = null, CancellationToken cancellationToken = default)
    {
        var summary = new TestRunSummary();
        
        AnsiConsole.MarkupLine("[bold cyan]🧪 Stroll Test Runner[/]");
        AnsiConsole.WriteLine();

        // Filter test suites if specified
        var suitesToRun = FilterTestSuites(_config.TestSuites, filter);
        summary.TotalSuites = suitesToRun.Count;

        if (suitesToRun.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No test suites match the specified filter.[/]");
            return summary;
        }

        // Display what we're about to run
        DisplayTestPlan(suitesToRun);

        // Prepare artifacts directory
        var artifactsDir = Path.Combine(_rootPath, _config.ExecutionSettings.ArtifactsDirectory);
        Directory.CreateDirectory(artifactsDir);

        var stopwatch = Stopwatch.StartNew();

        // Run test suites
        if (_config.ExecutionSettings.ParallelSuites)
        {
            summary.SuiteResults = await RunSuitesInParallelAsync(suitesToRun, artifactsDir, cancellationToken);
        }
        else
        {
            summary.SuiteResults = await RunSuitesSequentiallyAsync(suitesToRun, artifactsDir, cancellationToken);
        }

        stopwatch.Stop();
        summary.TotalExecutionTime = stopwatch.Elapsed;

        // Calculate summary statistics
        CalculateSummaryStats(summary);

        // Display final results
        DisplayFinalResults(summary);

        return summary;
    }

    /// <summary>
    /// Run test suites in parallel
    /// </summary>
    private async Task<List<TestSuiteResult>> RunSuitesInParallelAsync(
        List<TestSuite> suites, 
        string artifactsDir, 
        CancellationToken cancellationToken)
    {
        var semaphore = new SemaphoreSlim(_config.ExecutionSettings.MaxConcurrentSuites, _config.ExecutionSettings.MaxConcurrentSuites);
        var results = new List<TestSuiteResult>();
        
        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new ElapsedTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var tasks = suites.Select(async suite =>
                {
                    var task = ctx.AddTask(suite.Name);
                    task.MaxValue = 100;

                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var result = await RunSingleSuiteAsync(suite, artifactsDir, task, cancellationToken);
                        lock (results)
                        {
                            results.Add(result);
                        }
                        return result;
                    }
                    finally
                    {
                        semaphore.Release();
                        task.Value = 100;
                    }
                });

                await Task.WhenAll(tasks);
            });

        return results.OrderBy(r => r.Name).ToList();
    }

    /// <summary>
    /// Run test suites sequentially
    /// </summary>
    private async Task<List<TestSuiteResult>> RunSuitesSequentiallyAsync(
        List<TestSuite> suites, 
        string artifactsDir, 
        CancellationToken cancellationToken)
    {
        var results = new List<TestSuiteResult>();

        await AnsiConsole.Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new ElapsedTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var overallTask = ctx.AddTask("Overall Progress");
                overallTask.MaxValue = suites.Count;

                for (int i = 0; i < suites.Count; i++)
                {
                    var suite = suites[i];
                    var suiteTask = ctx.AddTask(suite.Name);
                    suiteTask.MaxValue = 100;

                    var result = await RunSingleSuiteAsync(suite, artifactsDir, suiteTask, cancellationToken);
                    results.Add(result);

                    suiteTask.Value = 100;
                    overallTask.Value = i + 1;

                    if (!result.TestsSucceeded && _config.FailureHandling.FailFast)
                    {
                        AnsiConsole.MarkupLine("[red]Stopping due to test failure (fail-fast mode)[/]");
                        break;
                    }
                }
            });

        return results;
    }

    /// <summary>
    /// Run a single test suite
    /// </summary>
    private async Task<TestSuiteResult> RunSingleSuiteAsync(
        TestSuite suite, 
        string artifactsDir, 
        ProgressTask progressTask,
        CancellationToken cancellationToken)
    {
        var result = new TestSuiteResult
        {
            Name = suite.Name,
            ProjectPath = suite.ProjectPath
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var projectPath = Path.Combine(_rootPath, suite.ProjectPath);
            var projectFile = FindProjectFile(projectPath);

            if (projectFile == null)
            {
                result.BuildErrors.Add(new BuildError
                {
                    Message = $"No .csproj file found in {projectPath}",
                    Severity = "error"
                });
                return result;
            }

            progressTask.Description = $"{suite.Name} - Building...";
            progressTask.Value = 10;
            await Task.Delay(100); // Allow UI to update

            // Build the project first
            if (_config.ExecutionSettings.BuildBeforeTest)
            {
                result.BuildSucceeded = await BuildProjectAsync(projectFile, result.BuildErrors, cancellationToken);
                if (!result.BuildSucceeded)
                {
                    progressTask.Value = 100;
                    progressTask.Description = $"{suite.Name} - ❌ Build Failed";
                    return result;
                }
            }
            else
            {
                result.BuildSucceeded = true;
            }

            progressTask.Description = $"{suite.Name} - Testing...";
            progressTask.Value = 50;
            await Task.Delay(100); // Allow UI to update

            // Run the tests
            await RunTestsAsync(projectFile, suite, artifactsDir, result, cancellationToken);

            progressTask.Value = 100;
            progressTask.Description = $"{suite.Name} - {(result.TestsSucceeded ? "✅ Passed" : "❌ Failed")}";
            await Task.Delay(100); // Allow UI to update
        }
        catch (OperationCanceledException)
        {
            result.BuildErrors.Add(new BuildError { Message = "Test execution was cancelled", Severity = "error" });
        }
        catch (Exception ex)
        {
            result.BuildErrors.Add(new BuildError { Message = $"Unexpected error: {ex.Message}", Severity = "error" });
        }
        finally
        {
            stopwatch.Stop();
            result.ExecutionTime = stopwatch.Elapsed;
        }

        return result;
    }

    /// <summary>
    /// Build a project and capture any build errors
    /// </summary>
    private async Task<bool> BuildProjectAsync(string projectFile, List<BuildError> buildErrors, CancellationToken cancellationToken)
    {
        var args = $"build \"{projectFile}\" -c {_config.ExecutionSettings.BuildConfiguration} --verbosity minimal";

        var processStartInfo = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = Path.GetDirectoryName(projectFile),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        var buildOutput = outputBuilder.ToString();
        var errorOutput = errorBuilder.ToString();

        if (process.ExitCode != 0)
        {
            // Parse build errors from output
            ParseBuildErrors(buildOutput + errorOutput, buildErrors);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Run tests for a project and parse results
    /// </summary>
    private async Task RunTestsAsync(
        string projectFile, 
        TestSuite suite, 
        string artifactsDir, 
        TestSuiteResult result,
        CancellationToken cancellationToken)
    {
        var trxFileName = $"{Path.GetFileNameWithoutExtension(projectFile)}-{DateTime.Now:yyyyMMdd-HHmmss}.trx";
        var trxPath = Path.Combine(artifactsDir, trxFileName);
        result.TrxPath = trxPath;

        var logFileName = $"{Path.GetFileNameWithoutExtension(projectFile)}-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var logPath = Path.Combine(artifactsDir, logFileName);
        result.LogPath = logPath;

        var args = BuildTestArguments(projectFile, suite, trxPath);

        var processStartInfo = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = Path.GetDirectoryName(projectFile),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Add environment variables
        foreach (var env in suite.Environment)
        {
            processStartInfo.EnvironmentVariables[env.Key] = env.Value;
        }

        using var process = new Process { StartInfo = processStartInfo };
        
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timeoutTask = Task.Delay(TimeSpan.FromMinutes(suite.TimeoutMinutes), cancellationToken);
        var processTask = process.WaitForExitAsync(cancellationToken);

        var completedTask = await Task.WhenAny(processTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            // Timeout occurred
            try
            {
                process.Kill(true);
            }
            catch { }

            result.BuildErrors.Add(new BuildError
            {
                Message = $"Test execution timed out after {suite.TimeoutMinutes} minutes",
                Severity = "error"
            });
            result.TestsSucceeded = false;
            return;
        }

        // Save detailed logs if configured
        if (_config.OutputSettings.SaveDetailedLogs)
        {
            var allOutput = outputBuilder.ToString() + "\n" + errorBuilder.ToString();
            await File.WriteAllTextAsync(logPath, allOutput, cancellationToken);
        }

        result.TestsSucceeded = process.ExitCode == 0;

        // Parse TRX results if available
        if (File.Exists(trxPath))
        {
            ParseTrxResults(trxPath, result);
        }
        else
        {
            // Fallback: parse from console output
            ParseConsoleTestResults(outputBuilder.ToString(), result);
        }
    }

    /// <summary>
    /// Parse TRX file for detailed test results
    /// </summary>
    private void ParseTrxResults(string trxPath, TestSuiteResult result)
    {
        try
        {
            var doc = XDocument.Load(trxPath);
            var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            var testResults = doc.Descendants(ns + "UnitTestResult").ToList();

            result.TotalTests = testResults.Count;
            result.PassedTests = testResults.Count(r => (string?)r.Attribute("outcome") == "Passed");
            result.FailedTests = testResults.Count(r => (string?)r.Attribute("outcome") == "Failed");
            result.SkippedTests = testResults.Count(r => (string?)r.Attribute("outcome") == "NotExecuted");

            // Extract failure details
            foreach (var failedTest in testResults.Where(r => (string?)r.Attribute("outcome") == "Failed"))
            {
                var testName = (string?)failedTest.Attribute("testName") ?? "Unknown";
                var output = failedTest.Element(ns + "Output");
                var errorInfo = output?.Element(ns + "ErrorInfo");

                var failure = new TestFailure
                {
                    TestName = testName,
                    Message = errorInfo?.Element(ns + "Message")?.Value?.Trim() ?? "",
                    StackTrace = errorInfo?.Element(ns + "StackTrace")?.Value?.Trim() ?? "",
                    StdOut = output?.Element(ns + "StdOut")?.Value?.Trim() ?? "",
                    StdErr = output?.Element(ns + "StdErr")?.Value?.Trim() ?? ""
                };

                result.Failures.Add(failure);
            }
        }
        catch (Exception ex)
        {
            result.BuildErrors.Add(new BuildError
            {
                Message = $"Failed to parse TRX file: {ex.Message}",
                Severity = "warning"
            });
        }
    }

    /// <summary>
    /// Parse test results from console output as fallback
    /// </summary>
    private void ParseConsoleTestResults(string output, TestSuiteResult result)
    {
        // Simple regex patterns to extract test counts from dotnet test output
        var passedMatch = Regex.Match(output, @"Passed!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)");
        var failedMatch = Regex.Match(output, @"Failed!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)");

        if (passedMatch.Success)
        {
            result.FailedTests = int.Parse(passedMatch.Groups[1].Value);
            result.PassedTests = int.Parse(passedMatch.Groups[2].Value);
            result.SkippedTests = int.Parse(passedMatch.Groups[3].Value);
            result.TotalTests = int.Parse(passedMatch.Groups[4].Value);
        }
        else if (failedMatch.Success)
        {
            result.FailedTests = int.Parse(failedMatch.Groups[1].Value);
            result.PassedTests = int.Parse(failedMatch.Groups[2].Value);
            result.SkippedTests = int.Parse(failedMatch.Groups[3].Value);
            result.TotalTests = int.Parse(failedMatch.Groups[4].Value);
        }
    }

    /// <summary>
    /// Parse build errors from compiler output
    /// </summary>
    private void ParseBuildErrors(string output, List<BuildError> buildErrors)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var errorPattern = new Regex(@"^(.+?)\((\d+),(\d+)\):\s*(error|warning)\s+([A-Z0-9]+):\s*(.+)$");

        foreach (var line in lines)
        {
            var match = errorPattern.Match(line.Trim());
            if (match.Success)
            {
                buildErrors.Add(new BuildError
                {
                    File = match.Groups[1].Value,
                    Line = int.Parse(match.Groups[2].Value),
                    Column = int.Parse(match.Groups[3].Value),
                    Severity = match.Groups[4].Value,
                    ErrorCode = match.Groups[5].Value,
                    Message = match.Groups[6].Value
                });
            }
            else if (line.Contains("error") || line.Contains("Error"))
            {
                buildErrors.Add(new BuildError
                {
                    Message = line.Trim(),
                    Severity = "error"
                });
            }
        }
    }

    // Additional helper methods continue in the next part...
}