using Stroll.Dataset;
using Stroll.Storage;

public static class Cli
{
    public sealed class UsageException(string message, string hint) : Exception(message) { public string Hint { get; } = hint; }
    public sealed class DataException(string message, string hint) : Exception(message) { public string Hint { get; } = hint; }

    public static async Task RunAsync(string[] args, IStorageProvider storage, DataCatalog catalog, IPackager pack)
    {
        if (args.Length == 0) throw new UsageException("no command", "try: stroll.history discover");

        var cmd = args[0].ToLowerInvariant();
        var kv  = ParseKv(args);

        switch (cmd)
        {
            case "discover":
                Console.WriteLine(pack.Discover());
                return;

            case "version":
                Console.WriteLine(pack.Version());
                return;

            case "list-datasets":
                Console.WriteLine(pack.Datasets(catalog));
                return;

            case "get-bars":
            {
                var symbol = Req(kv, "symbol");
                var from   = DateOnly.Parse(Req(kv, "from"));
                var to     = DateOnly.Parse(Req(kv, "to"));
                var g      = GranularityExtensions.Parse(Opt(kv, "granularity", "1m"));
                var fmt    = Opt(kv, "format", "json");

                var rows = await storage.GetBarsRawAsync(symbol, from, to, g);

                if (fmt.Equals("jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    JsonPackager.StreamBarsHeader(pack, symbol, from, to, g, rows.Count);
                    foreach (var r in rows) JsonPackager.StreamBarsRowRaw(r);
                    JsonPackager.StreamBarsFooter();
                }
                else
                {
                    Console.WriteLine(pack.BarsRaw(symbol, from, to, g, rows));
                }
                return;
            }

            case "get-options":
            {
                var symbol = Req(kv, "symbol");
                var date   = DateOnly.Parse(Req(kv, "date"));
                var rows   = await storage.GetOptionsChainRawAsync(symbol, date);
                Console.WriteLine(pack.OptionsChainRaw(symbol, date, rows));
                return;
            }

            default:
                throw new UsageException($"unknown command '{cmd}'", "try: stroll.history discover");
        }
    }

    static Dictionary<string,string> ParseKv(string[] args)
    {
        var m = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;
            if (i + 1 >= args.Length) throw new UsageException($"missing value for {a}", "provide a value after the flag");
            m[a[2..]] = args[++i];
        }
        return m;
    }
    static string Req(Dictionary<string,string> kv, string key)
        => kv.TryGetValue(key, out var v) ? v : throw new UsageException($"missing --{key}", $"add --{key} <value>");
    static string Opt(Dictionary<string,string> kv, string key, string def)
        => kv.TryGetValue(key, out var v) ? v : def;
}
