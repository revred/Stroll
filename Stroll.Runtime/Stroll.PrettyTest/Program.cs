using System.CommandLine;
using Spectre.Console;

namespace Stroll.PrettyTest;

/// <summary>
/// Enhanced Stroll Test Runner with YAML configuration and elegant output
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure console for better color support
        AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
        
        try
        {
            var rootCommand = CreateRootCommand();
            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand("Stroll Test Runner - Execute all test suites with elegant output")
        {
            CreateRunCommand(),
            CreateListCommand(),
            CreateValidateCommand()
        };

        return rootCommand;
    }

    private static Command CreateRunCommand()
    {
        var runCommand = new Command("run", "Run test suites");

        var configOption = new Option<string?>(
            aliases: ["--config", "-c"],
            description: "Path to test configuration file",
            getDefaultValue: () => null);

        var filterOption = new Option<string[]>(
            aliases: ["--filter", "-f"],
            description: "Filter tests by category:name, tag:name, or name pattern",
            getDefaultValue: () => Array.Empty<string>());

        var parallelOption = new Option<bool>(
            aliases: ["--parallel", "-p"],
            description: "Override parallel execution setting",
            getDefaultValue: () => false);

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Enable verbose output",
            getDefaultValue: () => false);

        runCommand.AddOption(configOption);
        runCommand.AddOption(filterOption);
        runCommand.AddOption(parallelOption);
        runCommand.AddOption(verboseOption);

        runCommand.SetHandler(async (configPath, filters, parallel, verbose) =>
        {
            try
            {
                var config = await LoadConfigurationAsync(configPath);
                
                // Override settings from command line
                if (parallel)
                {
                    config.ExecutionSettings.ParallelSuites = true;
                }

                if (verbose)
                {
                    config.OutputSettings.ConsoleVerbosity = "detailed";
                    config.OutputSettings.ShowBuildOutput = true;
                    config.OutputSettings.ShowTestOutput = true;
                }

                var rootPath = FindRepositoryRoot();
                var runner = new TestRunner(config, rootPath);
                
                var summary = await runner.RunAllAsync(filters.Length > 0 ? filters : null);
                
                Environment.Exit(summary.FailedSuites == 0 && summary.FailedTests == 0 ? 0 : 1);
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                Environment.Exit(1);
            }
        }, configOption, filterOption, parallelOption, verboseOption);

        return runCommand;
    }

    private static Command CreateListCommand()
    {
        var listCommand = new Command("list", "List available test suites");

        var configOption = new Option<string?>(
            aliases: ["--config", "-c"],
            description: "Path to test configuration file",
            getDefaultValue: () => null);

        listCommand.AddOption(configOption);

        listCommand.SetHandler(async (configPath) =>
        {
            try
            {
                var config = await LoadConfigurationAsync(configPath);
                DisplayTestSuites(config);
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                Environment.Exit(1);
            }
        }, configOption);

        return listCommand;
    }

    private static Command CreateValidateCommand()
    {
        var validateCommand = new Command("validate", "Validate test configuration");

        var configOption = new Option<string?>(
            aliases: ["--config", "-c"],
            description: "Path to test configuration file",
            getDefaultValue: () => null);

        validateCommand.AddOption(configOption);

        validateCommand.SetHandler(async (configPath) =>
        {
            try
            {
                var config = await LoadConfigurationAsync(configPath);
                await ValidateConfigurationAsync(config);
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                Environment.Exit(1);
            }
        }, configOption);

        return validateCommand;
    }

    private static async Task<TestConfiguration> LoadConfigurationAsync(string? configPath)
    {
        configPath ??= Path.Combine(AppContext.BaseDirectory, "test-configuration.yml");

        if (!File.Exists(configPath))
        {
            AnsiConsole.MarkupLine($"[red]Configuration file not found: {configPath}[/]");
            AnsiConsole.MarkupLine("[yellow]Creating default configuration...[/]");
            
            // Create a default configuration file
            await CreateDefaultConfigurationAsync(configPath);
            AnsiConsole.MarkupLine($"[green]Created default configuration at: {configPath}[/]");
        }

        return await TestConfigurationLoader.LoadAsync(configPath);
    }

    private static async Task CreateDefaultConfigurationAsync(string configPath)
    {
        var defaultConfig = @"# Stroll Test Suite Configuration
test_suites:
  - name: ""History Integrity Tests""
    description: ""IPC contract validation and performance regression tests""
    project_path: ""Stroll.History/Stroll.History.Integrity.Tests""
    category: ""integration""
    timeout_minutes: 10
    parallel: false
    tags: [""integrity"", ""ipc""]

execution_settings:
  build_configuration: ""Debug""
  build_before_test: true
  parallel_suites: true
  max_concurrent_suites: 3
  continue_on_failure: true
  artifacts_directory: "".testartifacts""

output_settings:
  use_colors: true
  console_verbosity: ""normal""
  save_detailed_logs: true

failure_handling:
  fail_fast: false
  max_failures: 10
  save_failure_logs: true
";

        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, defaultConfig);
    }

    private static void DisplayTestSuites(TestConfiguration config)
    {
        AnsiConsole.Write(new FigletText("Test Suites").Centered().Color(Color.Cyan1));
        AnsiConsole.WriteLine();

        if (!config.TestSuites.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No test suites configured[/]");
            return;
        }

        var table = new Table()
            .AddColumn(new TableColumn("Name").Centered())
            .AddColumn(new TableColumn("Category").Centered())
            .AddColumn(new TableColumn("Description").Centered())
            .AddColumn(new TableColumn("Path").Centered())
            .AddColumn(new TableColumn("Tags").Centered());

        table.Title = new TableTitle("Available Test Suites");
        table.Border = TableBorder.Rounded;

        foreach (var suite in config.TestSuites)
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
                new Text(suite.Description),
                new Markup($"[grey]{suite.ProjectPath}[/]"),
                new Markup($"[grey]{tagsText}[/]")
            );
        }

        AnsiConsole.Write(table);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Total: {config.TestSuites.Count} test suites configured[/]");
    }

    private static async Task ValidateConfigurationAsync(TestConfiguration config)
    {
        AnsiConsole.MarkupLine("[bold]Validating test configuration...[/]");
        AnsiConsole.WriteLine();

        var rootPath = FindRepositoryRoot();
        var validationResults = new List<(string suite, string status, string message)>();

        foreach (var suite in config.TestSuites)
        {
            var projectPath = Path.Combine(rootPath, suite.ProjectPath);
            
            if (Directory.Exists(projectPath))
            {
                var csprojFiles = Directory.GetFiles(projectPath, "*.csproj");
                if (csprojFiles.Any())
                {
                    validationResults.Add((suite.Name, "✅", "Project found"));
                }
                else
                {
                    validationResults.Add((suite.Name, "❌", "No .csproj file found"));
                }
            }
            else if (File.Exists(projectPath) && projectPath.EndsWith(".csproj"))
            {
                validationResults.Add((suite.Name, "✅", "Project file found"));
            }
            else
            {
                validationResults.Add((suite.Name, "❌", $"Path does not exist: {projectPath}"));
            }
        }

        var validationTable = new Table()
            .AddColumn("Test Suite")
            .AddColumn("Status")
            .AddColumn("Message")
            .Border(TableBorder.Rounded)
            .Title("Validation Results");

        foreach (var (suite, status, message) in validationResults)
        {
            var messageColor = status.Contains("✅") ? "green" : "red";
            validationTable.AddRow(
                new Markup($"[bold]{suite}[/]"),
                new Markup(status),
                new Markup($"[{messageColor}]{message}[/]")
            );
        }

        AnsiConsole.Write(validationTable);
        AnsiConsole.WriteLine();

        var validSuites = validationResults.Count(r => r.status.Contains("✅"));
        var totalSuites = validationResults.Count;

        if (validSuites == totalSuites)
        {
            AnsiConsole.MarkupLine("[green bold]✅ All test suites are valid![/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red bold]❌ {totalSuites - validSuites} test suite(s) have issues[/]");
            Environment.Exit(1);
        }
    }

    private static string FindRepositoryRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        
        while (currentDir != null)
        {
            if (Directory.Exists(Path.Combine(currentDir, ".git")) ||
                File.Exists(Path.Combine(currentDir, "Stroll.sln")))
            {
                return currentDir;
            }
            
            var parent = Directory.GetParent(currentDir);
            currentDir = parent?.FullName;
        }

        // Fallback to current directory
        return Directory.GetCurrentDirectory();
    }
}
