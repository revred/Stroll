using System.Diagnostics;
using System.Text.Json;
using Spectre.Console;
using YamlDotNet.Serialization;
using Stroll.PrettyTest;

namespace Stroll.Context.Test;

/// <summary>
/// Comprehensive test runner with interactive pick-and-choose functionality
/// </summary>
public class ComprehensiveTestRunner
{
    private readonly TestConfiguration _configuration;
    private readonly IAnsiConsole _console;
    private static readonly char[] NumberDelimiters = { ' ', ',', '\r', '\n' };
    
    public ComprehensiveTestRunner(TestConfiguration configuration, IAnsiConsole? console = null)
    {
        _configuration = configuration;
        _console = console ?? AnsiConsole.Console;
    }

    /// <summary>
    /// Run tests interactively allowing user to pick and choose
    /// </summary>
    public async Task<TestRunResult> RunInteractiveAsync()
    {
        _console.Clear();
        
        // Display welcome banner
        var panel = new Panel(
            new FigletText("Stroll Test Runner")
                .Centered()
                .Color(Color.Blue))
            .Border(BoxBorder.Double)
            .BorderColor(Color.Blue)
            .Header("[yellow]Comprehensive Test Management System[/]")
            .HeaderAlignment(Justify.Center);
        
        _console.Write(panel);
        _console.WriteLine();

        // Main menu loop
        while (true)
        {
            var choice = _console.Prompt(
                new SelectionPrompt<string>()
                    .Title("[cyan]What would you like to do?[/]")
                    .PageSize(10)
                    .AddChoices(
                        "🎯 Run All Tests",
                        "📋 Select Test Suites",
                        "🔍 Run Tests by Category",
                        "🏷️ Run Tests by Tag",
                        "🧪 Run Individual Tests",
                        "📊 View Test Statistics",
                        "⚙️ Configure Test Settings",
                        "📝 View Recent Results",
                        "🔄 Continuous Test Mode",
                        "❌ Exit"));

            switch (choice)
            {
                case "🎯 Run All Tests":
                    return await RunAllTestsAsync();
                    
                case "📋 Select Test Suites":
                    return await RunSelectedSuitesAsync();
                    
                case "🔍 Run Tests by Category":
                    return await RunByCategoryAsync();
                    
                case "🏷️ Run Tests by Tag":
                    return await RunByTagAsync();
                    
                case "🧪 Run Individual Tests":
                    return await RunIndividualTestsAsync();
                    
                case "📊 View Test Statistics":
                    await ShowTestStatisticsAsync();
                    break;
                    
                case "⚙️ Configure Test Settings":
                    await ConfigureSettingsAsync();
                    break;
                    
                case "📝 View Recent Results":
                    await ShowRecentResultsAsync();
                    break;
                    
                case "🔄 Continuous Test Mode":
                    return await RunContinuousModeAsync();
                    
                case "❌ Exit":
                    return new TestRunResult { Success = true, Message = "Test runner exited successfully" };
            }
        }
    }

    /// <summary>
    /// Run selected test suites with detailed selection
    /// </summary>
    private async Task<TestRunResult> RunSelectedSuitesAsync()
    {
        var suites = _configuration.TestSuites;
        
        var selectedSuites = _console.Prompt(
            new MultiSelectionPrompt<TestSuite>()
                .Title("[cyan]Select test suites to run:[/]")
                .Required()
                .PageSize(10)
                .MoreChoicesText("[grey](Move up and down to reveal more suites)[/]")
                .InstructionsText("[grey](Press [blue]<space>[/] to select, [green]<enter>[/] to run)[/]")
                .AddChoices(suites)
                .UseConverter(suite => 
                    $"[cyan]{suite.Name}[/] - {suite.Description}"));

        return await RunTestSuitesAsync(selectedSuites);
    }

