# Stroll.Dataset — Distributed Query Guide

**Goal:** Fast, efficient access to 25+ years of distributed market data across sharded SQLite databases with options-focused optimizations.

---

## 🚀 Quick Access Patterns (MCP Service Ready)

### **Single Symbol, Time Range**
```csharp
// Get SPX 1-minute data for last month (auto-distributed)
var data = await dataset.QueryAcrossTimeRange<BarData>(
    "indices", "SPX", 
    DateTime.Now.AddMonths(-1), DateTime.Now, 
    "1min"
);
```

### **Options with Greeks**
```csharp
// Get SPX options with computed Greeks for expiration week
var options = await dataset.QueryOptionsWithGreeks(
    "SPX", expirationDate.AddDays(-7), expirationDate,
    atmWindow: 10  // ±10 strikes around ATM
);
```

### **Cross-Database Aggregation**
```csharp
// Get 5-year view with automatic 5-minute rollups
var longTerm = await dataset.QueryAcrossTimeRange<BarData>(
    "indices", "SPX", 
    DateTime.Now.AddYears(-5), DateTime.Now,
    "5min"  // Uses distributed ATTACH strategy
);
```

---

## 📊 Database Architecture

### **Partitioning Strategy**
```
SecureData/Polygon/
├── Indices/
│   ├── indices_spx_2025.db           # 1-min yearly
│   ├── indices_spx_2020_2024_5m.db   # 5-min 5-year
│   └── indices_vix_2025.db
├── Options/
│   ├── options_spx_2025_01.db        # Monthly sharding
│   ├── options_spx_2025_02.db
│   └── options_spy_2025_08.db
├── ETFs/
│   ├── etfs_spy_2025.db
│   └── etfs_gld_2020_2024_5m.db
├── Stocks/
│   ├── stocks_nvda_2025.db
│   └── stocks_pltr_2025.db
└── Manifests/
    └── creation_logs/
```

### **Schema Types by Category**

#### **Equities/Indices (Optimized OHLCV)**
```sql
CREATE TABLE bars_eq (
    ticker TEXT NOT NULL,
    ts INTEGER NOT NULL,        -- epoch ms (UTC)
    o REAL, h REAL, l REAL, c REAL,
    v INTEGER, trades INTEGER, vwap REAL,
    PRIMARY KEY (ticker, ts)
) WITHOUT ROWID;

-- Dynamic 5-minute rollups
CREATE VIEW v_bars_eq_5m AS
SELECT ticker, (ts/300000)*300000 AS ts5m,
       MIN(CASE WHEN rn=1 THEN o END) AS o,
       MAX(h) AS h, MIN(l) AS l,
       MAX(CASE WHEN rn_last=1 THEN c END) AS c,
       SUM(v) AS v
FROM (SELECT *, ROW_NUMBER() OVER (...) AS rn FROM bars_eq)
GROUP BY ticker, ts5m;
```

#### **Options (Monthly Shards + Greeks)**
```sql
-- Per month: op_aggs_spx_2025_08
CREATE TABLE op_aggs_spx_2025_08 (
    contract TEXT NOT NULL,     -- OCC: O:SPXW240830C05500000
    ts INTEGER NOT NULL,
    o REAL, h REAL, l REAL, c REAL,
    v INTEGER, oi INTEGER, trades INTEGER,
    PRIMARY KEY (contract, ts)
) WITHOUT ROWID;

-- Derived Greeks
CREATE TABLE op_iv_greeks (
    contract TEXT, ts INTEGER,
    iv REAL, delta REAL, gamma REAL, theta REAL, vega REAL,
    ref_px REAL, mid_px REAL, spread_pct REAL,
    PRIMARY KEY (contract, ts)
);
```

---

## ⚡ High-Performance Query Patterns

### **1. Time-Range Distributed Query**
```csharp
// Automatic database discovery and ATTACH
public async Task<List<T>> QueryAcrossTimeRange<T>(
    string category, string symbol, 
    DateTime start, DateTime end, string granularity)
{
    // 1. Discover required databases
    var dbs = GetRequiredDatabases(category, symbol, start, end, granularity);
    
    // 2. Primary connection + ATTACH others
    var conn = await GetPooledConnection(dbs.First());
    foreach(var db in dbs.Skip(1)) {
        await conn.ExecuteAsync($"ATTACH '{db}' AS db{i} KEY '{password}'");
    }
    
    // 3. Build UNION ALL query across attached DBs
    var query = BuildDistributedUnion(dbs, start, end);
    
    // 4. Execute with compiled parameters
    return await conn.QueryAsync<T>(query, new { startTs, endTs });
}
```

