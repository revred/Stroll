#if ODTE_REAL
using System.Data;
using Microsoft.Data.Sqlite;
using Parquet;
using Parquet.Data;
using YamlDotNet.RepresentationModel;

namespace Stroll.Storage;

public sealed class OdteStorage : IStorageProvider
{
    public DataCatalog Catalog { get; }
    private readonly ColumnHints _hints;

    public OdteStorage(DataCatalog catalog)
    {
        Catalog = catalog;
        _hints = ColumnHints.Load(Path.Combine(AppContext.BaseDirectory, "configs", "column_hints.yml"));
    }

    public async Task<IReadOnlyList<IDictionary<string, object?>>> GetBarsRawAsync(string symbol, DateOnly from, DateOnly to, Granularity g)
    {
        var ds = Catalog.Datasets.FirstOrDefault(d => d.Kind=="bars" && d.Path.Contains(symbol, StringComparison.OrdinalIgnoreCase) && d.Granularity == g.Canon())
                 ?? Catalog.Datasets.First(d => d.Kind=="bars" && d.Granularity == g.Canon());

        using var fs = File.OpenRead(ds.Path);
        using var reader = await ParquetReader.CreateAsync(fs);
        var timeKeys = _hints.BarsTimeCandidates;
        var list = new List<IDictionary<string, object?>>();

        for (int rg = 0; rg < reader.RowGroupCount; rg++)
        {
            using var rgReader = reader.OpenRowGroupReader(rg);
            var fields = reader.Schema.Fields.OfType<DataField>().ToArray();
            var cols = new DataColumn[fields.Length];
            for (int i=0;i<fields.Length;i++) cols[i] = await rgReader.ReadColumnAsync(fields[i]);

            int rows = cols[0].Data.Length;
            for (int r = 0; r < rows; r++)
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < fields.Length; c++) dict[fields[c].Name] = cols[c].Data.GetValue(r);

                DateTime? t = ColumnHints.ExtractTime(dict, timeKeys);
                if (t is null) continue;
                var d = DateOnly.FromDateTime(DateTime.SpecifyKind(t.Value, DateTimeKind.Utc));
                if (d < from || d > to) continue;

                list.Add(dict);
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<IDictionary<string, object?>>> GetOptionsChainRawAsync(string symbol, DateOnly expiry)
    {
        var ds = Catalog.Datasets.First(d => d.Kind=="options");
        using var conn = new SqliteConnection($"Data Source={ds.Path};Mode=ReadOnly");
        await conn.OpenAsync();

        // Discover table
        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) tables.Add(rdr.GetString(0));
        }

        string? table = null;
        foreach (var t in tables)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({t})";
            var cols = new List<string>();
            using (var rdr = await cmd.ExecuteReaderAsync())
                while (await rdr.ReadAsync()) cols.Add(rdr.GetString(1).ToLowerInvariant());

            if (cols.Contains(_hints.SymbolCol) && _hints.ExpiryCandidates.Any(ec => cols.Contains(ec)))
            { table = t; break; }
        }
        if (table is null) throw new Exception("options table with symbol+expiry not found");

        var expiryCol = await PickExpiryColumn(conn, table!, _hints);
        var sql = $"SELECT * FROM {table} WHERE {Quote(_hints.SymbolCol)} = $sym AND {Quote(expiryCol)} = $exp";
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$sym", symbol);
            cmd.Parameters.AddWithValue("$exp", expiry.ToString("yyyy-MM-dd"));
            using var rdr = await cmd.ExecuteReaderAsync();

            var res = new List<IDictionary<string, object?>>();
            var schema = Enumerable.Range(0, rdr.FieldCount).Select(i => rdr.GetName(i)).ToArray();
            while (await rdr.ReadAsync())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int i=0;i<schema.Length;i++) row[schema[i]] = rdr.IsDBNull(i) ? null : rdr.GetValue(i);
                res.add(row);
            }
            return res;
        }
    }

    private static async Task<string> PickExpiryColumn(SqliteConnection conn, string table, ColumnHints hints)
    {
        foreach (var c in hints.ExpiryCandidates)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var name = rdr.GetString(1).ToLowerInvariant();
                if (name == c) return c;
            }
        }
        return hints.ExpiryCandidates.First();
    }

    private static string Quote(string id) => """ + id.Replace(""","""") + """;
}

file static class ColumnHints
{
    public string SymbolCol { get; init; } = "symbol";
    public string[] ExpiryCandidates { get; init; } = new[] { "expiry","expiration","exp_date" };
    public string[] BarsTimeCandidates { get; init; } = new[] { "t","time","timestamp","ts","datetime" };

    public static ColumnHints Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new ColumnHints();
            var yaml = File.ReadAllText(path);
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            var root = (YamlMappingNode)stream.Documents[0].RootNode;

            var hints = new ColumnHints();
            if (root.Children.TryGetValue(new YamlScalarNode("symbol"), out var symNode))
                hints.SymbolCol = symNode.ToString();
            if (root.Children.TryGetValue(new YamlScalarNode("expiry"), out var expNode))
                hints.ExpiryCandidates = AsSequenceStrings(expNode);
            if (root.Children.TryGetValue(new YamlScalarNode("bars_time"), out var timeNode))
                hints.BarsTimeCandidates = AsSequenceStrings(timeNode);
            return hints;
        }
        catch { return new ColumnHints(); }
    }

    public static DateTime? ExtractTime(IDictionary<string,object?> row, string[] candidates)
    {
        foreach (var k in candidates)
        {
            if (!row.TryGetValue(k, out var v) || v is null) continue;
            if (v is DateTime dt) return dt;
            if (DateTime.TryParse(v.ToString(), out var parsed)) return parsed;
            if (long.TryParse(v.ToString(), out var unix))
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime; } catch {}
            }
        }
        return null;
    }

    private static string[] AsSequenceStrings(YamlNode node)
        => node is YamlSequenceNode seq ? seq.Children.Select(c => c.ToString()).ToArray() : Array.Empty<string>();
}
#endif
