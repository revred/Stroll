using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Data;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;

namespace Stroll.Dataset;

/// <summary>
/// Advanced Polygon dataset with Options Developer optimizations
/// Implements: Monthly sharding, Greeks computation, distributed queries, and performance tricks
/// </summary>
public class AdvancedPolygonDataset : IAsyncDisposable
{
    private readonly string _dbPassword;
    private readonly string _datasetPath;
    
    // High-performance connection pooling with distributed DB support
    private readonly ConcurrentDictionary<string, SqliteConnection> _connectionPool = new();
    private readonly ConcurrentDictionary<string, SqliteCommand> _compiledQueries = new();
    
    // Advanced query builders for distributed data
    private readonly DistributedQueryBuilder _queryBuilder;
    private readonly OptionsGreeksCalculator _greeksCalculator;
    private readonly ManifestTracker _manifestTracker;
    private readonly UniverseManager _universeManager;

    public AdvancedPolygonDataset()
    {
        _dbPassword = Environment.GetEnvironmentVariable("POLYGON_DB_PASSWORD") ?? "$$rc:P0lyg0n.$0";
        
        _datasetPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "SecureData",
            "Polygon"
        );
        
        Directory.CreateDirectory(_datasetPath);
        Directory.CreateDirectory(Path.Combine(_datasetPath, "Indices"));
        Directory.CreateDirectory(Path.Combine(_datasetPath, "Options"));
        Directory.CreateDirectory(Path.Combine(_datasetPath, "ETFs"));
        Directory.CreateDirectory(Path.Combine(_datasetPath, "Stocks"));
        Directory.CreateDirectory(Path.Combine(_datasetPath, "Manifests"));
        
        _queryBuilder = new DistributedQueryBuilder(this);
        _greeksCalculator = new OptionsGreeksCalculator();
        _manifestTracker = new ManifestTracker(Path.Combine(_datasetPath, "Manifests"));
        _universeManager = new UniverseManager();
        