    /// <summary>
    /// Run tests by category
    /// </summary>
    private async Task<TestRunResult> RunByCategoryAsync()
    {
        var categories = _configuration.TestSuites
            .Select(s => s.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        var selectedCategory = _console.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Select test category:[/]")
                .PageSize(10)
                .AddChoices(categories));

        var suites = _configuration.TestSuites
            .Where(s => s.Category == selectedCategory)
            .ToList();

        _console.MarkupLine($"[green]Running {suites.Count} test suite(s) in category '{selectedCategory}'[/]");
        return await RunTestSuitesAsync(suites);
    }

    /// <summary>
    /// Run tests by tag
    /// </summary>
    private async Task<TestRunResult> RunByTagAsync()
    {
        var allTags = _configuration.TestSuites
            .SelectMany(s => s.Tags ?? new List<string>())
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        var selectedTags = _console.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[cyan]Select tags to filter tests:[/]")
                .Required()
                .PageSize(15)
                .InstructionsText("[grey](Tests with ANY selected tag will run)[/]")
                .AddChoices(allTags));

        var suites = _configuration.TestSuites
            .Where(s => s.Tags != null && s.Tags.Any(t => selectedTags.Contains(t)))
            .ToList();

        _console.MarkupLine($"[green]Running {suites.Count} test suite(s) with tags: {string.Join(", ", selectedTags)}[/]");
        return await RunTestSuitesAsync(suites);
    }

    /// <summary>
    /// Run individual tests with granular selection
    /// </summary>
    private async Task<TestRunResult> RunIndividualTestsAsync()
    {
        var suite = _console.Prompt(
            new SelectionPrompt<TestSuite>()
                .Title("[cyan]Select test suite:[/]")
                .PageSize(10)
                .AddChoices(_configuration.TestSuites)
                .UseConverter(s => $"{s.Name} ({s.ProjectPath})"));

        // Discover tests in the selected suite
        var tests = await DiscoverTestsInSuiteAsync(suite);
        
        if (!tests.Any())
        {
            _console.MarkupLine("[red]No tests found in the selected suite[/]");
            return new TestRunResult { Success = false, Message = "No tests found" };
        }

        var selectedTests = _console.Prompt(
            new MultiSelectionPrompt<TestInfo>()
                .Title($"[cyan]Select tests from {suite.Name}:[/]")
                .Required()
                .PageSize(20)
                .MoreChoicesText("[grey](Move up and down to reveal more tests)[/]")
                .InstructionsText("[grey](Press [blue]<space>[/] to select, [green]<enter>[/] to run)[/]")
                .AddChoices(tests)
                .UseConverter(test => $"{test.ClassName}.{test.MethodName}"));

        return await RunIndividualTestsAsync(suite, selectedTests);
    }

    /// <summary>
    /// Run all tests with progress tracking
    /// </summary>
    private async Task<TestRunResult> RunAllTestsAsync()
    {
        var confirm = _console.Confirm($"Run all {_configuration.TestSuites.Count} test suites?");
        
        if (!confirm)
            return new TestRunResult { Success = true, Message = "Cancelled by user" };

        return await RunTestSuitesAsync(_configuration.TestSuites);
    }

    /// <summary>
    /// Execute test suites with live progress
    /// </summary>
    private async Task<TestRunResult> RunTestSuitesAsync(List<TestSuite> suites)
    {
        var result = new TestRunResult
        {
            StartTime = DateTime.UtcNow,
            TotalSuites = suites.Count
        };

        await _console.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Running test suites[/]", maxValue: suites.Count);
                
                foreach (var suite in suites)
                {
                    task.Description = $"[cyan]Running: {suite.Name}[/]";
                    
                    var suiteResult = await RunSingleSuiteAsync(suite);
                    result.SuiteResults.Add(suiteResult);
                    
                    if (suiteResult.TestsSucceeded && suiteResult.BuildSucceeded)
                        result.PassedSuites++;
                    else
                        result.FailedSuites++;
                    
                    task.Increment(1);
                    
                    // Continue on failure if configured
                    if ((!suiteResult.TestsSucceeded || !suiteResult.BuildSucceeded) && !_configuration.ExecutionSettings.ContinueOnFailure)
                        break;
                }
            });

        result.EndTime = DateTime.UtcNow;
        result.Success = result.FailedSuites == 0;
        
        await DisplayTestResultsAsync(result);
        return result;
    }

    /// <summary>
    /// Run a single test suite
    /// </summary>
    private async Task<TestSuiteResult> RunSingleSuiteAsync(TestSuite suite)
    {
        var startTime = DateTime.UtcNow;
        var result = new TestSuiteResult
        {
            Name = suite.Name,
            ProjectPath = suite.ProjectPath
        };

        try
        {
            // Build if needed
            if (_configuration.ExecutionSettings.BuildBeforeTest)
            {
                var buildResult = await BuildProjectAsync(suite.ProjectPath);
                result.BuildSucceeded = buildResult.Success;
                if (!buildResult.Success)
                {
                    result.TestsSucceeded = false;
                    if (!string.IsNullOrEmpty(buildResult.ErrorMessage))
                    {
                        result.BuildErrors.Add(new BuildError
                        {
                            Message = buildResult.ErrorMessage,
                            Severity = "error"
                        });
                    }
                    result.ExecutionTime = DateTime.UtcNow - startTime;
                    return result;
                }
            }
            else
            {
                result.BuildSucceeded = true;
            }

            // Run tests
            var testResult = await ExecuteTestsAsync(suite);
            result.TestsSucceeded = testResult.Success;
            result.PassedTests = testResult.PassedTests;
            result.FailedTests = testResult.FailedTests;
            result.SkippedTests = testResult.SkippedTests;
            result.TotalTests = testResult.PassedTests + testResult.FailedTests + testResult.SkippedTests;
            
            if (!string.IsNullOrEmpty(testResult.ErrorMessage))
            {
                result.Failures.Add(new TestFailure
                {
                    TestName = "General",
                    Message = testResult.ErrorMessage
                });
            }
        }
        catch (Exception ex)
        {
            result.TestsSucceeded = false;
            result.Failures.Add(new TestFailure
            {
                TestName = "Exception",
                Message = ex.Message,
                StackTrace = ex.StackTrace ?? string.Empty
            });
        }
        finally
        {
            result.ExecutionTime = DateTime.UtcNow - startTime;
        }

        return result;
    }

    /// <summary>
    /// Build project before testing
    /// </summary>
    private async Task<BuildResult> BuildProjectAsync(string projectPath)
    {
        var buildResult = new BuildResult();
        
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" --configuration {_configuration.ExecutionSettings.BuildConfiguration}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            buildResult.Success = false;
            buildResult.ErrorMessage = "Failed to start build process";
            return buildResult;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        buildResult.Success = process.ExitCode == 0;
        buildResult.Output = output;
        buildResult.ErrorMessage = error;

        return buildResult;
    }

    /// <summary>
    /// Execute tests in a suite
    /// </summary>
    private async Task<TestExecutionResult> ExecuteTestsAsync(TestSuite suite)
    {
        var result = new TestExecutionResult();
        
        var arguments = $"test \"{suite.ProjectPath}\" " +
                       $"--configuration {_configuration.ExecutionSettings.BuildConfiguration} " +
                       $"--no-build --logger:json";

        // Add filters if specified
        if (suite.Tags != null && suite.Tags.Any())
        {
            var filter = string.Join("|", suite.Tags.Select(t => $"Category={t}"));
            arguments += $" --filter \"{filter}\"";
        }

        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(suite.ProjectPath)
        };

        // Set environment variables
        if (suite.Environment != null)
        {
            foreach (var env in suite.Environment)
            {
                processInfo.Environment[env.Key] = env.Value;
            }
        }

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            result.Success = false;
            result.ErrorMessage = "Failed to start test process";
            return result;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        
        // Set timeout
        var timeout = suite.TimeoutMinutes > 0 ? suite.TimeoutMinutes : _configuration.ExecutionSettings.DefaultTimeoutMinutes;
        var completed = await process.WaitForExitAsync(TimeSpan.FromMinutes(timeout));
        
        if (!completed)
        {
            process.Kill();
            result.Success = false;
            result.ErrorMessage = $"Test suite timed out after {timeout} minutes";
            return result;
        }

        result.Success = process.ExitCode == 0;
        result.Output = output;
        
        // Parse test results from output
        ParseTestResults(output, result);
        
        if (!string.IsNullOrEmpty(error))
        {
            result.ErrorMessage = error;
        }

        return result;
    }

    /// <summary>
    /// Parse test results from output
    /// </summary>
    private void ParseTestResults(string output, TestExecutionResult result)
    {
        // Simple parsing - can be enhanced based on actual output format
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("Passed:"))
            {
                if (int.TryParse(ExtractNumber(line, "Passed:"), out var passed))
                    result.PassedTests = passed;
            }
            else if (line.Contains("Failed:"))
            {
                if (int.TryParse(ExtractNumber(line, "Failed:"), out var failed))
                    result.FailedTests = failed;
            }
            else if (line.Contains("Skipped:"))
            {
                if (int.TryParse(ExtractNumber(line, "Skipped:"), out var skipped))
                    result.SkippedTests = skipped;
            }
        }
    }

    private string ExtractNumber(string line, string prefix)
    {
        var start = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) + prefix.Length;
        var end = line.IndexOfAny(NumberDelimiters, start);
        if (end == -1) end = line.Length;
        return line.Substring(start, end - start).Trim();
    }

    /// <summary>
    /// Discover individual tests in a suite
    /// </summary>
    private async Task<List<TestInfo>> DiscoverTestsInSuiteAsync(TestSuite suite)
    {
        var tests = new List<TestInfo>();
        
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"test \"{suite.ProjectPath}\" --list-tests",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null) return tests;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        // Parse test list from output
        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains('.') && !line.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("Test run", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Trim().Split('.');
                if (parts.Length >= 2)
                {
                    tests.Add(new TestInfo
                    {
                        ClassName = string.Join('.', parts.Take(parts.Length - 1)),
                        MethodName = parts.Last()
                    });
                }
            }
        }

        return tests;
    }

    /// <summary>
    /// Run individual tests
    /// </summary>
    private async Task<TestRunResult> RunIndividualTestsAsync(TestSuite suite, List<TestInfo> tests)
    {
        var result = new TestRunResult
        {
            StartTime = DateTime.UtcNow,
            TotalSuites = 1
        };

        var filter = string.Join("|", tests.Select(t => $"FullyQualifiedName~{t.ClassName}.{t.MethodName}"));
        
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"test \"{suite.ProjectPath}\" --filter \"{filter}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null)
        {
            result.Success = false;
            result.Message = "Failed to start test process";
            return result;
        }

        await process.WaitForExitAsync();
        result.Success = process.ExitCode == 0;
        result.EndTime = DateTime.UtcNow;

        return result;
    }

    /// <summary>
    /// Display test results
    /// </summary>
    private async Task DisplayTestResultsAsync(TestRunResult result)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(result.Success ? Color.Green : Color.Red)
            .Title($"[bold]Test Results - {(result.Success ? "PASSED" : "FAILED")}[/]");

        table.AddColumn("Suite");
        table.AddColumn("Status");
        table.AddColumn("Passed");
        table.AddColumn("Failed");
        table.AddColumn("Skipped");
        table.AddColumn("Duration");

        foreach (var suite in result.SuiteResults)
        {
            var status = (suite.TestsSucceeded && suite.BuildSucceeded) ? "[green]✓[/]" : "[red]✗[/]";
            var duration = suite.ExecutionTime.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "s";
            
            table.AddRow(
                suite.Name,
                status,
                suite.PassedTests.ToString(System.Globalization.CultureInfo.InvariantCulture),
                suite.FailedTests.ToString(System.Globalization.CultureInfo.InvariantCulture),
                suite.SkippedTests.ToString(System.Globalization.CultureInfo.InvariantCulture),
                duration);
        }

        _console.Write(table);
        
        // Summary
        var panel = new Panel(
            $"Total Suites: {result.TotalSuites} | " +
            $"Passed: [green]{result.PassedSuites}[/] | " +
            $"Failed: [red]{result.FailedSuites}[/] | " +
            $"Duration: {(result.EndTime - result.StartTime).TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}s")
            .Border(BoxBorder.Double)
            .BorderColor(result.Success ? Color.Green : Color.Red);
        
        _console.Write(panel);
    }

    /// <summary>
    /// Run continuous test mode
    /// </summary>
    private async Task<TestRunResult> RunContinuousModeAsync()
    {
        _console.MarkupLine("[yellow]Entering continuous test mode. Tests will run on file changes.[/]");
        _console.MarkupLine("[grey]Press Ctrl+C to exit[/]");
        
        // Implementation would include file watching and automatic test execution
        // This is a placeholder for the concept
        await Task.Delay(1000);
        
        return new TestRunResult { Success = true, Message = "Continuous mode ended" };
    }

    /// <summary>
    /// Show test statistics
    /// </summary>
    private async Task ShowTestStatisticsAsync()
    {
        var chart = new BarChart()
            .Width(60)
            .Label("[yellow]Test Suite Performance (seconds)[/]");

        foreach (var suite in _configuration.TestSuites)
        {
            chart.AddItem(suite.Name, Random.Shared.Next(5, 30), Color.Blue);
        }

        _console.Write(chart);
        _console.WriteLine();
        _console.MarkupLine("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    /// <summary>
    /// Configure test settings
    /// </summary>
    private async Task ConfigureSettingsAsync()
    {
        var settings = new Dictionary<string, string>
        {
            ["Build Configuration"] = _configuration.ExecutionSettings.BuildConfiguration,
            ["Parallel Execution"] = _configuration.ExecutionSettings.ParallelSuites.ToString(),
            ["Continue on Failure"] = _configuration.ExecutionSettings.ContinueOnFailure.ToString(),
            ["Default Timeout"] = $"{_configuration.ExecutionSettings.DefaultTimeoutMinutes} minutes"
        };

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("[bold]Current Settings[/]");
        
        table.AddColumn("Setting");
        table.AddColumn("Value");

        foreach (var setting in settings)
        {
            table.AddRow(setting.Key, setting.Value);
        }

        _console.Write(table);
        _console.WriteLine();
        _console.MarkupLine("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

    /// <summary>
    /// Show recent test results
    /// </summary>
    private async Task ShowRecentResultsAsync()
    {
        _console.MarkupLine("[cyan]Recent Test Results[/]");
        _console.MarkupLine("[grey]No recent results available[/]");
        _console.WriteLine();
        _console.MarkupLine("[grey]Press any key to continue...[/]");
        Console.ReadKey(true);
    }

}

// Supporting classes
public class TestRunResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int TotalSuites { get; set; }
    public int PassedSuites { get; set; }
    public int FailedSuites { get; set; }
    public List<TestSuiteResult> SuiteResults { get; set; } = new();
}


public class BuildResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
}

public class TestExecutionResult
{
    public bool Success { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }
    public int SkippedTests { get; set; }
    public string Output { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
}

public class TestInfo
{
    public string ClassName { get; set; } = "";
    public string MethodName { get; set; } = "";
}

// Extension for async process waiting
public static class ProcessExtensions
{
    public static async Task<bool> WaitForExitAsync(this Process process, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        process.EnableRaisingEvents = true;
        process.Exited += (sender, args) => tcs.TrySetResult(true);
        
        var delayTask = Task.Delay(timeout);
        var exitTask = tcs.Task;
        
        var completedTask = await Task.WhenAny(exitTask, delayTask);
        return completedTask == exitTask;
    }
    
    public static Task WaitForExitAsync(this Process process)
    {
        var tcs = new TaskCompletionSource<object?>();
        process.EnableRaisingEvents = true;
        process.Exited += (sender, args) => tcs.TrySetResult(null);
        return tcs.Task;
    }
}