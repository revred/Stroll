using Xunit;

namespace Stroll.Runner.HistoryIntegrity;

public class SmokeTests
{
    private readonly Cli _cli;
    private readonly string _baseDir;

    public SmokeTests()
    {
        _baseDir = AppContext.BaseDirectory;
        var cfg = TestConfig.Load(_baseDir);
        _cli = new Cli(cfg, _baseDir);
    }

    public static IEnumerable<object[]> SampleRows()
        => DatasetRows.Sample(AppContext.BaseDirectory, 200);

    [Theory(DisplayName = "bars (1d) invariants hold [sample]"), MemberData(nameof(SampleRows))]
    public async Task Bars_Sample(DatasetRows.Row r)
    {
        if (!r.Instrument.EndsWith(":bars1d", StringComparison.OrdinalIgnoreCase)) return;
        var symbol = r.Instrument.Split(':')[0];
        var doc = await _cli.RunAsync("get-bars", "--symbol", symbol, "--from", r.Date.ToString("yyyy-MM-dd"), "--to", r.Date.ToString("yyyy-MM-dd"), "--granularity", "1d");
        Invariants.EnvelopeOk(doc);
        Invariants.Bars(doc);
    }

    [Theory(DisplayName = "options invariants hold [sample]"), MemberData(nameof(SampleRows))]
    public async Task Options_Sample(DatasetRows.Row r)
    {
        if (!r.Instrument.EndsWith(":options", StringComparison.OrdinalIgnoreCase)) return;
        var symbol = r.Instrument.Split(':')[0];
        var doc = await _cli.RunAsync("get-options", "--symbol", symbol, "--date", r.Date.ToString("yyyy-MM-dd"));
        Invariants.EnvelopeOk(doc);
        var meta = doc.RootElement.Require("meta");
        var count = meta.TryGetProperty("count", out var c) ? c.GetInt32() : doc.RootElement.Require("data").Require("chain").GetArrayLength();
        if (count > 0) Invariants.Options(doc);
    }
}