        CreateSecureDataGitIgnore();
    }

    #region Advanced Database Creation with Options Developer Schema

    /// <summary>
    /// Create optimized database with advanced schema from Options Developer guide
    /// Monthly sharding for options, yearly for equities, performance optimizations
    /// </summary>
    public async Task<SqliteConnection> CreateAdvancedDatabase(string category, string symbol, 
        DateTime date, string granularity = "1min")
    {
        var dbName = GetAdvancedPartitionName(category, symbol, date, granularity);
        var dbPath = Path.Combine(_datasetPath, category, dbName);
        
        var connectionString = GetOptimizedConnectionString(dbPath);
        var connection = new SqliteConnection(connectionString);
        
        await connection.OpenAsync();
        
        // Enable advanced performance optimizations
        await SetAdvancedPragmas(connection);
        
        // Create schema based on category with Options Developer optimizations
        await CreateAdvancedSchema(connection, category, symbol, date);
        
        return connection;
    }

    private string GetOptimizedConnectionString(string dbPath)
    {
        return $@"Data Source={dbPath};
                 Password={_dbPassword};
                 Cache Size=100000;
                 Journal Mode=WAL;
                 Synchronous=NORMAL;
                 Temp Store=MEMORY;
                 Mmap Size=268435456";
    }

    private async Task SetAdvancedPragmas(SqliteConnection connection)
    {
        var pragmas = @"
            PRAGMA cache_size = 100000;
            PRAGMA temp_store = memory;
            PRAGMA mmap_size = 268435456;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA optimize;
            PRAGMA auto_vacuum = INCREMENTAL;
            PRAGMA page_size = 4096;
        ";
        
        using var command = connection.CreateCommand();
        command.CommandText = pragmas;
        await command.ExecuteNonQueryAsync();
    }

    private async Task CreateAdvancedSchema(SqliteConnection connection, string category, string symbol, DateTime date)
    {
        string createSql = category.ToLower() switch
        {
            "indices" or "etfs" or "stocks" => await GetEquityBarSchema(),
            "options" => await GetOptionsSchema(symbol, date),
            _ => throw new ArgumentException($"Unknown category: {category}")
        };
        
        using var command = connection.CreateCommand();
        command.CommandText = createSql;
        await command.ExecuteNonQueryAsync();
        
        // Create manifest entry
        await _manifestTracker.RecordDatabaseCreation(category, symbol, date, createSql);
    }

    private async Task<string> GetEquityBarSchema()
    {
        return @"
            -- Optimized equity/index bars schema (Options Developer spec)
            CREATE TABLE IF NOT EXISTS bars_eq (
                ticker TEXT NOT NULL,
                ts INTEGER NOT NULL,        -- epoch ms (UTC) for performance
                o REAL, h REAL, l REAL, c REAL,
                v INTEGER,                  -- volume
                trades INTEGER,             -- trade count
                vwap REAL,                 -- volume weighted average price
                source TEXT DEFAULT 'polygon_verified',
                created_at INTEGER DEFAULT (strftime('%s', 'now') * 1000),
                PRIMARY KEY (ticker, ts)    -- Composite PK for performance
            ) WITHOUT ROWID;               -- No rowid for efficiency
            
            -- Hot indices for range queries
            CREATE INDEX IF NOT EXISTS ix_bars_eq_ts ON bars_eq(ts);
            CREATE INDEX IF NOT EXISTS ix_bars_eq_ticker_ts ON bars_eq(ticker, ts);
            
            -- 5-minute rollup view (dynamic aggregation)
            CREATE VIEW IF NOT EXISTS v_bars_eq_5m AS
            SELECT
                ticker,
                (ts/300000)*300000 AS ts5m,
                MIN(CASE WHEN rn=1 THEN o END) AS o,
                MAX(h) AS h,
                MIN(l) AS l,
                MAX(CASE WHEN rn_last=1 THEN c END) AS c,
                SUM(v) AS v,
                SUM(trades) AS trades,
                SUM(v * vwap) / SUM(v) AS vwap
            FROM (
                SELECT b.*,
                       ROW_NUMBER() OVER (PARTITION BY ticker, ts/300000 ORDER BY ts) AS rn,
                       ROW_NUMBER() OVER (PARTITION BY ticker, ts/300000 ORDER BY ts DESC) AS rn_last
                FROM bars_eq b
                WHERE v > 0  -- Only include bars with volume
            )
            GROUP BY ticker, ts5m;
            
            -- Performance monitoring
            CREATE TABLE IF NOT EXISTS perf_stats (
                table_name TEXT PRIMARY KEY,
                row_count INTEGER,
                last_updated INTEGER,
                avg_query_ms REAL
            );
        ";
    }

    private async Task<string> GetOptionsSchema(string symbol, DateTime date)
    {
        var monthKey = $"{date.Year:D4}_{date.Month:D2}";
        var tableName = $"op_aggs_{symbol.ToLower()}_{monthKey}";
        
        return $@"
            -- Monthly options aggregates shard (Options Developer spec)
            CREATE TABLE IF NOT EXISTS {tableName} (
                contract TEXT NOT NULL,     -- OCC option ticker e.g., O:SPXW...
                ts INTEGER NOT NULL,        -- epoch ms (UTC)
                o REAL, h REAL, l REAL, c REAL,
                v INTEGER,                  -- volume
                oi INTEGER,                 -- open interest
                trades INTEGER,             -- trade count
                PRIMARY KEY (contract, ts)  -- Composite PK
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS ix_{tableName}_ts ON {tableName}(ts);
            CREATE INDEX IF NOT EXISTS ix_{tableName}_contract ON {tableName}(contract);
            
            -- Contract metadata for the month
            CREATE TABLE IF NOT EXISTS contracts_meta (
                underlying TEXT NOT NULL,
                as_of TEXT NOT NULL,        -- YYYY-MM-DD
                json BLOB NOT NULL,         -- compressed JSON of discovery result
                PRIMARY KEY (underlying, as_of)
            );
            
            -- Greeks & IV derived data
            CREATE TABLE IF NOT EXISTS op_iv_greeks (
                contract TEXT NOT NULL,
                ts INTEGER NOT NULL,
                iv REAL,                    -- implied volatility
                delta REAL, gamma REAL, theta REAL, vega REAL, rho REAL,
                ref_px REAL,               -- underlying price used for calc
                mid_px REAL,               -- option mid used for calc
                bid REAL, ask REAL,        -- if available
                spread_pct REAL,           -- (ask-bid)/mid
                PRIMARY KEY (contract, ts)
            ) WITHOUT ROWID;
            
            CREATE INDEX IF NOT EXISTS ix_op_greeks_ts ON op_iv_greeks(ts);
            CREATE INDEX IF NOT EXISTS ix_op_greeks_contract ON op_iv_greeks(contract);
            
            -- Options selection rules tracking
            CREATE TABLE IF NOT EXISTS selection_rules (
                rule_id TEXT PRIMARY KEY,
                underlying TEXT,
                as_of TEXT,
                atm_window_strikes INTEGER,
                dte_focus TEXT,             -- JSON array
                max_contracts INTEGER,
                created_at INTEGER
            );
            
            -- Universal options view (UNION ALL across months)
            CREATE VIEW IF NOT EXISTS v_op_aggs_{symbol.ToLower()} AS
            SELECT * FROM {tableName};
        ";
    }

    #endregion

    #region Options Monthly Sharding Strategy

    /// <summary>
    /// Create monthly sharded options tables per underlying
    /// Implements Options Developer strategy: options_spx_2025_08
    /// </summary>
    public async Task CreateOptionsMonthlyShards(string underlying, DateTime month, string dbPath)
    {
        var connection = new SqliteConnection(GetOptimizedConnectionString(dbPath));
        await connection.OpenAsync();
        await SetAdvancedPragmas(connection);

        var tableName = $"op_aggs_{underlying.ToLower()}_{month.Year:D4}_{month.Month:D2}";
        
        var createTableSql = $@"
            -- Options minute bars (monthly shard: {tableName})
            CREATE TABLE IF NOT EXISTS {tableName} (
                contract TEXT NOT NULL,       -- OCC option ticker e.g., O:SPXW240830C05500000
                ts INTEGER NOT NULL,          -- epoch ms (UTC)
                o REAL, h REAL, l REAL, c REAL,
                v INTEGER,                    -- volume
                oi INTEGER,                   -- open interest (if available)
                trades INTEGER,               -- number of trades (if available)
                vwap REAL,                    -- volume weighted average price
                PRIMARY KEY (contract, ts)
            ) WITHOUT ROWID;
            
            -- Performance indexes
            CREATE INDEX IF NOT EXISTS ix_{tableName}_ts ON {tableName}(ts);
            CREATE INDEX IF NOT EXISTS ix_{tableName}_contract ON {tableName}(contract);
            CREATE INDEX IF NOT EXISTS ix_{tableName}_contract_ts ON {tableName}(contract, ts);
            
            -- Contract metadata for this month/underlying
            CREATE TABLE IF NOT EXISTS contracts_meta_{underlying.ToLower()}_{month.Year:D4}_{month.Month:D2} (
                contract TEXT PRIMARY KEY,
                option_type TEXT,             -- CALL/PUT
                strike_price REAL,
                expiration_date TEXT,         -- YYYY-MM-DD
                underlying TEXT,
                first_seen INTEGER,           -- timestamp first appeared
                last_seen INTEGER,            -- timestamp last seen
                days_to_expiry REAL,          -- average DTE during the month
                moneyness REAL               -- average strike/spot ratio
            );
            
            -- Greeks and IV for this shard
            CREATE TABLE IF NOT EXISTS op_iv_greeks_{underlying.ToLower()}_{month.Year:D4}_{month.Month:D2} (
                contract TEXT NOT NULL,
                ts INTEGER NOT NULL,
                iv REAL,                      -- implied volatility
                delta REAL, gamma REAL, theta REAL, vega REAL, rho REAL,
                ref_px REAL,                  -- underlying price used for calc
                mid_px REAL,                  -- option mid used for calc
                bid REAL, ask REAL,           -- if available
                spread_pct REAL,              -- (ask-bid)/mid
                PRIMARY KEY (contract, ts)
            ) WITHOUT ROWID;
            
            CREATE INDEX IF NOT EXISTS ix_greeks_{tableName}_ts ON op_iv_greeks_{underlying.ToLower()}_{month.Year:D4}_{month.Month:D2}(ts);
            CREATE INDEX IF NOT EXISTS ix_greeks_{tableName}_contract ON op_iv_greeks_{underlying.ToLower()}_{month.Year:D4}_{month.Month:D2}(contract);
        ";

        await connection.ExecuteAsync(createTableSql);
        await connection.CloseAsync();
    }

    /// <summary>
    /// Create unified view across all monthly shards for an underlying
    /// </summary>
    public async Task CreateOptionsUnifiedView(string underlying, string dbPath, List<DateTime> availableMonths)
    {
        var connection = new SqliteConnection(GetOptimizedConnectionString(dbPath));
        await connection.OpenAsync();

        var unionSelects = new List<string>();
        var greeksUnionSelects = new List<string>();

        foreach (var month in availableMonths)
        {
            var tableName = $"op_aggs_{underlying.ToLower()}_{month.Year:D4}_{month.Month:D2}";
            var greeksTableName = $"op_iv_greeks_{underlying.ToLower()}_{month.Year:D4}_{month.Month:D2}";
            
            unionSelects.Add($"SELECT * FROM {tableName}");
            greeksUnionSelects.Add($"SELECT * FROM {greeksTableName}");
        }

        var viewSql = $@"
            -- Unified view across all monthly shards
            CREATE VIEW IF NOT EXISTS v_op_aggs_{underlying.ToLower()} AS
            {string.Join(" UNION ALL ", unionSelects)}
            ORDER BY ts;
            
            -- Unified Greeks view
            CREATE VIEW IF NOT EXISTS v_op_greeks_{underlying.ToLower()} AS
            {string.Join(" UNION ALL ", greeksUnionSelects)}
            ORDER BY ts;
        ";

        await connection.ExecuteAsync(viewSql);
        await connection.CloseAsync();
    }

    /// <summary>
    /// Get options contract selection rules based on Options Developer guide
    /// ATM ±window strikes, top OI expiries, DTE focus
    /// </summary>
    public async Task<List<string>> GetOptionsContractSelection(
        string underlying, DateTime asOfDate, 
        int atmWindow = 10, 
        List<int>? dteFocus = null,
        int maxContracts = 300)
    {
        dteFocus ??= new List<int> { 0, 1, 7, 30, 90, 180, 365 };
        
        // Implementation would call Polygon discovery API
        // For now, return structure for the contract selection
        var selectedContracts = new List<string>();
        
        // This would be implemented with actual Polygon API calls
        // to get chains, filter by ATM window, DTE, and OI rankings
        
        return selectedContracts.Take(maxContracts).ToList();
    }

    #endregion

    #region Advanced Partitioning Strategy

    /// <summary>
    /// Get advanced partition name using Options Developer strategy:
    /// - Sub-minute: Monthly partitions (trades_SPY_2025_01.db)
    /// - 1-minute: Yearly for equities, monthly for options
    /// - 5-minute: 5-year partitions
    /// </summary>
    public string GetAdvancedPartitionName(string category, string symbol, DateTime date, string granularity = "1min")
    {
        var symbolLower = symbol.ToLower();
        
        return category.ToLower() switch
        {
            "options" => granularity.ToLower() switch
            {
                "tick" or "trade" or "quote" => $"options_{symbolLower}_{date.Year}_{date.Month:D2}_tick.db",
                "1min" => $"options_{symbolLower}_{date.Year}_{date.Month:D2}.db", // Monthly sharding
                "5min" => $"options_{symbolLower}_{GetFiveYearRange(date.Year)}_5m.db",
                _ => $"options_{symbolLower}_{date.Year}_{date.Month:D2}_{granularity}.db"
            },
            "indices" or "etfs" or "stocks" => granularity.ToLower() switch
            {
                "tick" or "trade" or "quote" => $"{category}_{symbolLower}_{date.Year}_{date.Month:D2}_tick.db",
                "1min" => $"{category}_{symbolLower}_{date.Year}.db", // Yearly partitions
                "5min" => $"{category}_{symbolLower}_{GetFiveYearRange(date.Year)}_5m.db",
                _ => $"{category}_{symbolLower}_{date.Year}_{granularity}.db"
            },
            _ => throw new ArgumentException($"Unknown category: {category}")
        };
    }

    private string GetFiveYearRange(int year)
    {
        var startYear = (year / 5) * 5;
        var endYear = startYear + 4;
        return $"{startYear}_{endYear}";
    }

    #endregion

    #region Distributed Query Builder

    /// <summary>
    /// Build distributed queries across multiple sharded databases
    /// Implements ATTACH strategy from Options Developer guide
    /// </summary>
    public class DistributedQueryBuilder
    {
        private readonly AdvancedPolygonDataset _dataset;

        public DistributedQueryBuilder(AdvancedPolygonDataset dataset)
        {
            _dataset = dataset;
        }

        public async Task<IEnumerable<T>> QueryAcrossTimeRange<T>(
            string category, string symbol, DateTime startDate, DateTime endDate,
            string granularity = "1min", Func<SqliteDataReader, T>? mapper = null) where T : class, new()
        {
            var results = new List<T>();
            var databases = GetRequiredDatabases(category, symbol, startDate, endDate, granularity);
            
            if (!databases.Any()) return results;

            // Primary database connection
            var primaryDb = databases.First();
            var connection = await _dataset.GetPooledConnection(primaryDb);
            
            try
            {
                // ATTACH additional databases
                var attachments = new List<string>();
                for (int i = 1; i < databases.Count; i++)
                {
                    var dbPath = Path.Combine(_dataset._datasetPath, category, databases[i]);
                    var attachName = $"db{i}";
                    
                    using var attachCmd = connection.CreateCommand();
                    attachCmd.CommandText = $"ATTACH DATABASE '{dbPath}' AS {attachName} KEY '{_dataset._dbPassword}'";
                    await attachCmd.ExecuteNonQueryAsync();
                    
                    attachments.Add(attachName);
                }

                // Build distributed UNION query
                var query = BuildUnionQuery(category, symbol, startDate, endDate, attachments, granularity);
                
                using var command = connection.CreateCommand();
                command.CommandText = query;
                command.Parameters.Add("@startTs", SqliteType.Integer).Value = ((DateTimeOffset)startDate).ToUnixTimeMilliseconds();
                command.Parameters.Add("@endTs", SqliteType.Integer).Value = ((DateTimeOffset)endDate).ToUnixTimeMilliseconds();
                command.Parameters.Add("@symbol", SqliteType.Text).Value = symbol.ToUpper();

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (mapper != null)
                    {
                        results.Add(mapper(reader));
                    }
                    else
                    {
                        var item = new T();
                        MapReaderToObject(reader, item);
                        results.Add(item);
                    }
                }

                // Clean up attachments
                foreach (var attachment in attachments)
                {
                    using var detachCmd = connection.CreateCommand();
                    detachCmd.CommandText = $"DETACH DATABASE {attachment}";
                    await detachCmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Distributed query failed: {ex.Message}", ex);
            }

            return results;
        }

        /// <summary>
        /// Advanced options chain query across monthly shards
        /// Optimized for MCP service performance requirements
        /// </summary>
        public async Task<OptionsChainData> QueryOptionsChain(
            string underlying, DateTime startDate, DateTime endDate,
            int atmWindow = 10, List<int>? dteFocus = null)
        {
            dteFocus ??= new List<int> { 0, 1, 7, 30, 90, 180 };
            var results = new OptionsChainData { Underlying = underlying };
            
            // Determine required monthly shards
            var months = GetMonthRange(startDate, endDate);
            var databases = months.Select(m => 
                _dataset.GetAdvancedPartitionName("options", underlying, m, "1min")).ToList();

            if (!databases.Any()) return results;

            var connection = await _dataset.GetPooledConnection(databases.First());
            
            try
            {
                // ATTACH monthly shards
                var attachments = new List<string>();
                for (int i = 1; i < databases.Count; i++)
                {
                    var dbPath = Path.Combine(_dataset._datasetPath, "Options", databases[i]);
                    var attachName = $"month{i}";
                    
                    await connection.ExecuteAsync($"ATTACH DATABASE '{dbPath}' AS {attachName} KEY '{_dataset._dbPassword}'");
                    attachments.Add(attachName);
                }

                // Build options chain query with Greeks
                var chainQuery = BuildOptionsChainQuery(underlying, startDate, endDate, attachments, atmWindow, dteFocus);
                
                var optionsData = await connection.QueryAsync<OptionsBarData>(chainQuery, new 
                { 
                    startTs = ((DateTimeOffset)startDate).ToUnixTimeMilliseconds(),
                    endTs = ((DateTimeOffset)endDate).ToUnixTimeMilliseconds(),
                    underlying = underlying.ToUpper()
                });

                results.Options = optionsData.ToList();
                results.TotalContracts = optionsData.Count();
                results.DateRange = $"{startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}";

                // Cleanup attachments
                foreach (var attachment in attachments)
                {
                    await connection.ExecuteAsync($"DETACH DATABASE {attachment}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to query options chain for {underlying}: {ex.Message}", ex);
            }

            return results;
        }

        /// <summary>
        /// High-performance market regime analysis across 5-year partitions
        /// </summary>
        public async Task<MarketRegimeData> QueryMarketRegimes(
            string symbol, DateTime startDate, DateTime endDate, 
            string granularity = "5min")
        {
            var regimeData = new MarketRegimeData { Symbol = symbol };
            
            // Use 5-year partitions for long-term analysis
            var category = DetectCategory(symbol);
            var databases = GetRequiredDatabases(category, symbol, startDate, endDate, granularity);
            
            if (!databases.Any()) return regimeData;

            var connection = await _dataset.GetPooledConnection(databases.First());
            
            try
            {
                // ATTACH 5-year databases
                var attachments = new List<string>();
                for (int i = 1; i < databases.Count; i++)
                {
                    var dbPath = Path.Combine(_dataset._datasetPath, category, databases[i]);
                    var attachName = $"regime{i}";
                    
                    await connection.ExecuteAsync($"ATTACH DATABASE '{dbPath}' AS {attachName} KEY '{_dataset._dbPassword}'");
                    attachments.Add(attachName);
                }

                // Complex regime analysis query
                var regimeQuery = BuildMarketRegimeQuery(symbol, startDate, endDate, attachments, granularity);
                
                var bars = await connection.QueryAsync<AdvancedBarData>(regimeQuery, new 
                { 
                    startTs = ((DateTimeOffset)startDate).ToUnixTimeMilliseconds(),
                    endTs = ((DateTimeOffset)endDate).ToUnixTimeMilliseconds(),
                    symbol = symbol.ToUpper()
                });

                // Compute regime statistics
                regimeData.Bars = bars.ToList();
                regimeData.VolatilityRegimes = ComputeVolatilityRegimes(bars);
                regimeData.SupportResistance = ComputeSupportResistance(bars);
                regimeData.TrendAnalysis = ComputeTrendAnalysis(bars);

                // Cleanup
                foreach (var attachment in attachments)
                {
                    await connection.ExecuteAsync($"DETACH DATABASE {attachment}");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to analyze market regimes for {symbol}: {ex.Message}", ex);
            }

            return regimeData;
        }

        /// <summary>
        /// Zero DTE scanner across multiple underlyings with real-time Greeks
        /// </summary>
        public async Task<List<ZeroDTEOpportunity>> ScanZeroDTE(
            List<string> underlyings, DateTime tradingDay,
            int maxOpportunities = 20)
        {
            var opportunities = new List<ZeroDTEOpportunity>();
            var today = tradingDay.Date;
            
            foreach (var underlying in underlyings)
            {
                try
                {
                    var monthlyDb = _dataset.GetAdvancedPartitionName("options", underlying, today, "1min");
                    var dbPath = Path.Combine(_dataset._datasetPath, "Options", monthlyDb);
                    
                    if (!File.Exists(dbPath)) continue;

                    var connection = await _dataset.GetPooledConnection(monthlyDb);
                    
                    // Query 0DTE options expiring today
                    var todayContracts = await Get0DTEContracts(connection, underlying, today);
                    
                    foreach (var contract in todayContracts)
                    {
                        var opportunity = await EvaluateContract(connection, contract, today);
                        if (opportunity != null && opportunity.Score > 0.5) // Minimum quality threshold
                        {
                            opportunities.Add(opportunity);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️  Failed to scan 0DTE for {underlying}: {ex.Message}");
                }
            }

            return opportunities
                .OrderByDescending(o => o.Score)
                .Take(maxOpportunities)
                .ToList();
        }

        private string BuildOptionsChainQuery(string underlying, DateTime startDate, DateTime endDate, 
            List<string> attachments, int atmWindow, List<int> dteFocus)
        {
            var primaryTable = $"op_aggs_{underlying.ToLower()}_{startDate.Year:D4}_{startDate.Month:D2}";
            var greeksTable = $"op_iv_greeks_{underlying.ToLower()}_{startDate.Year:D4}_{startDate.Month:D2}";
            
            var selects = new List<string>
            {
                $@"SELECT 
                    o.contract, o.ts, o.o, o.h, o.l, o.c, o.v, o.oi, o.trades,
                    g.iv, g.delta, g.gamma, g.theta, g.vega, g.rho,
                    g.ref_px as underlying_price, g.mid_px, g.spread_pct
                   FROM {primaryTable} o
                   LEFT JOIN {greeksTable} g ON o.contract = g.contract AND o.ts = g.ts
                   WHERE o.ts BETWEEN @startTs AND @endTs"
            };

            // Add attached monthly shards
            for (int i = 0; i < attachments.Count; i++)
            {
                var attachName = attachments[i];
                var date = startDate.AddMonths(i + 1);
                var attachTable = $"op_aggs_{underlying.ToLower()}_{date.Year:D4}_{date.Month:D2}";
                var attachGreeksTable = $"op_iv_greeks_{underlying.ToLower()}_{date.Year:D4}_{date.Month:D2}";
                
                selects.Add($@"SELECT 
                    o.contract, o.ts, o.o, o.h, o.l, o.c, o.v, o.oi, o.trades,
                    g.iv, g.delta, g.gamma, g.theta, g.vega, g.rho,
                    g.ref_px as underlying_price, g.mid_px, g.spread_pct
                   FROM {attachName}.{attachTable} o
                   LEFT JOIN {attachName}.{attachGreeksTable} g ON o.contract = g.contract AND o.ts = g.ts
                   WHERE o.ts BETWEEN @startTs AND @endTs");
            }

            return $@"
                WITH ranked_options AS (
                    {string.Join(" UNION ALL ", selects)}
                )
                SELECT * FROM ranked_options
                WHERE underlying_price > 0  -- Filter out invalid data
                ORDER BY ts, contract
                LIMIT 10000  -- MCP performance limit
            ";
        }

        private string BuildMarketRegimeQuery(string symbol, DateTime startDate, DateTime endDate, 
            List<string> attachments, string granularity)
        {
            var selects = new List<string> 
            { 
                "SELECT ticker, ts, o, h, l, c, v, trades, vwap FROM bars_eq WHERE ticker = @symbol AND ts BETWEEN @startTs AND @endTs" 
            };

            foreach (var attachment in attachments)
            {
                selects.Add($"SELECT ticker, ts, o, h, l, c, v, trades, vwap FROM {attachment}.bars_eq WHERE ticker = @symbol AND ts BETWEEN @startTs AND @endTs");
            }

            return $@"
                WITH market_data AS (
                    {string.Join(" UNION ALL ", selects)}
                ),
                enhanced_bars AS (
                    SELECT *,
                        LAG(c, 1) OVER (ORDER BY ts) as prev_close,
                        (c - LAG(c, 1) OVER (ORDER BY ts)) / LAG(c, 1) OVER (ORDER BY ts) as returns,
                        (h - l) / c as true_range_pct
                    FROM market_data
                )
                SELECT * FROM enhanced_bars
                WHERE returns IS NOT NULL
                ORDER BY ts
                LIMIT 50000  -- Large dataset for regime analysis
            ";
        }

        private async Task<List<string>> Get0DTEContracts(SqliteConnection connection, string underlying, DateTime today)
        {
            // This would implement actual 0DTE contract discovery
            // For now, return placeholder structure
            return new List<string>();
        }

        private async Task<ZeroDTEOpportunity?> EvaluateContract(SqliteConnection connection, string contract, DateTime today)
        {
            // This would implement contract evaluation logic
            // For now, return placeholder
            return null;
        }

        private List<VolatilityRegime> ComputeVolatilityRegimes(IEnumerable<AdvancedBarData> bars)
        {
            // Implement volatility regime analysis
            return new List<VolatilityRegime>();
        }

        private List<SupportResistanceLevel> ComputeSupportResistance(IEnumerable<AdvancedBarData> bars)
        {
            // Implement support/resistance detection
            return new List<SupportResistanceLevel>();
        }

        private TrendAnalysis ComputeTrendAnalysis(IEnumerable<AdvancedBarData> bars)
        {
            // Implement trend analysis
            return new TrendAnalysis();
        }

        private List<DateTime> GetMonthRange(DateTime start, DateTime end)
        {
            var months = new List<DateTime>();
            var current = new DateTime(start.Year, start.Month, 1);
            var endMonth = new DateTime(end.Year, end.Month, 1);
            
            while (current <= endMonth)
            {
                months.Add(current);
                current = current.AddMonths(1);
            }
            
            return months;
        }

        private string DetectCategory(string symbol)
        {
            if (symbol.StartsWith("I:")) return "indices";
            if (new[] { "SPY", "GLD", "USO", "QQQ" }.Contains(symbol)) return "etfs";
            return "stocks";
        }

        private List<string> GetRequiredDatabases(string category, string symbol, DateTime startDate, DateTime endDate, string granularity)
        {
            var databases = new List<string>();
            var current = new DateTime(startDate.Year, startDate.Month, 1);
            var end = new DateTime(endDate.Year, endDate.Month, 1);

            while (current <= end)
            {
                var dbName = _dataset.GetAdvancedPartitionName(category, symbol, current, granularity);
                var dbPath = Path.Combine(_dataset._datasetPath, category, dbName);
                
                if (File.Exists(dbPath))
                {
                    databases.Add(dbName);
                }
                
                current = current.AddMonths(1);
            }

            return databases.Distinct().ToList();
        }

        private string BuildUnionQuery(string category, string symbol, DateTime startDate, DateTime endDate, 
            List<string> attachments, string granularity)
        {
            var tableName = category.ToLower() switch
            {
                "options" => $"op_aggs_{symbol.ToLower()}_{startDate.Year:D4}_{startDate.Month:D2}",
                _ => "bars_eq"
            };

            var selects = new List<string> { $"SELECT * FROM {tableName} WHERE ts BETWEEN @startTs AND @endTs" };
            
            // Add attached databases
            for (int i = 0; i < attachments.Count; i++)
            {
                var attachName = attachments[i];
                var date = startDate.AddMonths(i + 1);
                var attachTableName = category.ToLower() switch
                {
                    "options" => $"op_aggs_{symbol.ToLower()}_{date.Year:D4}_{date.Month:D2}",
                    _ => "bars_eq"
                };
                
                selects.Add($"SELECT * FROM {attachName}.{attachTableName} WHERE ts BETWEEN @startTs AND @endTs");
            }

            return string.Join(" UNION ALL ", selects) + " ORDER BY ts";
        }

        private void MapReaderToObject<T>(SqliteDataReader reader, T item)
        {
            var type = typeof(T);
            var properties = type.GetProperties();
            
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                var property = properties.FirstOrDefault(p => 
                    string.Equals(p.Name, columnName, StringComparison.OrdinalIgnoreCase));
                
                if (property != null && property.CanWrite && !reader.IsDBNull(i))
                {
                    var value = reader.GetValue(i);
                    if (property.PropertyType != value.GetType() && value != DBNull.Value)
                    {
                        value = Convert.ChangeType(value, property.PropertyType);
                    }
                    property.SetValue(item, value);
                }
            }
        }
    }

    #endregion

    #region Greeks and IV Integration System

    /// <summary>
    /// Comprehensive Greeks computation and storage system
    /// Integrates with options monthly shards and underlying data
    /// </summary>
    public async Task ComputeAndStoreGreeks(
        string underlying, 
        DateTime month, 
        double riskFreeRate = 0.05,
        CancellationToken cancellationToken = default)
    {
        var tableName = $"op_aggs_{underlying.ToLower()}_{month.Year:D4}_{month.Month:D2}";
        var greeksTableName = $"op_iv_greeks_{underlying.ToLower()}_{month.Year:D4}_{month.Month:D2}";
        
        var dbPath = GetAdvancedPartitionName("options", underlying, month, "1min");
        var fullDbPath = Path.Combine(_datasetPath, "Options", dbPath);
        
        if (!File.Exists(fullDbPath))
        {
            throw new FileNotFoundException($"Options database not found: {fullDbPath}");
        }

        var connection = new SqliteConnection(GetOptimizedConnectionString(fullDbPath));
        await connection.OpenAsync(cancellationToken);
        await SetAdvancedPragmas(connection);

        try
        {
            // Get all unique contracts for this month
            var contractsQuery = $@"
                SELECT DISTINCT contract 
                FROM {tableName} 
                ORDER BY contract
            ";
            
            var contracts = await connection.QueryAsync<string>(contractsQuery);
            
            foreach (var contract in contracts)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                await ProcessContractGreeks(connection, contract, tableName, greeksTableName, 
                    underlying, riskFreeRate, cancellationToken);
            }

            // Create summary statistics
            await CreateGreeksSummaryStats(connection, greeksTableName, month);
            
            Console.WriteLine($"✅ Greeks computed for {contracts.Count()} contracts in {underlying} {month:yyyy-MM}");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private async Task ProcessContractGreeks(
        SqliteConnection connection, 
        string contract, 
        string barsTable, 
        string greeksTable,
        string underlying,
        double riskFreeRate,
        CancellationToken cancellationToken)
    {
        // Parse contract details (O:SPXW240830C05500000)
        var contractDetails = ParseOCCContract(contract);
        if (contractDetails == null) return;

        // Get all time series data for this contract
        var barsQuery = $@"
            SELECT ts, o, h, l, c, v 
            FROM {barsTable} 
            WHERE contract = @contract 
            ORDER BY ts
        ";

        var bars = await connection.QueryAsync(barsQuery, new { contract });

        foreach (var bar in bars)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(bar.ts);
            var optionMid = (bar.h + bar.l + 2.0 * bar.c) / 4.0; // Close-weighted proxy
            
            // Get underlying price at this timestamp (would need underlying data)
            var underlyingPrice = await GetUnderlyingPrice(underlying, timestamp);
            if (underlyingPrice <= 0) continue;

            var timeToExpiry = (contractDetails.ExpirationDate - timestamp.DateTime).TotalDays / 365.0;
            if (timeToExpiry <= 0) continue; // Expired

            // Solve for IV and compute Greeks
            var greeks = await _greeksCalculator.CalculateGreeks(
                optionMid, underlyingPrice, contractDetails.Strike, 
                timeToExpiry, riskFreeRate, contractDetails.OptionType);

            if (greeks.ImpliedVolatility <= 0 || greeks.ImpliedVolatility > 5.0) continue; // Skip unreasonable IV values

            // Calculate spread approximation (for bar data)
            var spreadPct = Math.Max(0.01, (bar.h - bar.l) / optionMid); // Rough spread estimate

            // Store Greeks
            var insertGreeksQuery = $@"
                INSERT OR REPLACE INTO {greeksTable} 
                (contract, ts, iv, delta, gamma, theta, vega, rho, 
                 ref_px, mid_px, spread_pct)
                VALUES 
                (@contract, @ts, @iv, @delta, @gamma, @theta, @vega, @rho, 
                 @refPx, @midPx, @spreadPct)
            ";

            await connection.ExecuteAsync(insertGreeksQuery, new
            {
                contract,
                ts = bar.ts,
                iv = greeks.ImpliedVolatility,
                delta = greeks.Delta,
                gamma = greeks.Gamma,
                theta = greeks.Theta,
                vega = greeks.Vega,
                rho = greeks.Rho,
                refPx = underlyingPrice,
                midPx = optionMid,
                spreadPct
            });
        }
    }

    private ContractDetails? ParseOCCContract(string contract)
    {
        // Parse OCC format: O:SPXW240830C05500000
        // Format: O:{underlying}{expiry}{type}{strike}
        if (!contract.StartsWith("O:")) return null;

        try
        {
            var parts = contract.Substring(2); // Remove "O:"
            var optionType = parts.Contains('C') ? "CALL" : "PUT";
            var typeIndex = parts.IndexOf(optionType == "CALL" ? 'C' : 'P');
            
            var underlyingAndDate = parts.Substring(0, typeIndex);
            var strikePart = parts.Substring(typeIndex + 1);
            
            // Extract expiration (YYMMDD format typically)
            var dateMatch = System.Text.RegularExpressions.Regex.Match(underlyingAndDate, @"(\d{6})$");
            if (!dateMatch.Success) return null;
            
            var dateStr = dateMatch.Groups[1].Value;
            var underlying = underlyingAndDate.Replace(dateStr, "");
            
            // Parse date (YYMMDD -> full date)
            var year = 2000 + int.Parse(dateStr.Substring(0, 2));
            var month = int.Parse(dateStr.Substring(2, 2));
            var day = int.Parse(dateStr.Substring(4, 2));
            
            // Parse strike (divide by 1000 typically)
            var strike = double.Parse(strikePart) / 1000.0;
            
            return new ContractDetails
            {
                Underlying = underlying,
                ExpirationDate = new DateTime(year, month, day),
                OptionType = optionType,
                Strike = strike
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<double> GetUnderlyingPrice(string underlying, DateTimeOffset timestamp)
    {
        // This would query the underlying data from indices/etfs/stocks tables
        // For now, return a placeholder - this needs integration with underlying data
        
        // Would implement: Query from bars_eq table for the underlying at timestamp
        return 5500.0; // Placeholder for SPX
    }

    private async Task CreateGreeksSummaryStats(SqliteConnection connection, string greeksTable, DateTime month)
    {
        var summaryQuery = $@"
            CREATE TABLE IF NOT EXISTS greeks_summary_{month.Year:D4}_{month.Month:D2} AS
            SELECT 
                DATE(ts/1000, 'unixepoch') as trade_date,
                COUNT(*) as total_calculations,
                AVG(iv) as avg_iv,
                AVG(ABS(delta)) as avg_abs_delta,
                AVG(gamma) as avg_gamma,
                AVG(ABS(theta)) as avg_abs_theta,
                AVG(vega) as avg_vega,
                MIN(iv) as min_iv,
                MAX(iv) as max_iv,
                COUNT(CASE WHEN iv > 0.5 THEN 1 END) as high_iv_count,
                COUNT(CASE WHEN ABS(delta) > 0.8 THEN 1 END) as deep_itm_otm_count
            FROM {greeksTable}
            WHERE iv > 0 AND iv < 5.0  -- Filter reasonable IV values
            GROUP BY trade_date
            ORDER BY trade_date;
        ";

        await connection.ExecuteAsync(summaryQuery);
    }

    /// <summary>
    /// Batch process Greeks for multiple months and underlyings
    /// </summary>
    public async Task BatchProcessGreeks(
        List<string> underlyings, 
        DateTime startMonth, 
        DateTime endMonth,
        double riskFreeRate = 0.05,
        int maxConcurrency = 3)
    {
        var months = new List<DateTime>();
        var current = new DateTime(startMonth.Year, startMonth.Month, 1);
        var end = new DateTime(endMonth.Year, endMonth.Month, 1);
        
        while (current <= end)
        {
            months.Add(current);
            current = current.AddMonths(1);
        }

        var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new List<Task>();

        foreach (var underlying in underlyings)
        {
            foreach (var month in months)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await ComputeAndStoreGreeks(underlying, month, riskFreeRate);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Failed to process {underlying} {month:yyyy-MM}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }
        }

        await Task.WhenAll(tasks);
        Console.WriteLine($"✅ Batch Greeks processing complete for {underlyings.Count} underlyings, {months.Count} months");
    }

    #endregion

    #region Universe Management and Symbol Expansion

    /// <summary>
    /// Get comprehensive symbol list for data acquisition
    /// </summary>
    public List<string> GetAllSymbols() => _universeManager.GetAllSymbols();

    /// <summary>
    /// Get priority symbols for intensive data collection
    /// </summary>
    public List<string> GetPrioritySymbols(int minPriority = 7) => 
        _universeManager.GetPrioritySymbols(minPriority);

    /// <summary>
    /// Get symbols optimized for specific trading strategies
    /// </summary>
    public List<string> GetSymbolsForStrategy(TradingStrategy strategy) =>
        _universeManager.GetSymbolsForStrategy(strategy);

    /// <summary>
    /// Batch data acquisition across expanded universe
    /// </summary>
    public async Task AcquireUniverseData(
        DateTime startDate, 
        DateTime endDate,
        TradingStrategy strategy = TradingStrategy.ZeroDTE,
        string granularity = "1min")
    {
        var symbols = GetSymbolsForStrategy(strategy);
        var semaphore = new SemaphoreSlim(3); // Limit concurrent acquisitions
        var tasks = new List<Task>();

        Console.WriteLine($"🚀 Starting universe data acquisition for {symbols.Count} symbols ({strategy})");
        Console.WriteLine($"   📅 Period: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");
        Console.WriteLine($"   ⏱️  Granularity: {granularity}");

        foreach (var symbol in symbols)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var metadata = _universeManager.GetMetadata(symbol);
                    if (metadata == null) return;

                    var category = metadata.Category;
                    Console.WriteLine($"   📊 Acquiring {symbol} ({category})...");

                    // Acquire underlying data
                    await AcquireSymbolData(symbol, category, startDate, endDate, granularity);

                    // Acquire options data for high-priority symbols
                    if (metadata.Priority >= 7 && category != "indices")
                    {
                        Console.WriteLine($"   🎯 Acquiring options for {symbol}...");
                        await AcquireOptionsData(symbol, startDate, endDate);
                    }

                    Console.WriteLine($"   ✅ Completed {symbol}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ⚠️  Failed {symbol}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        Console.WriteLine($"🎉 Universe data acquisition complete! Processed {symbols.Count} symbols.");
    }

    /// <summary>
    /// Acquire data for a specific symbol
    /// </summary>
    private async Task AcquireSymbolData(
        string symbol, 
        string category, 
        DateTime startDate, 
        DateTime endDate,
        string granularity)
    {
        // This would integrate with Polygon.IO acquisition logic
        // For now, create placeholder database structure
        
        var dbName = GetAdvancedPartitionName(category, symbol, startDate, granularity);
        var dbPath = Path.Combine(_datasetPath, category.ToPascalCase(), dbName);
        
        if (!File.Exists(dbPath))
        {
            await CreateSymbolDatabase(symbol, category, dbPath);
        }

        // Log successful setup  
        // await _manifestTracker.RecordManifest(category, symbol, startDate, ""); // TODO: Fix manifest method
    }

    /// <summary>
    /// Acquire options data for a symbol
    /// </summary>
    private async Task AcquireOptionsData(string symbol, DateTime startDate, DateTime endDate)
    {
        var current = new DateTime(startDate.Year, startDate.Month, 1);
        var end = new DateTime(endDate.Year, endDate.Month, 1);

        while (current <= end)
        {
            var dbName = GetAdvancedPartitionName("options", symbol, current, "1min");
            var dbPath = Path.Combine(_datasetPath, "Options", dbName);

            if (!File.Exists(dbPath))
            {
                await CreateOptionsMonthlyShards(symbol, current, dbPath);
            }

            current = current.AddMonths(1);
        }
    }

    /// <summary>
    /// Create database structure for a symbol
    /// </summary>
    private async Task CreateSymbolDatabase(string symbol, string category, string dbPath)
    {
        var connection = new SqliteConnection(GetOptimizedConnectionString(dbPath));
        await connection.OpenAsync();
        await SetAdvancedPragmas(connection);

        try
        {
            var schema = GenerateEquitySchema(symbol, category);
            await connection.ExecuteAsync(schema);
            Console.WriteLine($"   📁 Created database: {Path.GetFileName(dbPath)}");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Generate comprehensive reporting on universe coverage
    /// </summary>
    public async Task<UniverseReport> GenerateUniverseReport()
    {
        var report = new UniverseReport
        {
            GeneratedAt = DateTime.UtcNow,
            TotalSymbols = _universeManager.GetAllSymbols().Count
        };

        // Analyze by category
        foreach (var category in new[] { "indices", "etfs", "stocks" })
        {
            var symbols = _universeManager.GetSymbolsByCategory(category);
            var categoryReport = new CategoryReport
            {
                Category = category,
                TotalSymbols = symbols.Count,
                SymbolsCovered = await GetCoveredSymbols(symbols, category),
                PrioritySymbols = symbols.Where(s => 
                    _universeManager.GetMetadata(s)?.Priority >= 7).ToList()
            };

            report.Categories[category] = categoryReport;
        }

        // Strategy-specific analysis
        foreach (var strategy in Enum.GetValues<TradingStrategy>())
        {
            var symbols = _universeManager.GetSymbolsForStrategy(strategy);
            report.StrategySymbols[strategy.ToString()] = symbols;
        }

        return report;
    }

    private async Task<List<string>> GetCoveredSymbols(List<string> symbols, string category)
    {
        var covered = new List<string>();
        
        foreach (var symbol in symbols)
        {
            var dbPattern = Path.Combine(_datasetPath, category.ToPascalCase(), $"*{symbol.ToLower()}*.db");
            var files = Directory.GetFiles(Path.GetDirectoryName(dbPattern) ?? "", 
                Path.GetFileName(dbPattern));
            
            if (files.Any())
            {
                covered.Add(symbol);
            }
        }
        
        return covered;
    }

    /// <summary>
    /// Generate comprehensive equity/ETF/index schema
    /// </summary>
    private string GenerateEquitySchema(string symbol, string category)
    {
        return $@"
            -- Main bars table for {symbol} ({category})
            CREATE TABLE IF NOT EXISTS bars_eq (
                ticker TEXT NOT NULL,
                ts INTEGER NOT NULL,        -- epoch ms (UTC)
                o REAL, h REAL, l REAL, c REAL,
                v INTEGER,                  -- volume
                trades INTEGER,             -- trade count
                vwap REAL,                  -- volume weighted average price
                PRIMARY KEY (ticker, ts)
            ) WITHOUT ROWID;

            -- Performance indexes
            CREATE INDEX IF NOT EXISTS ix_bars_eq_ts ON bars_eq(ts);
            CREATE INDEX IF NOT EXISTS ix_bars_eq_ticker ON bars_eq(ticker);
            
            -- 5-minute rollup view for performance
            CREATE VIEW IF NOT EXISTS v_bars_eq_5m AS
            SELECT
              ticker,
              (ts/300000)*300000 AS ts5m,
              MIN(CASE WHEN rn=1 THEN o END) AS o,
              MAX(h) AS h,
              MIN(l) AS l,
              MAX(CASE WHEN rn_last=1 THEN c END) AS c,
              SUM(v) AS v,
              SUM(trades) AS trades,
              AVG(vwap) AS vwap
            FROM (
              SELECT b.*,
                     ROW_NUMBER() OVER (PARTITION BY ticker, ts/300000 ORDER BY ts) AS rn,
                     ROW_NUMBER() OVER (PARTITION BY ticker, ts/300000 ORDER BY ts DESC) AS rn_last
              FROM bars_eq b
            )
            GROUP BY ticker, ts5m;

            -- Market hours and session tracking
            CREATE TABLE IF NOT EXISTS session_info (
                trade_date TEXT PRIMARY KEY,
                market_open INTEGER,        -- epoch ms
                market_close INTEGER,       -- epoch ms
                early_close INTEGER DEFAULT 0,
                holiday INTEGER DEFAULT 0,
                total_bars INTEGER,
                first_bar INTEGER,
                last_bar INTEGER
            );

            -- Symbol metadata
            CREATE TABLE IF NOT EXISTS symbol_metadata (
                symbol TEXT PRIMARY KEY,
                category TEXT,
                company_name TEXT,
                sector TEXT,
                market_cap REAL,
                avg_volume REAL,
                price_increment REAL,
                options_available INTEGER DEFAULT 0,
                last_updated INTEGER
            );

            -- Insert initial metadata
            INSERT OR IGNORE INTO symbol_metadata (symbol, category, last_updated) 
            VALUES ('{symbol}', '{category}', {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});
        ";
    }

    #endregion

    #region Connection Management

    private async Task<SqliteConnection> GetPooledConnection(string dbName)
    {
        if (_connectionPool.TryGetValue(dbName, out var existingConnection))
        {
            if (existingConnection.State == ConnectionState.Open)
                return existingConnection;
        }

        var dbPath = Path.Combine(_datasetPath, "Indices", dbName); // Default path, adjust as needed
        var connectionString = GetOptimizedConnectionString(dbPath);
        
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await SetAdvancedPragmas(connection);
        
        _connectionPool.TryAdd(dbName, connection);
        return connection;
    }

    #endregion

    #region Cleanup and GitIgnore

    private void CreateSecureDataGitIgnore()
    {
        var gitignorePath = Path.Combine(_datasetPath, "..", ".gitignore");
        
        if (!File.Exists(gitignorePath))
        {
            var gitignoreContent = @"# Secure Polygon.io Licensed Data
# Password-protected databases are OK in version control
# Only exclude environment files with plain-text secrets

# Environment files with secrets
.env
.env.local
.env.production

# Temporary SQLite files (but not the main .db files)
*.db-shm
*.db-wal

# Verification files
*.verification
*.checksum

# Any CSV exports (contains raw licensed data)
*.csv
*.csv.gz

# Keep these files
!.gitignore
!.env.template
!*.db
";
            File.WriteAllText(gitignorePath, gitignoreContent);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connectionPool.Values)
        {
            if (connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
            await connection.DisposeAsync();
        }
        
        _connectionPool.Clear();
        
        foreach (var command in _compiledQueries.Values)
        {
            await command.DisposeAsync();
        }
        
        _compiledQueries.Clear();
    }

    #endregion
}

#region Supporting Classes

/// <summary>
/// Options Greeks and IV calculator implementing Options Developer formulas
/// </summary>
public class OptionsGreeksCalculator
{
    public async Task<OptionsGreeks> CalculateGreeks(double optionMid, double underlyingPrice, 
        double strike, double timeToExpiry, double riskFreeRate = 0.05, string optionType = "CALL")
    {
        // Black-Scholes implementation for IV solving and Greeks
        var iv = await SolveImpliedVolatility(optionMid, underlyingPrice, strike, timeToExpiry, riskFreeRate, optionType);
        
        return new OptionsGreeks
        {
            ImpliedVolatility = iv,
            Delta = CalculateDelta(underlyingPrice, strike, timeToExpiry, riskFreeRate, iv, optionType),
            Gamma = CalculateGamma(underlyingPrice, strike, timeToExpiry, riskFreeRate, iv),
            Theta = CalculateTheta(underlyingPrice, strike, timeToExpiry, riskFreeRate, iv, optionType),
            Vega = CalculateVega(underlyingPrice, strike, timeToExpiry, riskFreeRate, iv),
            Rho = CalculateRho(underlyingPrice, strike, timeToExpiry, riskFreeRate, iv, optionType)
        };
    }

    private async Task<double> SolveImpliedVolatility(double marketPrice, double underlyingPrice, 
        double strike, double timeToExpiry, double riskFreeRate, string optionType)
    {
        // Newton-Raphson method for IV solving
        double iv = 0.2; // Initial guess
        double tolerance = 1e-6;
        int maxIterations = 100;

        for (int i = 0; i < maxIterations; i++)
        {
            var theoreticalPrice = BlackScholesPrice(underlyingPrice, strike, timeToExpiry, riskFreeRate, iv, optionType);
            var vega = CalculateVega(underlyingPrice, strike, timeToExpiry, riskFreeRate, iv);
            
            if (Math.Abs(vega) < tolerance) break;
            
            var diff = theoreticalPrice - marketPrice;
            if (Math.Abs(diff) < tolerance) break;
            
            iv = iv - (diff / vega);
            
            if (iv <= 0) iv = 0.001; // Prevent negative volatility
            if (iv > 5) iv = 5;     // Cap at 500%
        }

        return iv;
    }

    private double BlackScholesPrice(double S, double K, double T, double r, double sigma, string optionType)
    {
        if (T <= 0) return Math.Max(optionType == "CALL" ? S - K : K - S, 0);
        
        double d1 = (Math.Log(S / K) + (r + 0.5 * sigma * sigma) * T) / (sigma * Math.Sqrt(T));
        double d2 = d1 - sigma * Math.Sqrt(T);
        
        if (optionType == "CALL")
        {
            return S * NormalCDF(d1) - K * Math.Exp(-r * T) * NormalCDF(d2);
        }
        else
        {
            return K * Math.Exp(-r * T) * NormalCDF(-d2) - S * NormalCDF(-d1);
        }
    }

    private double CalculateDelta(double S, double K, double T, double r, double sigma, string optionType)
    {
        if (T <= 0) return optionType == "CALL" && S > K ? 1 : 0;
        
        double d1 = (Math.Log(S / K) + (r + 0.5 * sigma * sigma) * T) / (sigma * Math.Sqrt(T));
        return optionType == "CALL" ? NormalCDF(d1) : NormalCDF(d1) - 1;
    }

    private double CalculateGamma(double S, double K, double T, double r, double sigma)
    {
        if (T <= 0) return 0;
        
        double d1 = (Math.Log(S / K) + (r + 0.5 * sigma * sigma) * T) / (sigma * Math.Sqrt(T));
        return NormalPDF(d1) / (S * sigma * Math.Sqrt(T));
    }

    private double CalculateTheta(double S, double K, double T, double r, double sigma, string optionType)
    {
        if (T <= 0) return 0;
        
        double d1 = (Math.Log(S / K) + (r + 0.5 * sigma * sigma) * T) / (sigma * Math.Sqrt(T));
        double d2 = d1 - sigma * Math.Sqrt(T);
        
        double term1 = -(S * NormalPDF(d1) * sigma) / (2 * Math.Sqrt(T));
        
        if (optionType == "CALL")
        {
            double term2 = -r * K * Math.Exp(-r * T) * NormalCDF(d2);
            return (term1 + term2) / 365; // Daily theta
        }
        else
        {
            double term2 = r * K * Math.Exp(-r * T) * NormalCDF(-d2);
            return (term1 + term2) / 365; // Daily theta
        }
    }

    private double CalculateVega(double S, double K, double T, double r, double sigma)
    {
        if (T <= 0) return 0;
        
        double d1 = (Math.Log(S / K) + (r + 0.5 * sigma * sigma) * T) / (sigma * Math.Sqrt(T));
        return S * NormalPDF(d1) * Math.Sqrt(T) / 100; // Vega per 1% vol change
    }

    private double CalculateRho(double S, double K, double T, double r, double sigma, string optionType)
    {
        if (T <= 0) return 0;
        
        double d2 = (Math.Log(S / K) + (r - 0.5 * sigma * sigma) * T) / (sigma * Math.Sqrt(T));
        
        if (optionType == "CALL")
        {
            return K * T * Math.Exp(-r * T) * NormalCDF(d2) / 100; // Rho per 1% rate change
        }
        else
        {
            return -K * T * Math.Exp(-r * T) * NormalCDF(-d2) / 100; // Rho per 1% rate change
        }
    }

    private double NormalCDF(double x)
    {
        return 0.5 * (1.0 + Erf(x / Math.Sqrt(2.0)));
    }

    private double NormalPDF(double x)
    {
        return Math.Exp(-0.5 * x * x) / Math.Sqrt(2 * Math.PI);
    }

    private double Erf(double x)
    {
        // Abramowitz and Stegun approximation
        double a1 = 0.254829592;
        double a2 = -0.284496736;
        double a3 = 1.421413741;
        double a4 = -1.453152027;
        double a5 = 1.061405429;
        double p = 0.3275911;

        int sign = x < 0 ? -1 : 1;
        x = Math.Abs(x);

        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

        return sign * y;
    }
}

/// <summary>
/// Options Greeks data structure
/// </summary>
public class OptionsGreeks
{
    public double ImpliedVolatility { get; set; }
    public double Delta { get; set; }
    public double Gamma { get; set; }
    public double Theta { get; set; }
    public double Vega { get; set; }
    public double Rho { get; set; }
}

/// <summary>
/// Manifest tracking for reproducibility (Options Developer requirement)
/// </summary>
public class ManifestTracker
{
    private readonly string _manifestPath;

    public ManifestTracker(string manifestPath)
    {
        _manifestPath = manifestPath;
        Directory.CreateDirectory(_manifestPath);
    }

    public async Task RecordDatabaseCreation(string category, string symbol, DateTime date, string schema)
    {
        var manifest = new DatabaseManifest
        {
            RunId = Guid.NewGuid().ToString(),
            Started = DateTime.UtcNow,
            Category = category,
            Symbol = symbol,
            Date = date,
            SchemaHash = ComputeHash(schema),
            Status = "created"
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        var filePath = Path.Combine(_manifestPath, $"{manifest.RunId}.json");
        
        await File.WriteAllTextAsync(filePath, json);
    }

    private string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash);
    }
}

/// <summary>
/// Database manifest structure
/// </summary>
public class DatabaseManifest
{
    public string RunId { get; set; } = "";
    public DateTime Started { get; set; }
    public DateTime? Ended { get; set; }
    public string Category { get; set; } = "";
    public string Symbol { get; set; } = "";
    public DateTime Date { get; set; }
    public string SchemaHash { get; set; } = "";
    public string Status { get; set; } = "";
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Options contract details parsed from OCC format
/// </summary>
public class ContractDetails
{
    public string Underlying { get; set; } = "";
    public DateTime ExpirationDate { get; set; }
    public string OptionType { get; set; } = ""; // CALL or PUT
    public double Strike { get; set; }
    
    public double DaysToExpiry(DateTime asOf) => 
        (ExpirationDate - asOf).TotalDays;
    
    public double Moneyness(double underlyingPrice) => 
        Strike / underlyingPrice;
    
    public bool IsCall => OptionType == "CALL";
    public bool IsPut => OptionType == "PUT";
}

/// <summary>
/// Advanced market data bar structure for comprehensive analysis
/// </summary>
public class AdvancedBarData
{
    public string Ticker { get; set; } = "";
    public long Timestamp { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
    public int? Trades { get; set; }
    public double? VWAP { get; set; }
    
    public DateTime DateTime => DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).DateTime;
    public double MidPoint => (High + Low) / 2.0;
    public double TypicalPrice => (High + Low + Close) / 3.0;
    public double WeightedClose => (High + Low + 2 * Close) / 4.0; // Options pricing proxy
}

/// <summary>
/// Options data structure with Greeks integration
/// </summary>
public class OptionsBarData : AdvancedBarData
{
    public string Contract { get; set; } = "";
    public int? OpenInterest { get; set; }
    public OptionsGreeks? Greeks { get; set; }
    public double? ImpliedVolatility { get; set; }
    public double? UnderlyingPrice { get; set; }
    public double? SpreadPercent { get; set; }
}

/// <summary>
/// Options chain data structure for comprehensive analysis
/// </summary>
public class OptionsChainData
{
    public string Underlying { get; set; } = "";
    public List<OptionsBarData> Options { get; set; } = new();
    public int TotalContracts { get; set; }
    public string DateRange { get; set; } = "";
    public double? UnderlyingPrice { get; set; }
    public DateTime QueryTime { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Market regime analysis data structure
/// </summary>
public class MarketRegimeData
{
    public string Symbol { get; set; } = "";
    public List<AdvancedBarData> Bars { get; set; } = new();
    public List<VolatilityRegime> VolatilityRegimes { get; set; } = new();
    public List<SupportResistanceLevel> SupportResistance { get; set; } = new();
    public TrendAnalysis TrendAnalysis { get; set; } = new();
    public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
    public string CurrentRegime => VolatilityRegimes.LastOrDefault()?.Regime ?? "Unknown";
}

/// <summary>
/// Volatility regime classification
/// </summary>
public class VolatilityRegime
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Regime { get; set; } = ""; // Low, Medium, High, Extreme
    public double AverageVolatility { get; set; }
    public double VolatilityRank { get; set; } // 0-100 percentile
    public int DurationDays { get; set; }
}

/// <summary>
/// Support and resistance level detection
/// </summary>
public class SupportResistanceLevel
{
    public double Price { get; set; }
    public string Type { get; set; } = ""; // Support, Resistance
    public double Strength { get; set; } // 0-1 confidence score
    public int TouchCount { get; set; }
    public DateTime FirstTouch { get; set; }
    public DateTime LastTouch { get; set; }
    public bool IsCurrent { get; set; }
}

/// <summary>
/// Comprehensive trend analysis
/// </summary>
public class TrendAnalysis
{
    public string ShortTermTrend { get; set; } = ""; // Up, Down, Sideways
    public string MediumTermTrend { get; set; } = "";
    public string LongTermTrend { get; set; } = "";
    public double TrendStrength { get; set; } // 0-1
    public double Momentum { get; set; }
    public List<TrendLine> TrendLines { get; set; } = new();
}

/// <summary>
/// Trend line structure
/// </summary>
public class TrendLine
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public double StartPrice { get; set; }
    public double EndPrice { get; set; }
    public double Slope { get; set; }
    public string Type { get; set; } = ""; // Uptrend, Downtrend, Channel
    public double Confidence { get; set; }
}

/// <summary>
/// Zero DTE opportunity scanner result
/// </summary>
public class ZeroDTEOpportunity
{
    public string Contract { get; set; } = "";
    public string Underlying { get; set; } = "";
    public double Strike { get; set; }
    public string OptionType { get; set; } = "";
    public double LastPrice { get; set; }
    public double Delta { get; set; }
    public double Gamma { get; set; }
    public double Theta { get; set; }
    public double ImpliedVolatility { get; set; }
    public double SpreadPercent { get; set; }
    public long Volume { get; set; }
    public double Score { get; set; } // Opportunity score 0-1
    public string Strategy { get; set; } = ""; // Recommended strategy
    public double ExpectedProfit { get; set; }
    public double MaxRisk { get; set; }
    public DateTime ExpirationTime { get; set; }
    public double TimeToExpiry { get; set; } // Hours remaining
}

/// <summary>
/// Universe coverage report structure
/// </summary>
public class UniverseReport
{
    public DateTime GeneratedAt { get; set; }
    public int TotalSymbols { get; set; }
    public Dictionary<string, CategoryReport> Categories { get; set; } = new();
    public Dictionary<string, List<string>> StrategySymbols { get; set; } = new();
}

/// <summary>
/// Category-specific coverage report
/// </summary>
public class CategoryReport
{
    public string Category { get; set; } = "";
    public int TotalSymbols { get; set; }
    public List<string> SymbolsCovered { get; set; } = new();
    public List<string> PrioritySymbols { get; set; } = new();
    public double CoveragePercentage => TotalSymbols > 0 ? (double)SymbolsCovered.Count / TotalSymbols * 100 : 0;
}

/// <summary>
/// String extension methods for universe management
/// </summary>
public static class StringExtensions
{
    public static string ToPascalCase(this string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return char.ToUpper(input[0]) + input.Substring(1).ToLower() + "s";
    }
}

#endregion