### **2. Options Greeks Pipeline**
```csharp
// Real-time Greeks computation with caching
public async Task<OptionsData> GetOptionsWithGreeks(
    string underlying, DateTime date, int atmWindow = 5)
{
    // 1. Get underlying price
    var underlyingPrice = await GetLatestPrice(underlying, date);
    
    // 2. Discover ATM contracts ±window
    var contracts = await DiscoverATMContracts(underlying, date, atmWindow);
    
    // 3. Get option prices + compute Greeks
    var options = new List<OptionsData>();
    foreach(var contract in contracts) {
        var optionBars = await QueryContract(contract, date);
        var greeks = await _greeksCalculator.CalculateGreeks(
            optionBars.LastOrDefault()?.Close ?? 0,
            underlyingPrice, contract.Strike, contract.TimeToExpiry
        );
        
        options.Add(new OptionsData { 
            Contract = contract, 
            Greeks = greeks,
            LastPrice = optionBars.LastOrDefault()?.Close ?? 0
        });
    }
    
    return new OptionsData { Underlying = underlying, Options = options };
}
```

### **3. Cross-Year Aggregation**
```csharp
// Efficient 5-year historical analysis
public async Task<MarketRegimeData> AnalyzeRegimes(string symbol, int years = 5)
{
    // Use 5-minute 5-year partitions for speed
    var data = await QueryAcrossTimeRange<BarData>(
        "indices", symbol,
        DateTime.Now.AddYears(-years), DateTime.Now,
        "5min"  // Single database hit for 5 years
    );
    
    // Rolling statistics
    return new MarketRegimeData {
        VolRegimes = CalculateVolRegimes(data),
        SupportResistance = FindKeyLevels(data),
        SeasonalPatterns = AnalyzeSeasonality(data)
    };
}
```

---

## 🔧 MCP Service Integration

### **Fast Data Access Endpoints**
```csharp
[McpTool("get_market_data")]
public async Task<MarketDataResponse> GetMarketData(
    string symbol, string period = "1M", string granularity = "1min")
{
    var (start, end) = ParsePeriod(period);
    var category = DetectCategory(symbol); // indices/options/etfs/stocks
    
    var data = await _dataset.QueryAcrossTimeRange<BarData>(
        category, symbol, start, end, granularity
    );
    
    return new MarketDataResponse {
        Symbol = symbol,
        Period = period,
        DataPoints = data.Count(),
        Data = data.Take(1000).ToList(), // Limit for MCP
        Summary = CalculateSummary(data)
    };
}

[McpTool("get_options_chain")]
public async Task<OptionsChainResponse> GetOptionsChain(
    string underlying, string expiration, int atmWindow = 10)
{
    var expiry = DateTime.Parse(expiration);
    var options = await _dataset.QueryOptionsWithGreeks(
        underlying, expiry.AddDays(-1), expiry, atmWindow
    );
    
    return new OptionsChainResponse {
        Underlying = underlying,
        Expiration = expiration,
        AtmPrice = options.UnderlyingPrice,
        Calls = options.Options.Where(o => o.OptionType == "CALL").ToList(),
        Puts = options.Options.Where(o => o.OptionType == "PUT").ToList()
    };
}

[McpTool("analyze_market_regime")]
public async Task<MarketRegimeResponse> AnalyzeMarketRegime(
    string symbol, string timeframe = "1Y")
{
    var years = timeframe.EndsWith("Y") ? int.Parse(timeframe[..^1]) : 1;
    var regime = await _dataset.AnalyzeRegimes(symbol, years);
    
    return new MarketRegimeResponse {
        Symbol = symbol,
        Timeframe = timeframe,
        CurrentRegime = regime.CurrentRegime,
        VolatilityRank = regime.VolRank,
        KeyLevels = regime.SupportResistance,
        Recommendation = GenerateRecommendation(regime)
    };
}
```

### **Query Performance Optimization**
```csharp
// Connection pooling with database rotation
private readonly ConcurrentDictionary<string, SqliteConnection> _connectionPool = new();

// Compiled query caching
private readonly ConcurrentDictionary<string, SqliteCommand> _compiledQueries = new();

// Smart caching with TTL
private readonly MemoryCache _queryCache = new(new MemoryCacheOptions {
    SizeLimit = 1000,
    CompactionPercentage = 0.25
});

public async Task<T> GetCachedQuery<T>(string key, Func<Task<T>> factory, 
    TimeSpan? ttl = null)
{
    if (_queryCache.TryGetValue(key, out T cached)) return cached;
    
    var result = await factory();
    _queryCache.Set(key, result, ttl ?? TimeSpan.FromMinutes(5));
    return result;
}
```

