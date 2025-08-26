namespace Stroll.Storage;

public sealed class CompositeStorage : IStorageProvider
{
    public DataCatalog Catalog { get; }
    private readonly IStorageProvider _impl;

    public CompositeStorage(DataCatalog catalog)
    {
        Catalog = catalog;
        // Always use high-performance SQLite storage - fastest possible access
        _impl = new SqliteStorage(catalog);
    }

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetBarsRawAsync(string symbol, DateOnly from, DateOnly to, Granularity g)
    {
        return _impl.GetBarsRawAsync(symbol, from, to, g);
    }

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetOptionsChainRawAsync(string symbol, DateOnly expiry)
    {
        return _impl.GetOptionsChainRawAsync(symbol, expiry);
    }
}
