using Xunit;
using FluentAssertions;

namespace Stroll.Runner.HistoryIntegrity;

public class ContractTests
{
    private readonly Cli _cli;
    private readonly string _baseDir;

    public ContractTests()
    {
        _baseDir = AppContext.BaseDirectory;
        var cfg = TestConfig.Load(_baseDir);
        _cli = new Cli(cfg, _baseDir);
    }

    [Fact(DisplayName = "discover responds with command inventory")]
    public async Task Discover_Works()
    {
        var doc = await _cli.RunAsync("discover");
        Invariants.EnvelopeOk(doc);
        var data = doc.RootElement.Require("data");
        data.GetStringOrEmpty("service").Should().Be("stroll.history");
        data.GetStringOrEmpty("version").Should().NotBeNullOrWhiteSpace();
    }

    [Fact(DisplayName = "list-datasets returns at least one dataset")]
    public async Task ListDatasets_Works()
    {
        var doc = await _cli.RunAsync("list-datasets");
        Invariants.EnvelopeOk(doc);
        var datasets = doc.RootElement.Require("data").Require("datasets");
        datasets.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
    }
}
