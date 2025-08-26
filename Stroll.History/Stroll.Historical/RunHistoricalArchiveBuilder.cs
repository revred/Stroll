using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Stroll.Historical;

/// <summary>
/// Comprehensive historical archive builder using Alpha Vantage month-by-month strategy
/// Builds local SQLite archive for key underlyings going back to 2000-01
/// Strategy: 25 requests/day = 24 months/day = 2+ years of data daily
/// </summary>
public class RunHistoricalArchiveBuilder
{
    public static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<RunHistoricalArchiveBuilder>();
        
        logger.LogInformation("🏛️ HISTORICAL ARCHIVE BUILDER");
        logger.LogInformation("===============================");
        logger.LogInformation("📈 Strategy: Alpha Vantage month-by-month (25 req/day = 2 years/day)");
        logger.LogInformation("📅 Coverage: 2000-01 to present (25+ years)");

        try
        {
            // Configuration
            var symbols = new[] { "SPY", "QQQ", "IWM" }; // Core ETFs
            var interval = IntradayInterval.FiveMinute; // 5-min for 1DTE strategies
            var startYear = args.Length > 0 && int.TryParse(args[0], out var year) ? year : 2020; // Default: last 5 years
            var maxRequestsToday = args.Length > 1 && int.TryParse(args[1], out var max) ? max : 24; // Stay under 25/day limit

            var dataPath = Path.GetFullPath("./historical_archive");
            Directory.CreateDirectory(dataPath);
            
            logger.LogInformation("🏷️ Symbols: {Symbols}", string.Join(", ", symbols));
            logger.LogInformation("⏱️ Interval: {Interval}", interval);
            logger.LogInformation("📅 Start Year: {Year}", startYear);
            logger.LogInformation("🚀 Max Requests Today: {Max}/25", maxRequestsToday);
            logger.LogInformation("💾 Archive: {Path}", dataPath);

            // Get Alpha Vantage API key
            var apiKey = GetAlphaVantageKey(logger);
            if (string.IsNullOrEmpty(apiKey))
            {
                logger.LogError("❌ Alpha Vantage API key required");
                Environment.Exit(1);
            }

            var provider = new AlphaVantageProvider(apiKey, false, 
                loggerFactory.CreateLogger<AlphaVantageProvider>());

            // Initialize SQLite archive database
            var dbPath = Path.Combine(dataPath, "historical_archive.db");
            await InitializeArchiveDatabase(dbPath, logger);

            // Build archive month by month for each symbol
            var requestsUsed = 0;
            var totalBarsStored = 0;

            foreach (var symbol in symbols)
            {
                if (requestsUsed >= maxRequestsToday)
                {
                    logger.LogWarning("🛑 Reached daily request limit ({Used}/{Max})", requestsUsed, maxRequestsToday);
                    break;
                }

                logger.LogInformation("🔄 Building archive for {Symbol}...", symbol);

                // Get existing months to skip duplicates
                var existingMonths = await GetExistingMonths(dbPath, symbol, logger);
                logger.LogInformation("📊 {Symbol}: {Count} months already in archive", symbol, existingMonths.Count);

                // Generate month list from startYear to present
                var targetMonths = GenerateMonthList(startYear, DateTime.Now.Year);
                var missingMonths = targetMonths.Except(existingMonths).OrderBy(m => m).ToList();
                
                logger.LogInformation("📅 {Symbol}: {Missing} missing months, {Available} requests available", 
                    symbol, missingMonths.Count, maxRequestsToday - requestsUsed);

                // Acquire missing months (respecting daily limit)
                var monthsToProcess = missingMonths.Take(maxRequestsToday - requestsUsed).ToList();

                foreach (var month in monthsToProcess)
                {
                    try
                    {
                        logger.LogInformation("📅 Acquiring {Symbol} {Month}...", symbol, month);
                        
                        var result = await provider.GetIntradayHistoricalMonthAsync(symbol, interval, month);
                        
                        if (result.Success && result.Bars.Count > 0)
                        {
                            await StoreMonthInArchive(dbPath, symbol, month, result.Bars, logger);
                            totalBarsStored += result.Bars.Count;
                            
                            logger.LogInformation("✅ {Symbol} {Month}: {Records:N0} bars stored", 
                                symbol, month, result.Bars.Count);
                        }
                        else
                        {
                            logger.LogWarning("⚠️ No data for {Symbol} {Month}", symbol, month);
                        }

                        requestsUsed++;
                        
                        // Respect rate limit: 5 requests/minute = 12 seconds between requests
                        if (requestsUsed < maxRequestsToday)
                        {
                            logger.LogDebug("⏳ Rate limiting: 12 second delay...");
                            await Task.Delay(12000);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "❌ Failed to acquire {Symbol} {Month}", symbol, month);
                    }
                }

                var remainingMonths = missingMonths.Count - monthsToProcess.Count;
                if (remainingMonths > 0)
                {
                    logger.LogInformation("📅 {Symbol}: {Remaining} months remaining for future runs", symbol, remainingMonths);
                }
            }

            // Final report
            logger.LogInformation("🎯 ARCHIVE BUILD COMPLETE!");
            logger.LogInformation("===========================");
            logger.LogInformation("📊 Total Bars Stored: {Bars:N0}", totalBarsStored);
            logger.LogInformation("🔢 API Requests Used: {Used}/25", requestsUsed);
            logger.LogInformation("📁 Archive Database: {Path}", dbPath);

            // Show archive statistics
            await ShowArchiveStatistics(dbPath, logger);

            logger.LogInformation("🎉 Ready for 1DTE backtesting with historical precision!");
            logger.LogInformation("💡 Run daily to complete 25-year archive (2 years/day)");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 Historical archive build failed");
            Environment.Exit(1);
        }
    }

    private static string? GetAlphaVantageKey(ILogger logger)
    {
        var key = Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY");
        if (string.IsNullOrEmpty(key))
        {
            logger.LogInformation("🔑 Using default Alpha Vantage API key");
            key = "ZKAZC46RTVU47DCM";
        }

        if (!string.IsNullOrEmpty(key))
        {
            logger.LogInformation("✅ Alpha Vantage API key provided");
            return key;
        }

        return null;
    }

    private static async Task InitializeArchiveDatabase(string dbPath, ILogger logger)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS intraday_bars (
                symbol TEXT NOT NULL,
                timestamp DATETIME NOT NULL,
                open DECIMAL(10,4) NOT NULL,
                high DECIMAL(10,4) NOT NULL,
                low DECIMAL(10,4) NOT NULL,
                close DECIMAL(10,4) NOT NULL,
                volume INTEGER NOT NULL,
                month TEXT NOT NULL,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (symbol, timestamp)
            );
            
            CREATE INDEX IF NOT EXISTS idx_symbol_month ON intraday_bars(symbol, month);
            CREATE INDEX IF NOT EXISTS idx_symbol_timestamp ON intraday_bars(symbol, timestamp);";

        using var command = new SqliteCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();

        logger.LogInformation("🗄️ Archive database initialized: {Path}", dbPath);
    }

    private static async Task<List<string>> GetExistingMonths(string dbPath, string symbol, ILogger logger)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        var sql = "SELECT DISTINCT month FROM intraday_bars WHERE symbol = @symbol ORDER BY month";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@symbol", symbol);

        var months = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            months.Add(reader.GetString(0)); // Use index instead of column name
        }

        return months;
    }

    private static List<string> GenerateMonthList(int startYear, int endYear)
    {
        var months = new List<string>();
        
        for (int year = startYear; year <= endYear; year++)
        {
            var maxMonth = year == endYear ? DateTime.Now.Month : 12;
            for (int month = 1; month <= maxMonth; month++)
            {
                months.Add($"{year:D4}-{month:D2}");
            }
        }

        return months;
    }

    private static async Task StoreMonthInArchive(string dbPath, string symbol, string month, 
        List<Dictionary<string, object?>> bars, ILogger logger)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        var insertSql = @"
            INSERT OR REPLACE INTO intraday_bars 
            (symbol, timestamp, open, high, low, close, volume, month) 
            VALUES (@symbol, @timestamp, @open, @high, @low, @close, @volume, @month)";

        using var command = new SqliteCommand(insertSql, connection, transaction);

        foreach (var bar in bars)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@symbol", symbol);
            command.Parameters.AddWithValue("@timestamp", (DateTime)bar["t"]!);
            command.Parameters.AddWithValue("@open", (decimal)bar["o"]!);
            command.Parameters.AddWithValue("@high", (decimal)bar["h"]!);
            command.Parameters.AddWithValue("@low", (decimal)bar["l"]!);
            command.Parameters.AddWithValue("@close", (decimal)bar["c"]!);
            command.Parameters.AddWithValue("@volume", (long)bar["v"]!);
            command.Parameters.AddWithValue("@month", month);

            await command.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }

    private static async Task ShowArchiveStatistics(string dbPath, ILogger logger)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        var statsSql = @"
            SELECT 
                symbol,
                COUNT(*) as total_bars,
                MIN(timestamp) as first_date,
                MAX(timestamp) as last_date,
                COUNT(DISTINCT month) as months_count
            FROM intraday_bars 
            GROUP BY symbol
            ORDER BY symbol";

        using var command = new SqliteCommand(statsSql, connection);
        using var reader = await command.ExecuteReaderAsync();

        logger.LogInformation("📊 ARCHIVE STATISTICS:");
        logger.LogInformation("=====================");

        while (await reader.ReadAsync())
        {
            var symbol = reader.GetString(0);
            var totalBars = reader.GetInt32(1);
            var firstDate = reader.GetDateTime(2);
            var lastDate = reader.GetDateTime(3);
            var monthsCount = reader.GetInt32(4);

            logger.LogInformation("🏷️ {Symbol}: {Bars:N0} bars, {Months} months ({First:yyyy-MM-dd} to {Last:yyyy-MM-dd})",
                symbol, totalBars, monthsCount, firstDate, lastDate);
        }
    }
}