using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Stroll.PrettyTest;

/// <summary>
/// Helper methods for TestRunner
/// </summary>
public partial class TestRunner
{
    /// <summary>
    /// Filter test suites based on command line arguments
    /// </summary>
    private List<TestSuite> FilterTestSuites(List<TestSuite> allSuites, string[]? filter)
    {
        if (filter == null || filter.Length == 0)
        {
            return allSuites.ToList();
        }

        var filtered = new List<TestSuite>();
        
        foreach (var suite in allSuites)
        {
            bool include = false;

            foreach (var filterItem in filter)
            {
                if (filterItem.StartsWith("category:"))
                {
                    var category = filterItem.Substring("category:".Length);
                    if (suite.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                        include = true;
                }
                else if (filterItem.StartsWith("tag:"))
                {
                    var tag = filterItem.Substring("tag:".Length);
                    if (suite.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                        include = true;
                }
                else if (filterItem.StartsWith("name:"))
                {
                    var namePattern = filterItem.Substring("name:".Length);
                    if (suite.Name.Contains(namePattern, StringComparison.OrdinalIgnoreCase))
                        include = true;
                }
                else
                {
                    // Default: match by name
                    if (suite.Name.Contains(filterItem, StringComparison.OrdinalIgnoreCase))
                        include = true;
                }
            }

            if (include)
                filtered.Add(suite);
        }

        return filtered;
    }

    /// <summary>
    /// Find the .csproj file in a directory
    /// </summary>
    private string? FindProjectFile(string projectPath)
    {
        if (File.Exists(projectPath) && projectPath.EndsWith(".csproj"))
        {
            return projectPath;
        }

        if (Directory.Exists(projectPath))
        {
            var csprojFiles = Directory.GetFiles(projectPath, "*.csproj");
            return csprojFiles.FirstOrDefault();
        }

        return null;
    }

    /// <summary>
    /// Build command line arguments for dotnet test
    /// </summary>
    private string BuildTestArguments(string projectFile, TestSuite suite, string trxPath)
    {
        var args = new List<string>
        {
            "test",
            $"\"{projectFile}\"",
            $"-c {_config.ExecutionSettings.BuildConfiguration}",
            $"-l \"trx;LogFileName={trxPath}\"",
            $"-l \"console;verbosity={GetConsoleVerbosity()}\""
        };

        if (_config.ExecutionSettings.CollectCoverage)
        {
            args.Add("--collect:\"XPlat Code Coverage\"");
        }

        if (suite.Parallel)
        {
            args.Add("--parallel");
        }

        return string.Join(" ", args);
    }

    /// <summary>
    /// Get console verbosity setting
    /// </summary>
    private string GetConsoleVerbosity()
    {
        return _config.OutputSettings.ConsoleVerbosity.ToLowerInvariant() switch
        {
            "minimal" => "minimal",
            "detailed" => "detailed",
            _ => "normal"
        };
    }

    /// <summary>
    /// Display the test execution plan
    /// </summary>
    private void DisplayTestPlan(List<TestSuite> suites)
    {
        var table = new Table()
            .AddColumn(new TableColumn("Test Suite").Centered())
            .AddColumn(new TableColumn("Category").Centered())
            .AddColumn(new TableColumn("Tags").Centered())
            .AddColumn(new TableColumn("Timeout").Centered())
            .AddColumn(new TableColumn("Path").Centered());

        table.Title = new TableTitle("Test Execution Plan");
        table.Border = TableBorder.Rounded;

        foreach (var suite in suites)
        {
            var categoryColor = suite.Category.ToLowerInvariant() switch
            {
                "unit" => "green",
                "integration" => "yellow",
                "performance" => "red",
                _ => "white"
            };

            var tagsText = suite.Tags.Any() ? string.Join(", ", suite.Tags) : "-";
            
            table.AddRow(
                new Markup($"[bold]{suite.Name}[/]"),
                new Markup($"[{categoryColor}]{suite.Category}[/]"),
                new Markup($"[grey]{tagsText}[/]"),
                new Markup($"[blue]{suite.TimeoutMinutes}m[/]"),
                new Markup($"[grey]{suite.ProjectPath}[/]")
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Calculate summary statistics
    /// </summary>
    private void CalculateSummaryStats(TestRunSummary summary)
    {
        summary.SuccessfulSuites = summary.SuiteResults.Count(r => r.TestsSucceeded && r.BuildSucceeded);
        summary.FailedSuites = summary.SuiteResults.Count(r => !r.TestsSucceeded || !r.BuildSucceeded);
        summary.TotalTests = summary.SuiteResults.Sum(r => r.TotalTests);
        summary.PassedTests = summary.SuiteResults.Sum(r => r.PassedTests);
        summary.FailedTests = summary.SuiteResults.Sum(r => r.FailedTests);
        summary.SkippedTests = summary.SuiteResults.Sum(r => r.SkippedTests);
    }

    /// <summary>
    /// Display final test results with elegant formatting
    /// </summary>
    private void DisplayFinalResults(TestRunSummary summary)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Test Results Summary[/]").RuleStyle(Style.Parse("cyan")));
        AnsiConsole.WriteLine();

        // Overall statistics
        var overallTable = new Table()
            .AddColumn("Metric")
            .AddColumn("Value")
            .Border(TableBorder.Rounded)
            .Title("Overall Statistics");

        overallTable.AddRow("🏃 Total Execution Time", $"[yellow]{summary.TotalExecutionTime:mm\\:ss\\.fff}[/]");
        overallTable.AddRow("📦 Test Suites", $"{summary.SuccessfulSuites}[green]/[/]{summary.TotalSuites}");
        overallTable.AddRow("🧪 Total Tests", $"{summary.PassedTests}[green]/[/]{summary.TotalTests}");
        overallTable.AddRow("❌ Failed Tests", $"[red]{summary.FailedTests}[/]");
        overallTable.AddRow("⏭️ Skipped Tests", $"[yellow]{summary.SkippedTests}[/]");

        AnsiConsole.Write(overallTable);
        AnsiConsole.WriteLine();

        // Suite results
        if (summary.SuiteResults.Any())
        {
            var suiteTable = new Table()
                .AddColumn("Suite")
                .AddColumn("Build")
                .AddColumn("Tests")
                .AddColumn("Passed")
                .AddColumn("Failed")
                .AddColumn("Skipped")
                .AddColumn("Time")
                .Border(TableBorder.Rounded)
                .Title("Suite Results");

            foreach (var suite in summary.SuiteResults.OrderBy(r => r.Name))
            {
                var buildStatus = suite.BuildSucceeded ? "[green]✓[/]" : "[red]✗[/]";
                var testStatus = suite.TestsSucceeded ? "[green]✓[/]" : "[red]✗[/]";
                var passedColor = suite.FailedTests > 0 ? "yellow" : "green";

                suiteTable.AddRow(
                    new Markup($"[bold]{suite.Name}[/]"),
                    new Markup(buildStatus),
                    new Markup(testStatus),
                    new Markup($"[{passedColor}]{suite.PassedTests}[/]"),
                    new Markup($"[red]{suite.FailedTests}[/]"),
                    new Markup($"[yellow]{suite.SkippedTests}[/]"),
                    new Markup($"[grey]{suite.ExecutionTime:mm\\:ss}[/]")
                );
            }

            AnsiConsole.Write(suiteTable);
            AnsiConsole.WriteLine();
        }

        // Display failures
        DisplayFailures(summary);

        // Display build errors
        DisplayBuildErrors(summary);

        // Final status with clear spacing and visibility
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
        
        var passRate = summary.TotalTests > 0 ? (double)summary.PassedTests / summary.TotalTests * 100 : 0;
        
        if (summary.FailedSuites == 0 && summary.FailedTests == 0)
        {
            AnsiConsole.Write(new Panel(new Markup("[green bold]🎉 ALL TESTS PASSED! 100% Success Rate[/]"))
                .Border(BoxBorder.Double)
                .BorderColor(Color.Green)
                .Padding(1, 0)
                .Expand());
        }
        else
        {
            var statusText = $"[red bold]❌ TESTS FAILED[/]\n\n" +
                           $"[white]Pass Rate: {passRate:F1}% ({summary.PassedTests}/{summary.TotalTests})[/]\n" +
                           $"[red]Failed Tests: {summary.FailedTests}[/]\n" +
                           $"[red]Failed Suites: {summary.FailedSuites}[/]";
                           
            AnsiConsole.Write(new Panel(new Markup(statusText))
                .Border(BoxBorder.Double)
                .BorderColor(Color.Red)
                .Padding(1, 0)
                .Expand());
        }
        
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Display test failures with detailed information
    /// </summary>
    private void DisplayFailures(TestRunSummary summary)
    {
        var allFailures = summary.SuiteResults.SelectMany(r => 
            r.Failures.Select(f => new { Suite = r.Name, Failure = f })).ToList();

        if (!allFailures.Any())
            return;

        AnsiConsole.Write(new Rule("[red]Test Failures[/]").LeftJustified());
        AnsiConsole.WriteLine();

        foreach (var failure in allFailures.Take(5)) // Show fewer failures for better readability
        {
            // Truncate test names that are too long
            var displayName = failure.Failure.TestName.Length > 60 
                ? failure.Failure.TestName.Substring(0, 57) + "..." 
                : failure.Failure.TestName;
                
            var panel = new Panel(FormatFailureContent(failure.Failure))
                .Header($"[red]{failure.Suite}: {displayName}[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Red)
                .Collapse();

            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }

        if (allFailures.Count > 5)
        {
            AnsiConsole.MarkupLine($"[grey]... and {allFailures.Count - 5} more failures (see detailed logs)[/]");
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Format failure content for display
    /// </summary>
    private IRenderable FormatFailureContent(TestFailure failure)
    {
        var grid = new Grid().AddColumn().AddColumn();

        if (!string.IsNullOrWhiteSpace(failure.Message))
        {
            var message = failure.Message.Trim();
            if (message.Length > 300)
            {
                message = message.Substring(0, 297) + "...";
            }
            grid.AddRow(new Markup("[bold]Message:[/]"), new Text(message));
        }

        if (!string.IsNullOrWhiteSpace(failure.StdOut))
        {
            var output = failure.StdOut.Trim();
            if (output.Length > 300)
            {
                output = output.Substring(0, 297) + "...";
            }
            grid.AddRow(new Markup("[bold]Output:[/]"), new Text(output));
        }

        if (!string.IsNullOrWhiteSpace(failure.StdErr))
        {
            var error = failure.StdErr.Trim();
            if (error.Length > 300)
            {
                error = error.Substring(0, 297) + "...";
            }
            grid.AddRow(new Markup("[bold]Error:[/]"), new Text(error));
        }

        return grid;
    }

    /// <summary>
    /// Display build errors with elegant formatting
    /// </summary>
    private void DisplayBuildErrors(TestRunSummary summary)
    {
        var allBuildErrors = summary.SuiteResults.SelectMany(r => 
            r.BuildErrors.Select(e => new { Suite = r.Name, Error = e })).ToList();

        if (!allBuildErrors.Any())
            return;

        AnsiConsole.Write(new Rule("[red]Build Errors[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var errorGroups = allBuildErrors.GroupBy(e => e.Suite);

        foreach (var group in errorGroups)
        {
            var suiteErrors = group.ToList();
            var errorColor = suiteErrors.Any(e => e.Error.Severity == "error") ? "red" : "yellow";

            var panel = new Panel(FormatBuildErrors(suiteErrors.Select(e => e.Error).ToList()))
                .Header($"[{errorColor}]{group.Key}[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(errorColor == "red" ? Color.Red : Color.Yellow)
                .Collapse();

            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }
    }

    /// <summary>
    /// Format build errors for display
    /// </summary>
    private IRenderable FormatBuildErrors(List<BuildError> errors)
    {
        var table = new Table()
            .AddColumn("Severity")
            .AddColumn("Location")
            .AddColumn("Code")
            .AddColumn("Message")
            .Border(TableBorder.None)
            .HideHeaders();

        foreach (var error in errors.Take(10)) // Limit to first 10 errors
        {
            var severityColor = error.Severity == "error" ? "red" : "yellow";
            var location = string.IsNullOrEmpty(error.File) ? "-" : 
                $"{Path.GetFileName(error.File)}({error.Line},{error.Column})";

            table.AddRow(
                new Markup($"[{severityColor}]{error.Severity}[/]"),
                new Markup($"[grey]{location}[/]"),
                new Markup($"[blue]{error.ErrorCode}[/]"),
                new Text(error.Message.Trim())
            );
        }

        if (errors.Count > 10)
        {
            table.AddRow(
                new Markup(""),
                new Markup(""),
                new Markup(""),
                new Text($"... and {errors.Count - 10} more errors")
            );
        }

        return table;
    }
}