using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stroll.Storage;

namespace Stroll.Dataset;

/// <summary>
/// High-performance JSON packager optimized for sub-5ms serialization.
/// Uses pre-allocated buffers, minimal formatting, and cached responses.
/// </summary>
public sealed class HighPerformancePackager : IPackager
{
    private readonly string _schema;
    private readonly string _version;
    private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    
    // Pre-cached common responses
    private readonly Lazy<string> _discoverResponse;
    private readonly Lazy<string> _versionResponse;

    // Optimized JSON serialization options
    private static readonly JsonSerializerOptions FastJsonOptions = new()
    {
        PropertyNamingPolicy = null, // preserve original keys  
        NumberHandling = JsonNumberHandling.Strict,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false, // Critical: no indentation for minimal size
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Faster encoding
    };

    // Pre-compiled JSON strings for maximum performance
    private readonly string _schemaOkPrefix;
    private readonly string _metaTemplate;

    public HighPerformancePackager(string schemaVersion, string serviceVersion)
    {
        _schema = schemaVersion;
        _version = serviceVersion;
        
        // Pre-compile common JSON fragments
        _schemaOkPrefix = $"{{\"schema\":\"{_schema}\",\"ok\":true,\"data\":";
        _metaTemplate = ",\"meta\":{{\"count\":{0},\"timestamp\":\"{1}\"}}}}";
        
        // Pre-cache static responses
        _discoverResponse = new Lazy<string>(BuildDiscoverResponse);
        _versionResponse = new Lazy<string>(BuildVersionResponse);
    }

    public string Discover() => _discoverResponse.Value;
    
    public string Version() => _versionResponse.Value;

    public string Datasets(DataCatalog catalog)
    {
        var datasets = catalog.Datasets.Select(d => new
        {
            path = d.Path,
            kind = d.Kind,
            granularity = d.Granularity,
            name = d.Name
        });

        return BuildResponse(new { datasets });
    }

    public string BarsRaw(string symbol, DateOnly from, DateOnly to, Granularity g, IReadOnlyList<IDictionary<string, object?>> rows)
    {
        // Fast path: Use string concatenation for maximum performance
        var sb = new StringBuilder(capacity: rows.Count * 200 + 500); // Pre-allocate based on expected size
        
        sb.Append(_schemaOkPrefix);
        
        // Build bars array manually for maximum speed
        sb.Append("{\"symbol\":\"").Append(symbol)
          .Append("\",\"granularity\":\"").Append(g.Canon())
          .Append("\",\"from\":\"").Append(from.ToString("yyyy-MM-dd"))
          .Append("\",\"to\":\"").Append(to.ToString("yyyy-MM-dd"))
          .Append("\",\"bars\":[");

        var first = true;
        foreach (var row in rows)
        {
            if (!first) sb.Append(',');
            first = false;
            
            AppendBarJson(sb, row, symbol, g.Canon());
        }
        
        sb.Append("]}");
        
        // Add metadata
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        sb.AppendFormat(_metaTemplate, rows.Count, timestamp);
        
        return sb.ToString();
    }

    public string OptionsChainRaw(string symbol, DateOnly expiry, IReadOnlyList<IDictionary<string, object?>> rows)
    {
        var data = new
        {
            symbol,
            expiry = expiry.ToString("yyyy-MM-dd"),
            chain = rows.Select(row => new
            {
                symbol = GetValue<string>(row, "symbol", symbol),
                expiry = GetValue<string>(row, "expiry", expiry.ToString("yyyy-MM-dd")),
                right = GetValue<string>(row, "right", "CALL"),
                strike = GetValue<decimal>(row, "strike", 0m),
                bid = GetValue<decimal>(row, "bid", 0m),
                ask = GetValue<decimal>(row, "ask", 0m),
                mid = GetValue<decimal>(row, "mid", 0m),
                delta = GetValue<decimal>(row, "delta", 0m),
                gamma = GetValue<decimal>(row, "gamma", 0m)
            })
        };

        return BuildResponseWithMeta(data, rows.Count);
    }

    private string BuildResponse(object data)
    {
        return _schemaOkPrefix + JsonSerializer.Serialize(data, FastJsonOptions) + "}";
    }

    private string BuildResponseWithMeta(object data, int count)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        return _schemaOkPrefix + JsonSerializer.Serialize(data, FastJsonOptions) + 
               string.Format(_metaTemplate, count, timestamp);
    }

    private static void AppendBarJson(StringBuilder sb, IDictionary<string, object?> row, string symbol, string granularity)
    {
        sb.Append("{\"t\":\"");
        
        // Format timestamp efficiently
        if (row.TryGetValue("t", out var timeObj) && timeObj is DateTime dt)
        {
            sb.Append(dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        }
        
        sb.Append("\",\"o\":").Append(GetValue<decimal>(row, "o", 0m))
          .Append(",\"h\":").Append(GetValue<decimal>(row, "h", 0m))
          .Append(",\"l\":").Append(GetValue<decimal>(row, "l", 0m))
          .Append(",\"c\":").Append(GetValue<decimal>(row, "c", 0m))
          .Append(",\"v\":").Append(GetValue<long>(row, "v", 0L))
          .Append(",\"symbol\":\"").Append(symbol)
          .Append("\",\"g\":\"").Append(granularity)
          .Append("\"}");
    }

    private static T GetValue<T>(IDictionary<string, object?> row, string key, T defaultValue)
    {
        if (row.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        
        // Try type conversion for common cases
        if (value != null)
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                // Fall back to default value
            }
        }
        
        return defaultValue;
    }

    private string BuildDiscoverResponse()
    {
        var response = new
        {
            schema = _schema,
            ok = true,
            data = new
            {
                service = "stroll.history",
                version = _version,
                env = new[]{ new { name="STROLL_DATA", desc="dataset root override", required=false } },
                commands = new object[] {
                    new { name = "version", args = Array.Empty<object>() },
                    new { name = "discover", args = Array.Empty<object>() },
                    new { name = "list-datasets", args = Array.Empty<object>() },
                    new { name = "get-bars", args = new object[]{
                        new { name = "symbol", required = true },
                        new { name = "from", required = true, type = "yyyy-MM-dd" },
                        new { name = "to", required = true, type = "yyyy-MM-dd" },
                        new { name = "granularity", required = false, @default = "1m", oneOf = new[]{"1m","5m","1d"} },
                        new { name = "format", required = false, @default = "json", oneOf = new[]{"json","jsonl"} }
                    }},
                    new { name = "get-options", args = new object[]{
                        new { name = "symbol", required = true },
                        new { name = "date", required = true, type = "yyyy-MM-dd" }
                    }},
                    new { name = "acquire-data", args = new object[]{
                        new { name = "symbol", required = true },
                        new { name = "from", required = true, type = "yyyy-MM-dd" },
                        new { name = "to", required = true, type = "yyyy-MM-dd" },
                        new { name = "interval", required = false, @default = "1d", oneOf = new[]{"1d","1h","30m","15m","5m","1m"} },
                        new { name = "output", required = false, @default = "./data" }
                    }},
                    new { name = "provider-status", args = new object[]{
                        new { name = "output", required = false, @default = "./data" }
                    }}
                }
            }
        };

        return JsonSerializer.Serialize(response, FastJsonOptions);
    }

    private string BuildVersionResponse()
    {
        var response = new
        {
            schema = _schema,
            ok = true,
            data = new
            {
                service = "stroll.history",
                version = _version
            }
        };

        return JsonSerializer.Serialize(response, FastJsonOptions);
    }
}