---

## 📈 Advanced Query Examples

### **0DTE Scanner**
```csharp
// Find today's 0DTE opportunities
public async Task<List<ZeroDTEOpportunity>> Scan0DTE(DateTime tradingDay)
{
    var opportunities = new List<ZeroDTEOpportunity>();
    var underlyings = new[] { "SPX", "SPY", "XSP" };
    
    foreach(var underlying in underlyings) {
        // Get ATM options expiring today
        var options = await QueryOptionsWithGreeks(
            underlying, tradingDay, tradingDay.AddHours(16), 
            atmWindow: 5
        );
        
        // Filter by liquidity and spreads
        var liquid = options.Options.Where(o => 
            o.Volume > 10 && 
            o.SpreadPct < 0.15 &&  // <15% spread
            Math.Abs(o.Greeks.Delta) > 0.1
        ).ToList();
        
        opportunities.AddRange(liquid.Select(o => new ZeroDTEOpportunity {
            Contract = o.Contract,
            Delta = o.Greeks.Delta,
            Gamma = o.Greeks.Gamma,
            Theta = o.Greeks.Theta,
            SpreadPct = o.SpreadPct,
            Score = CalculateOpportunityScore(o)
        }));
    }
    
    return opportunities.OrderByDescending(o => o.Score).Take(20).ToList();
}
```

### **LEAPS Analysis**
```csharp
// Long-term options analysis
public async Task<LEAPSAnalysis> AnalyzeLEAPS(
    string symbol, List<DateTime> expiries)
{
    var analysis = new LEAPSAnalysis { Symbol = symbol };
    
    foreach(var expiry in expiries.Where(e => e > DateTime.Now.AddMonths(9))) {
        var options = await QueryOptionsWithGreeks(symbol, 
            DateTime.Now.AddDays(-30), DateTime.Now, atmWindow: 20);
        
        // Filter to this expiration
        var expiryOptions = options.Options.Where(o => 
            o.Contract.ExpirationDate.Date == expiry.Date).ToList();
        
        analysis.ExpiryAnalysis[expiry] = new ExpiryAnalysis {
            TotalVolume = expiryOptions.Sum(o => o.Volume),
            OpenInterest = expiryOptions.Sum(o => o.OpenInterest),
            IVRank = CalculateIVRank(expiryOptions),
            CallPutRatio = CalculateCallPutRatio(expiryOptions),
            MaxPain = CalculateMaxPain(expiryOptions)
        };
    }
    
    return analysis;
}
```

---

## 🏎️ Performance Benchmarks

### **Query Performance Targets**
- **Single symbol, 1 day**: <50ms
- **Single symbol, 1 month**: <200ms  
- **Single symbol, 1 year**: <1s
- **Options chain, current expiry**: <100ms
- **Greeks computation, 100 contracts**: <300ms
- **5-year historical analysis**: <2s

### **Optimization Techniques**
1. **Connection Pooling**: Reuse encrypted connections
2. **Query Compilation**: PRAGMA prepare for repeated queries
3. **Smart Sharding**: Monthly for options, yearly for equities
4. **ATTACH Strategy**: Cross-database queries without data copying
5. **View Rollups**: Pre-computed 5-minute aggregations
6. **Result Caching**: Memory cache with TTL for frequent queries

---

## 🔒 Security & Compliance

### **Data Protection**
- **Password Protection**: All databases encrypted with `POLYGON_DB_PASSWORD`
- **Environment Variables**: No hardcoded credentials
- **Git Safety**: Databases safe in version control (encrypted)
- **License Compliance**: Private repository for raw CSV data

### **Access Patterns**
```csharp
// Environment-based password management
private string GetSecurePassword() =>
    Environment.GetEnvironmentVariable("POLYGON_DB_PASSWORD") ?? 
    throw new InvalidOperationException("POLYGON_DB_PASSWORD required");

// Secure connection string
private string GetConnectionString(string dbPath) =>
    $"Data Source={dbPath};Password={GetSecurePassword()};Cache Size=100000;Journal Mode=WAL";
```

---

**Maintained by:** Stroll.Dataset  
**Integration:** Stroll.TestMcp → AdvancedPolygonDataset → MCP Tools  
**Performance:** Sub-second queries across 25+ years of distributed data