using Stroll.Dataset;
using Stroll.Storage;
using Stroll.Historical;
using Microsoft.Extensions.Logging;

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

            case "acquire-data":
            {
                var symbol = Req(kv, "symbol");
                var from = DateTime.Parse(Req(kv, "from"));
                var to = DateTime.Parse(Req(kv, "to"));
                var interval = Opt(kv, "interval", "1d");
                var outputPath = Opt(kv, "output", Path.Combine(AppContext.BaseDirectory, "data"));

                Console.WriteLine($"Starting data acquisition for {symbol} from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}");
                
                using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var logger = loggerFactory.CreateLogger<DataAcquisitionEngine>();
                
                using var engine = new DataAcquisitionEngine(outputPath, logger);
                var result = await engine.AcquireDataAsync(symbol, from, to, interval);
                
                if (result.Success)
                {
                    Console.WriteLine($"✅ Data acquisition completed successfully:");
                    Console.WriteLine($"   - Bars acquired: {result.BarsAcquired}");
                    Console.WriteLine($"   - Duration: {result.Duration.TotalSeconds:F1}s");
                    Console.WriteLine($"   - Providers used: {string.Join(", ", result.SuccessfulProviders)}");
                }
                else
                {
                    Console.WriteLine($"❌ Data acquisition failed: {result.ErrorMessage}");
                    if (result.FailedProviders.Any())
                    {
                        Console.WriteLine($"   Failed providers: {string.Join(", ", result.FailedProviders)}");
                    }
                }
                return;
            }

            case "provider-status":
            {
                var outputPath = Opt(kv, "output", Path.Combine(AppContext.BaseDirectory, "data"));
                
                using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var logger = loggerFactory.CreateLogger<DataAcquisitionEngine>();
                
                using var engine = new DataAcquisitionEngine(outputPath, logger);
                var statuses = await engine.GetProviderStatusAsync();
                
                Console.WriteLine("📊 Data Provider Status:");
                Console.WriteLine();
                
                foreach (var status in statuses.OrderBy(s => s.Priority))
                {
                    var healthIcon = status.IsHealthy ? "✅" : "❌";
                    var availableIcon = status.IsAvailable ? "🟢" : "🔴";
                    
                    Console.WriteLine($"{healthIcon} {availableIcon} {status.Name} (Priority: {status.Priority})");
                    Console.WriteLine($"   Available: {status.IsAvailable}");
                    Console.WriteLine($"   Healthy: {status.IsHealthy}");
                    Console.WriteLine($"   Response Time: {status.ResponseTimeMs:F0}ms");
                    Console.WriteLine($"   Rate Limit: {status.RequestsRemaining}/{status.RequestsPerMinute} per minute");
                    
                    if (status.IsThrottled)
                        Console.WriteLine($"   ⚠️  Currently throttled");
                    
                    if (!string.IsNullOrEmpty(status.ErrorMessage))
                        Console.WriteLine($"   Error: {status.ErrorMessage}");
                    
                    Console.WriteLine();
                }
                return;
            }

            case "migrate-to-sqlite":
            {
                var dataPath = Opt(kv, "data-path", "./data");
                if (!Directory.Exists(dataPath))
                {
                    throw new DataException($"Data directory not found: {dataPath}", "Specify correct --data-path");
                }

                Console.WriteLine("🔄 Starting CSV to SQLite migration...");
                
                // CompositeStorage always uses SqliteStorage now
                if (storage is CompositeStorage composite)
                {
                    // Create a new SqliteStorage for migration
                    using var sqliteStorage = new SqliteStorage(catalog);
                    var migrator = new CsvToSqliteMigrator(sqliteStorage);
                    await migrator.MigrateAllCsvFilesAsync(dataPath);
                    
                    var stats = sqliteStorage.GetDatabaseStats();
                    Console.WriteLine($"✅ Migration completed!");
                    Console.WriteLine($"Total bars migrated: {stats["total_bars"]}");
                    Console.WriteLine($"Database size: {stats["database_size_mb"]:F2} MB");
                }
                else
                {
                    throw new DataException("Migration command not supported for this storage type", "Expected CompositeStorage");
                }
                return;
            }

            case "mcp-health":
            {
                Console.WriteLine("🏥 Checking MCP service health...");
                
                // Start the MCP service process
                var mcpExePath = Path.Combine(AppContext.BaseDirectory, "..", "Stroll.History.Mcp", "bin", "Debug", "net9.0", "Stroll.History.Mcp.exe");
                if (!File.Exists(mcpExePath))
                {
                    Console.WriteLine($"❌ MCP service executable not found at: {mcpExePath}");
                    Environment.Exit(1);
                }

                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = mcpExePath;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                
                process.Start();
                
                // Send health check request
                var healthRequest = @"{""jsonrpc"": ""2.0"", ""id"": 1, ""method"": ""tools/call"", ""params"": {""name"": ""provider_status"", ""arguments"": {}}}";
                await process.StandardInput.WriteLineAsync(healthRequest);
                
                // Read response with timeout
                var responseTask = process.StandardOutput.ReadLineAsync();
                var timeoutTask = Task.Delay(5000);
                
                var completedTask = await Task.WhenAny(responseTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    Console.WriteLine("❌ MCP service health check timed out");
                    process.Kill();
                    Environment.Exit(1);
                }
                
                var response = await responseTask;
                if (string.IsNullOrEmpty(response))
                {
                    Console.WriteLine("❌ MCP service returned empty response");
                    process.Kill();
                    Environment.Exit(1);
                }
                
                Console.WriteLine("✅ MCP service is healthy");
                Console.WriteLine($"Response: {response[..Math.Min(200, response.Length)]}...");
                
                process.Kill();
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
