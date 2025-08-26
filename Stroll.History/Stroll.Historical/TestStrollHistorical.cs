using System;
using System.Text.Json;

namespace TestStrollHistorical;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                return OutputError(2, "INVALID_ARGUMENTS", "No command specified");
            }

            var command = args[0].ToLowerInvariant();

            switch (command)
            {
                case "version":
                    return OutputVersion();
                
                case "discover":
                    return OutputDiscover();
                
                case "provider-status":
                    return OutputProviderStatus();
                
                case "get-bars":
                    return HandleGetBars(args);
                
                case "get-options":
                    return HandleGetOptions(args);
                
                default:
                    return OutputError(2, "INVALID_ARGUMENTS", $"Unknown command: {command}");
            }
        }
        catch (Exception ex)
        {
            return OutputError(10, "INTERNAL_ERROR", ex.Message);
        }
    }

    static int OutputVersion()
    {
        var response = new
        {
            schema = "stroll.history.v1",
            ok = true,
            data = new
            {
                service = "stroll.history",
                version = "1.0.0-test"
            }
        };
        
        Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false }));
        return 0;
    }

    static int OutputDiscover()
    {
        var response = new
        {
            schema = "stroll.history.v1",
            ok = true,
            data = new
            {
                service = "stroll.history",
                version = "1.0.0-test",
                commands = new[]
                {
                    new { name = "discover", description = "Service discovery" },
                    new { name = "version", description = "Get service version" },
                    new { name = "get-bars", description = "Get historical bars" },
                    new { name = "get-options", description = "Get options chain" },
                    new { name = "provider-status", description = "Get provider status" }
                }
            }
        };
        
        Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false }));
        return 0;
    }

    static int OutputProviderStatus()
    {
        var response = new
        {
            schema = "stroll.history.v1",
            ok = true,
            data = new
            {
                providers = new[]
                {
                    new
                    {
                        name = "Test Local Data",
                        priority = 0,
                        available = true,
                        healthy = true,
                        responseTimeMs = 1,
                        rateLimit = new
                        {
                            requestsRemaining = 2147483647,
                            requestsPerMinute = 2147483647
                        }
                    }
                }
            }
        };
        
        Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false }));
        return 0;
    }

    static int HandleGetBars(string[] args)
    {
        // Parse arguments
        string? symbol = null;
        string? from = null;
        string? to = null;
        string granularity = "1d";

        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length) break;
            
            var arg = args[i];
            var value = args[i + 1];

            switch (arg.ToLowerInvariant())
            {
                case "--symbol":
                    symbol = value;
                    break;
                case "--from":
                    from = value;
                    break;
                case "--to":
                    to = value;
                    break;
                case "--granularity":
                    granularity = value;
                    break;
            }
        }

        if (string.IsNullOrEmpty(symbol))
        {
            return OutputError(2, "INVALID_ARGUMENTS", "Missing required parameter: --symbol");
        }

        if (string.IsNullOrEmpty(from))
        {
            return OutputError(2, "INVALID_ARGUMENTS", "Missing required parameter: --from");
        }

        if (string.IsNullOrEmpty(to))
        {
            return OutputError(2, "INVALID_ARGUMENTS", "Missing required parameter: --to");
        }

        // Simulate test data for known symbols
        if (!IsKnownSymbol(symbol))
        {
            return OutputError(3, "DATA_NOT_FOUND", $"No data found for symbol {symbol}");
        }

        var response = new
        {
            schema = "stroll.history.v1",
            ok = true,
            data = new
            {
                symbol = symbol,
                granularity = granularity,
                from = from,
                to = to,
                bars = new[]
                {
                    new
                    {
                        t = DateTime.Parse(from).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        o = 475.23m,
                        h = 477.89m,
                        l = 474.15m,
                        c = 476.44m,
                        v = 45123456L,
                        symbol = symbol,
                        g = granularity
                    }
                }
            },
            meta = new
            {
                count = 1,
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            }
        };

        Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false }));
        return 0;
    }

    static int HandleGetOptions(string[] args)
    {
        // Parse arguments
        string? symbol = null;
        string? date = null;

        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length) break;
            
            var arg = args[i];
            var value = args[i + 1];

            switch (arg.ToLowerInvariant())
            {
                case "--symbol":
                    symbol = value;
                    break;
                case "--date":
                    date = value;
                    break;
            }
        }

        if (string.IsNullOrEmpty(symbol))
        {
            return OutputError(2, "INVALID_ARGUMENTS", "Missing required parameter: --symbol");
        }

        if (string.IsNullOrEmpty(date))
        {
            return OutputError(2, "INVALID_ARGUMENTS", "Missing required parameter: --date");
        }

        // Simulate test options data
        if (!IsKnownSymbol(symbol))
        {
            return OutputError(3, "DATA_NOT_FOUND", $"No options data found for symbol {symbol}");
        }

        var response = new
        {
            schema = "stroll.history.v1",
            ok = true,
            data = new
            {
                symbol = symbol,
                expiry = date,
                chain = new[]
                {
                    new
                    {
                        symbol = symbol,
                        expiry = date,
                        right = "CALL",
                        strike = 470.0m,
                        bid = 5.15m,
                        ask = 5.25m,
                        mid = 5.20m,
                        delta = 0.65m,
                        gamma = 0.08m
                    },
                    new
                    {
                        symbol = symbol,
                        expiry = date,
                        right = "PUT",
                        strike = 470.0m,
                        bid = 2.15m,
                        ask = 2.25m,
                        mid = 2.20m,
                        delta = -0.35m,
                        gamma = 0.08m
                    }
                }
            },
            meta = new
            {
                count = 2,
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            }
        };

        Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false }));
        return 0;
    }

    static bool IsKnownSymbol(string symbol)
    {
        var knownSymbols = new[] { "SPY", "QQQ", "XLE", "USO", "OILY", "XOP", "OIH", "UNG", "DRIP", "GUSH", "ERX", "ERY", "BOIL" };
        return Array.Exists(knownSymbols, s => s.Equals(symbol, StringComparison.OrdinalIgnoreCase));
    }

    static int OutputError(int exitCode, string errorCode, string message)
    {
        var response = new
        {
            schema = "stroll.history.v1",
            ok = false,
            error = new
            {
                code = errorCode,
                message = message
            }
        };
        
        Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false }));
        return exitCode;
    }
}