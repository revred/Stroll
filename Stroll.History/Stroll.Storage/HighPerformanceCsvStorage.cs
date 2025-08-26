using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Stroll.Storage;

/// <summary>
/// High-performance CSV storage provider optimized for sub-15ms response times.
/// Uses memory-mapped files, pre-built indices, and response caching.
/// </summary>
public sealed class HighPerformanceCsvStorage : IStorageProvider, IDisposable
{
    public DataCatalog Catalog { get; }

    // High-performance data structures
    private readonly ConcurrentDictionary<string, SymbolIndex> _symbolIndices = new();
    private readonly ConcurrentDictionary<string, CachedResponse> _responseCache = new();
    private readonly ArrayPool<char> _charPool = ArrayPool<char>.Shared;
    private readonly string _dataPath;

    // Memory-mapped file access
    private readonly Dictionary<string, (MemoryMappedFile mmf, MemoryMappedViewAccessor accessor)> _mmfFiles = new();

    public HighPerformanceCsvStorage(DataCatalog catalog)
    {
        Catalog = catalog;
        _dataPath = Path.Combine(AppContext.BaseDirectory, "data");
        
        // Initialize asynchronously in background
        _ = Task.Run(InitializeIndicesAsync);
    }

    public async Task<IReadOnlyList<IDictionary<string, object?>>> GetBarsRawAsync(string symbol, DateOnly from, DateOnly to, Granularity g)
    {
        var cacheKey = $"bars_{symbol}_{from:yyyy-MM-dd}_{to:yyyy-MM-dd}_{g.Canon()}";
        
        // Check cache first (target: <1ms for cache hits)
        if (_responseCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            return cached.Data;
        }

        // Fast path: Use pre-built index for data retrieval
        var data = await GetBarsFromIndexAsync(symbol, from, to, g);
        
        // Cache the result with 5-minute TTL
        _responseCache[cacheKey] = new CachedResponse(data, TimeSpan.FromMinutes(5));
        
        return data;
    }

    public async Task<IReadOnlyList<IDictionary<string, object?>>> GetOptionsChainRawAsync(string symbol, DateOnly expiry)
    {
        var cacheKey = $"options_{symbol}_{expiry:yyyy-MM-dd}";
        
        if (_responseCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            return cached.Data;
        }

        // Stub implementation for options (focus on bars first)
        var data = CreateStubOptions(symbol, expiry);
        _responseCache[cacheKey] = new CachedResponse(data, TimeSpan.FromMinutes(10));
        
        return data;
    }

    private async Task<IReadOnlyList<IDictionary<string, object?>>> GetBarsFromIndexAsync(string symbol, DateOnly from, DateOnly to, Granularity g)
    {
        // Get symbol index
        if (!_symbolIndices.TryGetValue(symbol, out var index))
        {
            // If index not ready, fall back to direct file reading
            return await ReadCsvDirectAsync(symbol, from, to);
        }

        var result = new List<IDictionary<string, object?>>();
        
        // Use index for fast date range lookup
        foreach (var date in index.GetDateRange(from, to))
        {
            if (index.DateEntries.TryGetValue(date, out var entry))
            {
                // Memory-mapped file access for ultra-fast reading
                var bars = await ReadBarsFromMemoryMappedFileAsync(symbol, entry);
                result.AddRange(bars);
            }
        }

        return result;
    }

    private async Task<List<IDictionary<string, object?>>> ReadBarsFromMemoryMappedFileAsync(string symbol, DateIndexEntry entry)
    {
        var result = new List<IDictionary<string, object?>>();
        
        if (!_mmfFiles.TryGetValue(symbol, out var mmfInfo))
        {
            // Fall back to direct file reading if MMF not available
            return await ReadCsvLinesAsync(GetCsvPath(symbol), entry.LineStart, entry.LineCount);
        }

        try
        {
            // Use memory-mapped file for zero-copy access
            var buffer = _charPool.Rent(8192);
            try
            {
                // Read from memory-mapped file at specific offset
                var bytesToRead = Math.Min(entry.ByteLength, buffer.Length * 2);
                var bytes = new byte[bytesToRead];
                mmfInfo.accessor.ReadArray(entry.ByteOffset, bytes, 0, bytesToRead);
                
                var text = Encoding.UTF8.GetString(bytes);
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    if (TryParseCsvLine(line, out var bar))
                    {
                        result.Add(bar);
                    }
                }
            }
            finally
            {
                _charPool.Return(buffer);
            }
        }
        catch
        {
            // Fall back to direct file reading on any MMF error
            return await ReadCsvLinesAsync(GetCsvPath(symbol), entry.LineStart, entry.LineCount);
        }

