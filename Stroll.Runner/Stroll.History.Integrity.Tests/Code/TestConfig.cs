using System.Text.Json;

namespace Stroll.Runner.HistoryIntegrity;

public sealed class TestConfig
{
    public string Mode { get; init; } = "dotnet-run"; // or "exe"
    public string? Project { get; init; }
    public string? Exe { get; init; }
    public Dictionary<string,string> Env { get; init; } = new();
    public int TimeoutMs { get; init; } = 60_000;

    public static TestConfig Load(string baseDir)
    {
        var path = Path.Combine(baseDir, "Tools", "cli.config.json");
        var cfg = JsonSerializer.Deserialize<TestConfig>(File.ReadAllText(path))!;

        var envMode = Environment.GetEnvironmentVariable("HISTORY_LAUNCH_MODE");
        if (!string.IsNullOrWhiteSpace(envMode)) cfg.Mode = envMode;

        var envProject = Environment.GetEnvironmentVariable("HISTORY_PROJECT");
        if (!string.IsNullOrWhiteSpace(envProject)) cfg.Project = envProject;

        var envExe = Environment.GetEnvironmentVariable("HISTORY_EXE");
        if (!string.IsNullOrWhiteSpace(envExe)) cfg.Exe = envExe;

        var envData = Environment.GetEnvironmentVariable("STROLL_DATA");
        if (!string.IsNullOrWhiteSpace(envData)) cfg.Env["STROLL_DATA"] = envData;

        var envTimeout = Environment.GetEnvironmentVariable("HISTORY_TIMEOUT_MS");
        if (int.TryParse(envTimeout, out var t) && t > 0) cfg.TimeoutMs = t;

        return cfg;
    }
}
