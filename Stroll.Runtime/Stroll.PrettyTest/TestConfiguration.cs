using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Stroll.PrettyTest;

/// <summary>
/// Root configuration for test execution
/// </summary>
public class TestConfiguration
{
    [YamlMember(Alias = "test_suites")]
    public List<TestSuite> TestSuites { get; set; } = new();
    
    [YamlMember(Alias = "execution_settings")]
    public ExecutionSettings ExecutionSettings { get; set; } = new();
    
    [YamlMember(Alias = "output_settings")]
    public OutputSettings OutputSettings { get; set; } = new();
    
    [YamlMember(Alias = "filters")]
    public FilterSettings Filters { get; set; } = new();
    
    [YamlMember(Alias = "failure_handling")]
    public FailureHandling FailureHandling { get; set; } = new();
    
    [YamlMember(Alias = "notifications")]
    public NotificationSettings Notifications { get; set; } = new();
}

/// <summary>
/// Configuration for a test suite
/// </summary>
public class TestSuite
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    
    [YamlMember(Alias = "project_path")]
    public string ProjectPath { get; set; } = string.Empty;
    
    public string Category { get; set; } = "unit";
    
    [YamlMember(Alias = "timeout_minutes")]
    public int TimeoutMinutes { get; set; } = 10;
    
    public bool Parallel { get; set; } = true;
    
    public Dictionary<string, string> Environment { get; set; } = new();
    
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Global execution settings
/// </summary>
public class ExecutionSettings
{
    [YamlMember(Alias = "build_configuration")]
    public string BuildConfiguration { get; set; } = "Debug";
    
    [YamlMember(Alias = "build_before_test")]
    public bool BuildBeforeTest { get; set; } = true;
    
    [YamlMember(Alias = "parallel_suites")]
    public bool ParallelSuites { get; set; } = true;
    
    [YamlMember(Alias = "max_concurrent_suites")]
    public int MaxConcurrentSuites { get; set; } = 3;
    
    [YamlMember(Alias = "default_timeout_minutes")]
    public int DefaultTimeoutMinutes { get; set; } = 10;
    
    [YamlMember(Alias = "continue_on_failure")]
    public bool ContinueOnFailure { get; set; } = true;
    
    [YamlMember(Alias = "artifacts_directory")]
    public string ArtifactsDirectory { get; set; } = ".testartifacts";
    
    [YamlMember(Alias = "collect_coverage")]
    public bool CollectCoverage { get; set; } = false;
    
    [YamlMember(Alias = "coverage_format")]
    public string CoverageFormat { get; set; } = "cobertura";
}

/// <summary>
/// Output formatting settings
/// </summary>
public class OutputSettings
{
    [YamlMember(Alias = "use_colors")]
    public bool UseColors { get; set; } = true;
    
    [YamlMember(Alias = "console_verbosity")]
    public string ConsoleVerbosity { get; set; } = "normal";
    
    [YamlMember(Alias = "show_build_output")]
    public bool ShowBuildOutput { get; set; } = false;
    
    [YamlMember(Alias = "show_test_output")]
    public bool ShowTestOutput { get; set; } = false;
    
    [YamlMember(Alias = "generate_html_report")]
    public bool GenerateHtmlReport { get; set; } = false;
    
    [YamlMember(Alias = "save_detailed_logs")]
    public bool SaveDetailedLogs { get; set; } = true;
}

/// <summary>
/// Test filtering settings
/// </summary>
public class FilterSettings
{
    [YamlMember(Alias = "default_categories")]
    public List<string> DefaultCategories { get; set; } = new();
    
    [YamlMember(Alias = "default_tags")]
    public List<string> DefaultTags { get; set; } = new();
    
    [YamlMember(Alias = "exclude_tags")]
    public List<string> ExcludeTags { get; set; } = new();
    
    [YamlMember(Alias = "exclude_patterns")]
    public List<string> ExcludePatterns { get; set; } = new();
}

/// <summary>
/// Failure handling settings
/// </summary>
public class FailureHandling
{
    [YamlMember(Alias = "fail_fast")]
    public bool FailFast { get; set; } = false;
    
    [YamlMember(Alias = "max_failures")]
    public int MaxFailures { get; set; } = 10;
    
    [YamlMember(Alias = "retry_failed")]
    public bool RetryFailed { get; set; } = false;
    
    [YamlMember(Alias = "retry_count")]
    public int RetryCount { get; set; } = 2;
    
    [YamlMember(Alias = "save_failure_logs")]
    public bool SaveFailureLogs { get; set; } = true;
}

/// <summary>
/// Notification settings
/// </summary>
public class NotificationSettings
{
    public bool Enabled { get; set; } = false;
    public List<string> Channels { get; set; } = new();
}

/// <summary>
/// Result of a test suite execution
/// </summary>
public class TestSuiteResult
{
    public string Name { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public bool BuildSucceeded { get; set; }
    public bool TestsSucceeded { get; set; }
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }
    public int SkippedTests { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public List<TestFailure> Failures { get; set; } = new();
    public List<BuildError> BuildErrors { get; set; } = new();
    public string? TrxPath { get; set; }
    public string? LogPath { get; set; }
}

/// <summary>
/// Details of a test failure
/// </summary>
public class TestFailure
{
    public string TestName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string StackTrace { get; set; } = string.Empty;
    public string StdOut { get; set; } = string.Empty;
    public string StdErr { get; set; } = string.Empty;
}

/// <summary>
/// Details of a build error
/// </summary>
public class BuildError
{
    public string File { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "error";
}

/// <summary>
/// Overall test run summary
/// </summary>
public class TestRunSummary
{
    public int TotalSuites { get; set; }
    public int SuccessfulSuites { get; set; }
    public int FailedSuites { get; set; }
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }
    public int SkippedTests { get; set; }
    public TimeSpan TotalExecutionTime { get; set; }
    public List<TestSuiteResult> SuiteResults { get; set; } = new();
}

/// <summary>
/// Configuration loader for test settings
/// </summary>
public static class TestConfigurationLoader
{
    public static async Task<TestConfiguration> LoadAsync(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Test configuration file not found: {configPath}");
        }

        var yaml = await File.ReadAllTextAsync(configPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<TestConfiguration>(yaml);
    }

    public static async Task<TestConfiguration> LoadDefaultAsync()
    {
        var configPath = Path.Combine(
            AppContext.BaseDirectory,
            "test-configuration.yml"
        );

        return await LoadAsync(configPath);
    }
}