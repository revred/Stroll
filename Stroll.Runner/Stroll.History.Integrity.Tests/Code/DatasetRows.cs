using System.Globalization;

namespace Stroll.Runner.HistoryIntegrity;

public sealed class DatasetRows
{
    public sealed record Row(DateOnly Date, string Instrument, string Expected);

    public static IEnumerable<Row> Load(string baseDir)
    {
        var csv = Path.Combine(baseDir, "Integrity", "integrity_dataset.csv");
        using var sr = new StreamReader(csv);
        _ = sr.ReadLine();
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            var parts = line.Split(',');
            var date = DateOnly.ParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            yield return new Row(date, parts[1], parts[2]);
        }
    }

    public static IEnumerable<object[]> Sample(string baseDir, int count = 200)
        => Load(baseDir).Take(count).Select(r => new object[] { r });

    public static IEnumerable<object[]> Full(string baseDir)
        => Load(baseDir).Select(r => new object[] { r });
}
