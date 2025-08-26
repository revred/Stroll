using System.Text.Json;
using FluentAssertions;

namespace Stroll.Runner.HistoryIntegrity;

public static class Invariants
{
    public static void EnvelopeOk(JsonDocument doc)
    {
        var root = doc.RootElement;
        root.Require("schema").GetString().Should().Be("stroll.history.v1");
        root.Require("ok").GetBoolean().Should().BeTrue();
        _ = root.Require("data");
        _ = root.Require("meta");
    }

    public static void Bars(JsonDocument doc)
    {
        var bars = doc.RootElement.Require("data").Require("bars");
        bars.ValueKind.Should().Be(JsonValueKind.Array);
        DateTime? prev = null;
        foreach (var b in bars.EnumerateArray())
        {
            var o = b.GetDouble("o","Open");
            var h = b.GetDouble("h","High");
            var l = b.GetDouble("l","Low");
            var c = b.GetDouble("c","Close");
            var v = b.GetDouble("v","Volume");
            (l <= o && o <= h).Should().BeTrue("low ≤ open ≤ high");
            (l <= c && c <= h).Should().BeTrue("low ≤ close ≤ high");
            (v >= 0).Should().BeTrue("volume non-negative");

            if (b.TryGetProperty("t", out var t) || b.TryGetProperty("Time", out t))
            {
                var dt = t.ValueKind == JsonValueKind.String ? DateTime.Parse(t.GetString()!) : t.GetDateTime();
                if (prev is not null) (dt >= prev).Should().BeTrue("timestamps must be non-decreasing");
                prev = dt;
            }
        }
        var meta = doc.RootElement.Require("meta");
        if (meta.TryGetProperty("count", out var cnt) && cnt.ValueKind == JsonValueKind.Number)
            cnt.GetInt32().Should().Be(bars.GetArrayLength());
    }

    public static void Options(JsonDocument doc)
    {
        var chain = doc.RootElement.Require("data").Require("chain");
        chain.ValueKind.Should().Be(JsonValueKind.Array);
        foreach (var row in chain.EnumerateArray())
        {
            var bid = row.GetDouble("bid","Bid");
            var ask = row.GetDouble("ask","Ask");
            (bid <= ask).Should().BeTrue("bid ≤ ask");
            if (row.TryGetProperty("strike", out var s) || row.TryGetProperty("Strike", out s))
                if (s.ValueKind == JsonValueKind.Number) s.GetDouble().Should().BeGreaterThan(0);
        }
    }
}
