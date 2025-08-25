using System.Text.Json;
using System.Text.Json.Serialization;
using Stroll.Storage;

namespace Stroll.Dataset;

public interface IPackager
{
    string Discover();
    string Version();
    string Datasets(DataCatalog catalog);
    string BarsRaw(string symbol, DateOnly from, DateOnly to, Granularity g, IReadOnlyList<IDictionary<string, object?>> rows);
    string OptionsChainRaw(string symbol, DateOnly expiry, IReadOnlyList<IDictionary<string, object?>> rows);
}

public sealed class JsonPackager : IPackager
{
    private readonly string _schema;
    private readonly string _version;

    public JsonPackager(string schemaVersion, string serviceVersion)
    { _schema = schemaVersion; _version = serviceVersion; }

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null, // preserve original keys
        NumberHandling = JsonNumberHandling.Strict,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    static string J(object o) => JsonSerializer.Serialize(o, Options);

    public string Discover() => J(new
    {
        schema = _schema,
        ok = true,
        data = new {
            service = "stroll.history",
            version = _version,
            env = new[]{ new { name="STROLL_DATA", desc="dataset root override", required=false } },
            commands = new object[] {
                new { name="version", args = Array.Empty<object>() },
                new { name="discover", args = Array.Empty<object>() },
                new { name="list-datasets", args = Array.Empty<object>() },
                new { name="get-bars", args = new[]{
                    new { name="symbol", required=true },
                    new { name="from", required=true, type="yyyy-MM-dd" },
                    new { name="to",   required=true, type="yyyy-MM-dd" },
                    new { name="granularity", required=false, @default="1m", oneOf=new[]{"1m","5m","1d"} },
                    new { name="format", required=false, @default="json", oneOf=new[]{"json","jsonl"} }
                }},
                new { name="get-options", args = new[]{
                    new { name="symbol", required=true },
                    new { name="date", required=true, type="yyyy-MM-dd" }
                }}
            }
        }
    });

    public string Version() => J(new { schema=_schema, ok=true, data=new{ service="stroll.history", version=_version }});

    public string Datasets(DataCatalog catalog) => J(new
    {
        schema = _schema, ok = true,
        data = new {
            datasets = catalog.Datasets.Select(d => new { d.Name, d.Kind, d.Path, d.Granularity })
        },
        meta = new { count = catalog.Datasets.Count, root = catalog.Root }
    });

    public string BarsRaw(string symbol, DateOnly from, DateOnly to, Granularity g, IReadOnlyList<IDictionary<string, object?>> rows) => J(new
    {
        schema = _schema, ok = true,
        data = new {
            symbol,
            granularity = g.Canon(),
            from = from.ToString("yyyy-MM-dd"),
            to = to.ToString("yyyy-MM-dd"),
            bars = rows
        },
        meta = new { count = rows.Count }
    });

    public string OptionsChainRaw(string symbol, DateOnly expiry, IReadOnlyList<IDictionary<string, object?>> rows) => J(new
    {
        schema = _schema, ok = true,
        data = new {
            symbol,
            expiry = expiry.ToString("yyyy-MM-dd"),
            chain = rows
        },
        meta = new { count = rows.Count }
    });

    public static void StreamBarsHeader(IPackager pack, string symbol, DateOnly from, DateOnly to, Granularity g, int countHint)
    {
        Console.WriteLine(J(new { schema="stroll.history.v1", ok=true, type="bars-header",
            data=new { symbol, granularity=g.Canon(), from=from.ToString("yyyy-MM-dd"), to=to.ToString("yyyy-MM-dd") },
            meta=new { countHint } }));
    }
    public static void StreamBarsRowRaw(IDictionary<string, object?> row)
        => Console.WriteLine(J(new { schema="stroll.history.v1", ok=true, type="bar", data=row }));
    public static void StreamBarsFooter()
        => Console.WriteLine(J(new { schema="stroll.history.v1", ok=true, type="bars-footer" }));

    public static string Error(string schema, string code, string message, string? hint)
        => J(new { schema, ok=false, error=new { code, message, hint }});
}
