namespace Stroll.Storage;

public enum Granularity { OneMinute, FiveMinute, Daily }

public static class GranularityExtensions
{
    public static Granularity Parse(string s) => s.ToLowerInvariant() switch
    {
        "1m" or "1min" => Granularity.OneMinute,
        "5m" or "5min" => Granularity.FiveMinute,
        "1d" or "d" or "day" => Granularity.Daily,
        _ => throw new ArgumentException("granularity must be 1m|5m|1d")
    };
    public static string Canon(this Granularity g) => g switch
    {
        Granularity.OneMinute => "1m",
        Granularity.FiveMinute => "5m",
        Granularity.Daily => "1d",
        _ => "1m"
    };
}

public record DatasetInfo(string Name, string Kind, string Path, string Granularity);

public interface IStorageProvider
{
    DataCatalog Catalog { get; }
    Task<IReadOnlyList<IDictionary<string, object?>>> GetBarsRawAsync(string symbol, DateOnly from, DateOnly to, Granularity g);
    Task<IReadOnlyList<IDictionary<string, object?>>> GetOptionsChainRawAsync(string symbol, DateOnly expiry);
}

public sealed class DataCatalog
{
    public IReadOnlyList<DatasetInfo> Datasets { get; }
    public string Root { get; }

    public DataCatalog(IEnumerable<DatasetInfo> sets, string root)
    {
        Datasets = sets.ToList();
        Root = root;
    }

    public static DataCatalog Default(string? rootEnv = null)
    {
        string root = string.IsNullOrWhiteSpace(rootEnv) ? "./data" : rootEnv!;
        return new(new[]
        {
            new DatasetInfo("XSP-bars", "bars", Path.Combine(root, "XSP_1m.parquet"), "1m"),
            new DatasetInfo("SPX-bars", "bars", Path.Combine(root, "SPX_1m.parquet"), "1m"),
            new DatasetInfo("VIX-bars", "bars", Path.Combine(root, "VIX_1m.parquet"), "1m"),
            new DatasetInfo("XSP-options", "options", Path.Combine(root, "XSP_options.sqlite"), "EOD"),
        }, root);
    }
}