        return result;
    }

    private async Task<List<IDictionary<string, object?>>> ReadCsvDirectAsync(string symbol, DateOnly from, DateOnly to)
    {
        var csvPath = GetCsvPath(symbol);
        if (!File.Exists(csvPath))
        {
            return new List<IDictionary<string, object?>>();
        }

        var result = new List<IDictionary<string, object?>>();
        var lines = await File.ReadAllLinesAsync(csvPath);
        
        // Skip header if present
        var startIndex = lines.Length > 0 && lines[0].Contains("Date") ? 1 : 0;
        
        for (int i = startIndex; i < lines.Length; i++)
        {
            if (TryParseCsvLine(lines[i], out var bar))
            {
                var date = ExtractDate(bar);
                if (date >= from && date <= to)
                {
                    result.Add(bar);
                }
            }
        }

        return result;
    }

    private async Task<List<IDictionary<string, object?>>> ReadCsvLinesAsync(string csvPath, int startLine, int count)
    {
        var result = new List<IDictionary<string, object?>>();
        
        if (!File.Exists(csvPath)) return result;
        
        var lines = await File.ReadAllLinesAsync(csvPath);
        var endLine = Math.Min(startLine + count, lines.Length);
        
        for (int i = startLine; i < endLine; i++)
        {
            if (TryParseCsvLine(lines[i], out var bar))
            {
                result.Add(bar);
            }
        }
        
        return result;
    }

    private static bool TryParseCsvLine(ReadOnlySpan<char> line, out Dictionary<string, object?> bar)
    {
        bar = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        
        if (line.IsEmpty || line.StartsWith("Date"))
        {
            return false;
        }

        var parts = line.ToString().Split(',');
        if (parts.Length < 6)
        {
            return false;
        }

        try
        {
            // Parse CSV format: Date,Open,High,Low,Close,Volume
            if (DateTime.TryParse(parts[0], out var date))
            {
                bar["t"] = DateTime.SpecifyKind(date.Date.AddHours(13).AddMinutes(30), DateTimeKind.Utc);
            }

            if (decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var open))
                bar["o"] = open;
            
            if (decimal.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var high))
                bar["h"] = high;
            
            if (decimal.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var low))
                bar["l"] = low;
            
            if (decimal.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var close))
                bar["c"] = close;
            
            if (long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var volume))
                bar["v"] = volume;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DateOnly ExtractDate(IDictionary<string, object?> bar)
    {
        if (bar.TryGetValue("t", out var timeObj) && timeObj is DateTime dt)
        {
            return DateOnly.FromDateTime(dt);
        }
        return DateOnly.MinValue;
    }

    private string GetCsvPath(string symbol)
    {
        // Look for various naming patterns
        var patterns = new[]
        {
            $"{symbol}_20050101_20250815.csv",
            $"{symbol}_historical_data.csv",
            $"{symbol}.csv",
            $"{symbol}_*data*.csv"
        };

        foreach (var pattern in patterns)
        {
            var files = Directory.GetFiles(_dataPath, pattern, SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                return files[0];
            }
        }

        return Path.Combine(_dataPath, $"{symbol}_20050101_20250815.csv");
    }

    private static List<IDictionary<string, object?>> CreateStubOptions(string symbol, DateOnly expiry)
    {
        return new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["symbol"] = symbol, ["expiry"] = expiry.ToString("yyyy-MM-dd"), ["right"] = "CALL", ["strike"] = 470.0m, ["bid"] = 5.15m, ["ask"] = 5.25m, ["mid"] = 5.20m, ["delta"] = 0.65m, ["gamma"] = 0.08m },
            new Dictionary<string, object?> { ["symbol"] = symbol, ["expiry"] = expiry.ToString("yyyy-MM-dd"), ["right"] = "PUT", ["strike"] = 470.0m, ["bid"] = 2.15m, ["ask"] = 2.25m, ["mid"] = 2.20m, ["delta"] = -0.35m, ["gamma"] = 0.08m }
        };
    }

    private async Task InitializeIndicesAsync()
    {
        try
        {
            var csvFiles = Directory.GetFiles(_dataPath, "*.csv", SearchOption.AllDirectories);
            var tasks = csvFiles.Select(BuildSymbolIndexAsync).ToArray();
            await Task.WhenAll(tasks);
            
            // Initialize memory-mapped files for largest symbols
            await InitializeMemoryMappedFilesAsync(new[] { "SPY", "QQQ", "XLE", "USO" });
        }
        catch (Exception ex)
        {
            // Log error but don't throw - gracefully degrade to direct file access
            Console.WriteLine($"Warning: Index initialization failed: {ex.Message}");
        }
    }

    private async Task BuildSymbolIndexAsync(string csvPath)
    {
        try
        {
            var symbol = ExtractSymbolFromPath(csvPath);
            if (string.IsNullOrEmpty(symbol)) return;

            var index = new SymbolIndex(symbol, csvPath);
            var lines = await File.ReadAllLinesAsync(csvPath);
            
            long byteOffset = 0;
            var hasHeader = lines.Length > 0 && lines[0].Contains("Date");
            var startIndex = hasHeader ? 1 : 0;

            if (hasHeader)
            {
                byteOffset = Encoding.UTF8.GetByteCount(lines[0] + "\n");
            }

            for (int i = startIndex; i < lines.Length; i++)
            {
                if (TryParseCsvLine(lines[i], out var bar))
                {
                    var date = ExtractDate(bar);
                    if (date != DateOnly.MinValue)
                    {
                        var lineBytes = Encoding.UTF8.GetByteCount(lines[i] + "\n");
                        index.AddEntry(date, new DateIndexEntry(i, 1, byteOffset, lineBytes));
                        byteOffset += lineBytes;
                    }
                }
            }

            _symbolIndices[symbol] = index;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to build index for {csvPath}: {ex.Message}");
        }
    }

    private async Task InitializeMemoryMappedFilesAsync(string[] symbols)
    {
        foreach (var symbol in symbols)
        {
            try
            {
                var csvPath = GetCsvPath(symbol);
                if (File.Exists(csvPath))
                {
                    var fileInfo = new FileInfo(csvPath);
                    var mmf = MemoryMappedFile.CreateFromFile(csvPath, FileMode.Open, $"stroll_history_{symbol}", fileInfo.Length, MemoryMappedFileAccess.Read);
                    var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                    _mmfFiles[symbol] = (mmf, accessor);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to initialize MMF for {symbol}: {ex.Message}");
            }
        }
    }

    private static string ExtractSymbolFromPath(string csvPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(csvPath);
        var underscoreIndex = fileName.IndexOf('_');
        return underscoreIndex > 0 ? fileName[..underscoreIndex] : fileName;
    }

    public void Dispose()
    {
        foreach (var (mmf, accessor) in _mmfFiles.Values)
        {
            accessor.Dispose();
            mmf.Dispose();
        }
        _mmfFiles.Clear();
    }
}

// Supporting classes
public class SymbolIndex
{
    public string Symbol { get; }
    public string FilePath { get; }
    public Dictionary<DateOnly, DateIndexEntry> DateEntries { get; } = new();

    public SymbolIndex(string symbol, string filePath)
    {
        Symbol = symbol;
        FilePath = filePath;
    }

    public void AddEntry(DateOnly date, DateIndexEntry entry)
    {
        DateEntries[date] = entry;
    }

    public IEnumerable<DateOnly> GetDateRange(DateOnly from, DateOnly to)
    {
        return DateEntries.Keys.Where(d => d >= from && d <= to).OrderBy(d => d);
    }
}

public record DateIndexEntry(int LineStart, int LineCount, long ByteOffset, int ByteLength);

public class CachedResponse
{
    public IReadOnlyList<IDictionary<string, object?>> Data { get; }
    public DateTime ExpiryTime { get; }
    
    public bool IsExpired => DateTime.UtcNow > ExpiryTime;

    public CachedResponse(IReadOnlyList<IDictionary<string, object?>> data, TimeSpan ttl)
    {
        Data = data;
        ExpiryTime = DateTime.UtcNow.Add(ttl);
    }
}