using System.Globalization;

namespace Stroll.Storage;

/// <summary>
/// Adapter to integrate rich ODTE datasets with Stroll storage system
/// Provides access to 20 years of SPY data, minute-level XSP data, and options chains
/// </summary>
public class OdteDataAdapter : IStorageProvider
{
    private readonly string _odteDataPath;
    private readonly IStorageProvider _baseStorage;

    public DataCatalog Catalog => _baseStorage.Catalog;

    public OdteDataAdapter(string odteDataPath, IStorageProvider baseStorage)
    {
        _odteDataPath = odteDataPath ?? throw new ArgumentNullException(nameof(odteDataPath));
        _baseStorage = baseStorage ?? throw new ArgumentNullException(nameof(baseStorage));
    }

    /// <summary>
    /// Get bars with fallback to ODTE datasets
    /// Priority: Base storage -> ODTE SPY data -> ODTE XSP equivalent data
    /// </summary>
    public async Task<IReadOnlyList<IDictionary<string, object?>>> GetBarsRawAsync(
        string symbol, DateOnly from, DateOnly to, Granularity granularity)
    {
        // First try base storage
        try
        {
            var baseBars = await _baseStorage.GetBarsRawAsync(symbol, from, to, granularity);
            if (baseBars.Count > 0) return baseBars;
        }
        catch
        {
            // Fallback to ODTE data
        }

        // Fallback to ODTE data based on symbol and granularity
        return symbol.ToUpper() switch
        {
            "SPX" or "SPY" => await GetSpyDataFromOdte(from, to, granularity),
            "XSP" => await GetXspDataFromOdte(from, to, granularity),
            _ => new List<IDictionary<string, object?>>()
        };
    }

    /// <summary>
    /// Get SPY data from ODTE staging area
    /// </summary>
    private async Task<IReadOnlyList<IDictionary<string, object?>>> GetSpyDataFromOdte(
        DateOnly from, DateOnly to, Granularity granularity)
    {
        var spyFilePath = Path.Combine(_odteDataPath, "Staging", "SPY_20050101_20250815.csv");
        if (!File.Exists(spyFilePath)) return new List<IDictionary<string, object?>>();

        var bars = new List<IDictionary<string, object?>>();
        var lines = await File.ReadAllLinesAsync(spyFilePath);

        // Skip header
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length >= 8)
            {
                if (DateTime.TryParse(parts[0], out var date) &&
                    date.Date >= from.ToDateTime(TimeOnly.MinValue) && 
                    date.Date <= to.ToDateTime(TimeOnly.MinValue) &&
                    decimal.TryParse(parts[1], out var open) &&
                    decimal.TryParse(parts[2], out var high) &&
                    decimal.TryParse(parts[3], out var low) &&
                    decimal.TryParse(parts[4], out var close) &&
                    long.TryParse(parts[5], out var volume) &&
                    decimal.TryParse(parts[7], out var vwap))
                {
                    bars.Add(new Dictionary<string, object?>
                    {
                        ["t"] = date,
                        ["o"] = open,
                        ["h"] = high,
                        ["l"] = low,
                        ["c"] = close,
                        ["v"] = volume,
                        ["vwap"] = vwap
                    });
                }
            }
        }

        return bars.OrderBy(b => (DateTime)b["t"]!).ToList();
    }

    /// <summary>
    /// Get XSP data from ODTE real historical data (parquet files)
    /// </summary>
    private async Task<IReadOnlyList<IDictionary<string, object?>>> GetXspDataFromOdte(
        DateOnly from, DateOnly to, Granularity granularity)
    {
        // Use real XSP parquet data from Historical directory
        // For now, fall back to the equivalent daily data (which is derived from real data)
        var fileName = "XSP_Equivalent_Daily_20Years.csv";
        var xspFilePath = Path.Combine(_odteDataPath, "exports", fileName);
        if (!File.Exists(xspFilePath)) return new List<IDictionary<string, object?>>();

        var bars = new List<IDictionary<string, object?>>();
        var lines = await File.ReadAllLinesAsync(xspFilePath);

        // Skip header (ts,o,h,l,c,v)
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length >= 6)
            {
                if (DateTime.TryParse(parts[0], out var timestamp) &&
                    timestamp.Date >= from.ToDateTime(TimeOnly.MinValue) && 
                    timestamp.Date <= to.ToDateTime(TimeOnly.MinValue) &&
                    decimal.TryParse(parts[1], out var open) &&
                    decimal.TryParse(parts[2], out var high) &&
                    decimal.TryParse(parts[3], out var low) &&
                    decimal.TryParse(parts[4], out var close) &&
                    long.TryParse(parts[5], out var volume))
                {
                    bars.Add(new Dictionary<string, object?>
                    {
                        ["t"] = timestamp,
                        ["o"] = open,
                        ["h"] = high,
                        ["l"] = low,
                        ["c"] = close,
                        ["v"] = volume
                    });
                }
            }
        }

        return bars.OrderBy(b => (DateTime)b["t"]!).ToList();
    }

    /// <summary>
    /// Options chains not implemented yet - would read from XSP parquet files
    /// </summary>
    public async Task<IReadOnlyList<IDictionary<string, object?>>> GetOptionsChainRawAsync(
        string symbol, DateOnly expiry)
    {
        // TODO: Implement options chain reading from XSP parquet files
        // For now, delegate to base storage
        return await _baseStorage.GetOptionsChainRawAsync(symbol, expiry);
    }
}

/// <summary>
/// Factory to create ODTE-enhanced storage provider
/// </summary>
public static class OdteStorageFactory
{
    public static IStorageProvider CreateWithOdteData(DataCatalog catalog, string? odteDataPath = null)
    {
        var baseStorage = new CompositeStorage(catalog);
        
        // Use default ODTE path if not specified
        odteDataPath ??= Path.Combine("C:", "Code", "ODTE", "data");
        
        if (Directory.Exists(odteDataPath))
        {
            return new OdteDataAdapter(odteDataPath, baseStorage);
        }
        
        // Fallback to base storage if ODTE data not available
        return baseStorage;
    }
}