namespace Stroll.Storage;

public sealed class CompositeStorage : IStorageProvider
{
    public DataCatalog Catalog { get; }

#if ODTE_REAL
    private readonly IStorageProvider _impl;
#endif

    public CompositeStorage(DataCatalog catalog)
    {
        Catalog = catalog;
#if ODTE_REAL
        _impl = new OdteStorage(catalog);
#endif
    }

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetBarsRawAsync(string symbol, DateOnly from, DateOnly to, Granularity g)
    {
#if ODTE_REAL
        return _impl.GetBarsRawAsync(symbol, from, to, g);
#else
        return Task.FromResult<IReadOnlyList<IDictionary<string, object?>>>(StubBars(symbol, from, to, g));
#endif
    }

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetOptionsChainRawAsync(string symbol, DateOnly expiry)
    {
#if ODTE_REAL
        return _impl.GetOptionsChainRawAsync(symbol, expiry);
#else
        return Task.FromResult<IReadOnlyList<IDictionary<string, object?>>>(StubOptions(symbol, expiry));
#endif
    }

#if !ODTE_REAL
    private static IReadOnlyList<IDictionary<string, object?>> StubBars(string symbol, DateOnly from, DateOnly to, Granularity g)
    {
        var t0 = DateTime.SpecifyKind(DateTime.Parse(f"{from:yyyy-MM-dd} 13:30:00"), DateTimeKind.Utc)
        ;
        return new List<Dictionary<string, object?>>
        {
            new() { ["t"]=t0, ["o"]=520.12m, ["h"]=521.00m, ["l"]=519.85m, ["c"]=520.55m, ["v"]=120_345L, ["symbol"]=symbol, ["g"]=g.Canon() },
            new() { ["t"]=t0.AddMinutes(1), ["o"]=520.55m, ["h"]=521.20m, ["l"]=520.40m, ["c"]=521.10m, ["v"]=90_321L,  ["symbol"]=symbol, ["g"]=g.Canon() }
        };
    }

    private static IReadOnlyList<IDictionary<string, object?>> StubOptions(string symbol, DateOnly expiry)
    {
        return new List<Dictionary<string, object?>>
        {
            new() { ["symbol"]=symbol, ["expiry"]=expiry.ToString("yyyy-MM-dd"), ["right"]="PUT",  ["strike"]=500m, ["bid"]=3.20m, ["ask"]=3.30m, ["mid"]=3.25m, ["delta"]=-0.45m, ["gamma"]=0.08m },
            new() { ["symbol"]=symbol, ["expiry"]=expiry.ToString("yyyy-MM-dd"), ["right"]="PUT",  ["strike"]=505m, ["bid"]=3.60m, ["ask"]=3.75m, ["mid"]=3.68m, ["delta"]=-0.47m, ["gamma"]=0.09m },
            new() { ["symbol"]=symbol, ["expiry"]=expiry.ToString("yyyy-MM-dd"), ["right"]="CALL", ["strike"]=540m, ["bid"]=2.10m, ["ask"]=2.25m, ["mid"]=2.18m, ["delta"]= 0.35m, ["gamma"]=0.07m }
        };
    }
#endif
}
