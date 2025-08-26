using Xunit;
using Xunit.Sdk;

namespace Stroll.Runner.HistoryIntegrity;

public class FullSweepTests
{
    private readonly Cli _cli;
    private readonly string _baseDir;

    public FullSweepTests()
    {
        if (Environment.GetEnvironmentVariable("RUN_FULL") != "1")
            throw new SkipException("Set RUN_FULL=1 to enable FullSweepTests.");
        _baseDir = AppContext.BaseDirectory;
        var cfg = TestConfig.Load(_baseDir);
        _cli = new Cli(cfg, _baseDir);
    }

    public static IEnumerable<object[]> AllRows()
        => DatasetRows.Full(AppContext.BaseDirectory);

    [Theory(DisplayName = "full sweep: bars/options invariants across 10k rows"), MemberData(nameof(AllRows))]
    public async Task FullSweep(DatasetRows.Row r)
    {
        var parts = r.Instrument.Split(':');
        var symbol = parts[0];
        var kind = parts[1];

        if (kind == "bars1d")
        {
            var doc = await _cli.RunAsync("get-bars", "--symbol", symbol, "--from", r.Date.ToString("yyyy-MM-dd"), "--to", r.Date.ToString("yyyy-MM-dd"), "--granularity", "1d");
            Invariants.EnvelopeOk(doc);
            Invariants.Bars(doc);
        }
        else if (kind == "options")
        {
            var doc = await _cli.RunAsync("get-options", "--symbol", symbol, "--date", r.Date.ToString("yyyy-MM-dd"));
            Invariants.EnvelopeOk(doc);
            var meta = doc.RootElement.Require("meta");
            var count = meta.TryGetProperty("count", out var c) ? c.GetInt32() : doc.RootElement.Require("data").Require("chain").GetArrayLength();
            if (count > 0) Invariants.Options(doc);
        }
    }
}
