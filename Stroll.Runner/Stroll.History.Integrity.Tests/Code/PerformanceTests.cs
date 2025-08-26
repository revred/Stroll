using Xunit;
using Xunit.Sdk;
using FluentAssertions;
using System.Diagnostics;

namespace Stroll.Runner.HistoryIntegrity;

public class PerformanceTests
{
    private readonly Cli _cli;
    private readonly string _baseDir;

    public PerformanceTests()
    {
        if (Environment.GetEnvironmentVariable("RUN_LATENCY") != "1")
            throw new SkipException("Set RUN_LATENCY=1 to enable PerformanceTests.");
        _baseDir = AppContext.BaseDirectory;
        var cfg = TestConfig.Load(_baseDir);
        _cli = new Cli(cfg, _baseDir);
    }

    [Fact(DisplayName = "SPY options quick response (best‑effort)")]
    public async Task SpyOptions_Latency()
    {
        var sw = Stopwatch.StartNew();
        _ = await _cli.RunAsync("get-options", "--symbol", "SPY", "--date", "2024-05-17");
        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(150);
    }
}
