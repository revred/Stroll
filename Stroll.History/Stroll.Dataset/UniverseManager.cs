using System.Text.Json;

namespace Stroll.Dataset;

/// <summary>
/// Comprehensive symbol universe manager for Stroll dataset
/// Manages indices, ETFs, stocks, and options as defined in Options Developer guide
/// </summary>
public class UniverseManager
{
    private readonly Dictionary<string, List<string>> _universe;
    private readonly Dictionary<string, SymbolMetadata> _metadata;
    
    public UniverseManager()
    {
        _universe = InitializeUniverse();
        _metadata = InitializeMetadata();
    }

    /// <summary>
    /// Initialize the comprehensive trading universe based on Options Developer requirements
    /// </summary>
    private Dictionary<string, List<string>> InitializeUniverse()
    {
        return new Dictionary<string, List<string>>
        {
            // Indices - Core US market indices with Polygon format
            ["indices"] = new()
            {
                "I:SPX",     // S&P 500 Index
                "I:VIX",     // CBOE Volatility Index  
                "I:DJI",     // Dow Jones Industrial Average
                "I:NDX",     // NASDAQ 100 Index
                "I:RUT",     // Russell 2000 Index
                "I:XOI",     // AMEX Oil Index (Oil/Energy)
                "I:XAU",     // Philadelphia Gold/Silver Index
                "I:SOX",     // Philadelphia Semiconductor Index
                "I:XNG",     // AMEX Natural Gas Index
                "I:HUI",     // NYSE Arca Gold BUGS Index
            },

            // ETFs - High-volume, liquid ETFs with active options markets
            ["etfs"] = new()
            {
                // Core Market ETFs
                "SPY",       // SPDR S&P 500 ETF Trust
                "QQQ",       // Invesco QQQ Trust
                "IWM",       // iShares Russell 2000 ETF
                "DIA",       // SPDR Dow Jones Industrial Average ETF
                
                // Sector ETFs
                "XLF",       // Financial Select Sector SPDR Fund
                "XLK",       // Technology Select Sector SPDR Fund
                "XLE",       // Energy Select Sector SPDR Fund
                "XLV",       // Health Care Select Sector SPDR Fund
                "XLI",       // Industrial Select Sector SPDR Fund
                "XLU",       // Utilities Select Sector SPDR Fund
                "XLY",       // Consumer Discretionary Select Sector SPDR Fund
                "XLP",       // Consumer Staples Select Sector SPDR Fund
                "XLB",       // Materials Select Sector SPDR Fund
                "XLRE",      // Real Estate Select Sector SPDR Fund
                
                // Commodities & Precious Metals
                "GLD",       // SPDR Gold Trust
                "SLV",       // iShares Silver Trust
                "USO",       // United States Oil Fund
                "UNG",       // United States Natural Gas Fund
                "DBA",       // Invesco DB Agriculture Fund
                
                // International
                "EEM",       // iShares MSCI Emerging Markets ETF
                "FXI",       // iShares China Large-Cap ETF
                "EWJ",       // iShares MSCI Japan ETF
                "EFA",       // iShares MSCI EAFE ETF
                
                // Volatility & Strategy ETFs
                "VIX",       // iPath Series B S&P 500 VIX Short-Term Futures ETN
                "UVXY",      // ProShares Ultra VIX Short-Term Futures ETF
                "SQQQ",      // ProShares UltraPro Short QQQ
                "TQQQ",      // ProShares UltraPro QQQ
                "SPXU",      // ProShares UltraPro Short S&P500
                "UPRO",      // ProShares UltraPro S&P500
            },

            // Stocks - High-volume stocks with active options markets
            ["stocks"] = new()
            {
                // Technology - FAANG + Mega Caps
                "AAPL",      // Apple Inc.
                "MSFT",      // Microsoft Corporation  
                "GOOGL",     // Alphabet Inc. Class A
                "GOOG",      // Alphabet Inc. Class C
                "AMZN",      // Amazon.com Inc.
                "META",      // Meta Platforms Inc.
                "TSLA",      // Tesla Inc.
                "NFLX",      // Netflix Inc.
                "NVDA",      // NVIDIA Corporation
                "AMD",       // Advanced Micro Devices Inc.
                "INTC",      // Intel Corporation
                "CRM",       // Salesforce Inc.
                "ORCL",      // Oracle Corporation
                "ADBE",      // Adobe Inc.
                
                // High-Volume Options Plays (Popular with retail)
                "PLTR",      // Palantir Technologies Inc.
                "SOFI",      // SoFi Technologies Inc.
                "AMC",       // AMC Entertainment Holdings Inc.
                "GME",       // GameStop Corp.
                "MARA",      // Marathon Digital Holdings Inc.
                "RIOT",      // Riot Platforms Inc.
                "BB",        // BlackBerry Limited
                "NOK",       // Nokia Corporation
                "WISH",      // ContextLogic Inc.
                "CLOV",      // Clover Health Investments Corp.
                
                // Financial Services  
                "JPM",       // JPMorgan Chase & Co.
                "BAC",       // Bank of America Corporation
                "WFC",       // Wells Fargo & Company
                "GS",        // Goldman Sachs Group Inc.
                "MS",        // Morgan Stanley
                "C",         // Citigroup Inc.
                
                // Healthcare & Biotech
                "UNH",       // UnitedHealth Group Inc.
                "JNJ",       // Johnson & Johnson
                "PFE",       // Pfizer Inc.
                "MRNA",      // Moderna Inc.
                "BNTX",      // BioNTech SE
                
                // Energy
                "XOM",       // Exxon Mobil Corporation
                "CVX",       // Chevron Corporation
                "COP",       // ConocoPhillips
                
                // Consumer & Retail
                "WMT",       // Walmart Inc.
                "HD",        // Home Depot Inc.
                "DIS",       // Walt Disney Company
                "NKE",       // Nike Inc.
                "SBUX",      // Starbucks Corporation
                
                // Transportation & Automotive  
                "F",         // Ford Motor Company
                "GM",        // General Motors Company
                "UBER",      // Uber Technologies Inc.
                "LYFT",      // Lyft Inc.
                
                // Communication Services
                "T",         // AT&T Inc.
                "VZ",        // Verizon Communications Inc.
                "NFLX",      // Netflix Inc. (duplicate, but keeping for options focus)
                
                // Industrials
                "BA",        // Boeing Company
                "CAT",       // Caterpillar Inc.
                "GE",        // General Electric Company
                
                // Real Estate & REITs
                "SPG",       // Simon Property Group Inc.
                
                // Utilities
                "NEE",       // NextEra Energy Inc.
            }
        };
    }

    /// <summary>
    /// Initialize metadata for symbols including option chain characteristics
    /// </summary>
    private Dictionary<string, SymbolMetadata> InitializeMetadata()
    {
        var metadata = new Dictionary<string, SymbolMetadata>();

        // Add metadata for key symbols
        foreach (var category in _universe)
        {
            foreach (var symbol in category.Value)
            {
                metadata[symbol] = CreateSymbolMetadata(symbol, category.Key);
            }
        }

        return metadata;
    }

    private SymbolMetadata CreateSymbolMetadata(string symbol, string category)
    {
        return symbol switch
        {
            // Indices - European-style options, cash settlement
            "I:SPX" => new SymbolMetadata 
            { 
                Symbol = symbol, 
                Category = category, 
                OptionStyle = "European", 
                Settlement = "Cash",
                TypicalVolume = 1000000,
                MinStrikeIncrement = 5.0,
                PreferredDTE = new[] { 0, 1, 7, 14, 30, 60, 90 },
                OptionsSymbol = "SPXW",
                Priority = 10
            },
            
            "I:VIX" => new SymbolMetadata 
            { 
                Symbol = symbol, 
                Category = category, 
                OptionStyle = "European", 
                Settlement = "Cash",
                TypicalVolume = 500000,
                MinStrikeIncrement = 1.0,
                PreferredDTE = new[] { 0, 1, 7, 14, 30 },
                OptionsSymbol = "VIX",
                Priority = 9
            },

            // Major ETFs - American-style, physical delivery/cash
            "SPY" => new SymbolMetadata 
            { 
                Symbol = symbol, 
                Category = category, 
                OptionStyle = "American", 
                Settlement = "Physical",
                TypicalVolume = 2000000,
                MinStrikeIncrement = 1.0,
                PreferredDTE = new[] { 0, 1, 2, 7, 14, 30, 60, 90 },
                OptionsSymbol = "SPY",
                Priority = 10
            },

            "QQQ" => new SymbolMetadata 
            { 
                Symbol = symbol, 
                Category = category, 
                OptionStyle = "American", 
                Settlement = "Physical",
                TypicalVolume = 1500000,
                MinStrikeIncrement = 1.0,
                PreferredDTE = new[] { 0, 1, 7, 14, 30, 60, 90 },
                OptionsSymbol = "QQQ",
                Priority = 9
            },

            // High-priority individual stocks
            var s when new[] { "AAPL", "MSFT", "TSLA", "NVDA", "AMZN" }.Contains(s) => new SymbolMetadata 
            { 
                Symbol = symbol, 
                Category = category, 
                OptionStyle = "American", 
                Settlement = "Physical",
                TypicalVolume = 1000000,
                MinStrikeIncrement = 2.5,
                PreferredDTE = new[] { 0, 1, 7, 14, 30, 60, 90, 180, 365 },
                OptionsSymbol = symbol,
                Priority = 8
            },

            // Meme stocks - high volatility, retail interest
            var s when new[] { "PLTR", "SOFI", "AMC", "GME" }.Contains(s) => new SymbolMetadata 
            { 
                Symbol = symbol, 
                Category = category, 
                OptionStyle = "American", 
                Settlement = "Physical",
                TypicalVolume = 500000,
                MinStrikeIncrement = 1.0,
                PreferredDTE = new[] { 0, 1, 7, 14, 30, 60 },
                OptionsSymbol = symbol,
                Priority = 6
            },

            // Default metadata
            _ => new SymbolMetadata 
            { 
                Symbol = symbol, 
                Category = category, 
                OptionStyle = "American", 
                Settlement = "Physical",
                TypicalVolume = 100000,
                MinStrikeIncrement = 1.0,
                PreferredDTE = new[] { 0, 1, 7, 14, 30, 60, 90 },
                OptionsSymbol = symbol,
                Priority = 5
            }
        };
    }

    /// <summary>
    /// Get all symbols by category
    /// </summary>
    public List<string> GetSymbolsByCategory(string category)
    {
        return _universe.TryGetValue(category.ToLower(), out var symbols) ? symbols : new List<string>();
    }

    /// <summary>
    /// Get high-priority symbols for intensive data collection
    /// </summary>
    public List<string> GetPrioritySymbols(int minPriority = 7)
    {
        return _metadata.Values
            .Where(m => m.Priority >= minPriority)
            .OrderByDescending(m => m.Priority)
            .Select(m => m.Symbol)
            .ToList();
    }

    /// <summary>
    /// Get 0DTE-focused symbols (indices and high-volume ETFs/stocks)
    /// </summary>
    public List<string> GetZeroDTESymbols()
    {
        var symbols = new List<string>();
        
        // Add major indices
        symbols.AddRange(GetSymbolsByCategory("indices").Where(s => 
            new[] { "I:SPX", "I:VIX" }.Contains(s)));
        
        // Add major ETFs  
        symbols.AddRange(GetSymbolsByCategory("etfs").Where(s => 
            new[] { "SPY", "QQQ", "IWM" }.Contains(s)));
        
        // Add high-volume stocks
        symbols.AddRange(GetSymbolsByCategory("stocks").Where(s => 
            new[] { "AAPL", "MSFT", "TSLA", "NVDA", "AMZN" }.Contains(s)));
        
        return symbols;
    }

    /// <summary>
    /// Get LEAPS-focused symbols (9+ month options)
    /// </summary>
    public List<string> GetLEAPSSymbols()
    {
        return _metadata.Values
            .Where(m => m.PreferredDTE.Contains(180) || m.PreferredDTE.Contains(365))
            .OrderByDescending(m => m.Priority)
            .Select(m => m.Symbol)
            .Take(50) // Limit for practical LEAPS management
            .ToList();
    }

    /// <summary>
    /// Get symbol metadata
    /// </summary>
    public SymbolMetadata? GetMetadata(string symbol)
    {
        return _metadata.TryGetValue(symbol, out var metadata) ? metadata : null;
    }

    /// <summary>
    /// Get all symbols in the universe
    /// </summary>
    public List<string> GetAllSymbols()
    {
        return _universe.SelectMany(kv => kv.Value).ToList();
    }

    /// <summary>
    /// Export universe configuration to JSON
    /// </summary>
    public async Task ExportUniverseConfig(string filePath)
    {
        var config = new UniverseConfig
        {
            Universe = _universe,
            Metadata = _metadata,
            LastUpdated = DateTime.UtcNow,
            Version = "1.0.0"
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Get symbols for specific strategy focus
    /// </summary>
    public List<string> GetSymbolsForStrategy(TradingStrategy strategy)
    {
        return strategy switch
        {
            TradingStrategy.ZeroDTE => GetZeroDTESymbols(),
            TradingStrategy.LEAPS => GetLEAPSSymbols(),
            TradingStrategy.WeeklyIncome => GetSymbolsByCategory("etfs")
                .Concat(GetPrioritySymbols(7))
                .Take(20).ToList(),
            TradingStrategy.Momentum => GetSymbolsByCategory("stocks")
                .Where(s => _metadata[s].Priority >= 6)
                .Take(30).ToList(),
            TradingStrategy.Volatility => new[] { "I:SPX", "I:VIX", "SPY", "QQQ", "UVXY", "SQQQ" }.ToList(),
            _ => GetPrioritySymbols(7)
        };
    }
}

/// <summary>
/// Symbol metadata for comprehensive tracking
/// </summary>
public class SymbolMetadata
{
    public string Symbol { get; set; } = "";
    public string Category { get; set; } = "";
    public string OptionStyle { get; set; } = ""; // American, European
    public string Settlement { get; set; } = ""; // Physical, Cash
    public long TypicalVolume { get; set; }
    public double MinStrikeIncrement { get; set; }
    public int[] PreferredDTE { get; set; } = Array.Empty<int>();
    public string OptionsSymbol { get; set; } = ""; // May differ from underlying symbol
    public int Priority { get; set; } // 1-10, higher = more important
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Universe configuration export structure
/// </summary>
public class UniverseConfig
{
    public Dictionary<string, List<string>> Universe { get; set; } = new();
    public Dictionary<string, SymbolMetadata> Metadata { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public string Version { get; set; } = "";
}

/// <summary>
/// Trading strategy enumeration
/// </summary>
public enum TradingStrategy
{
    ZeroDTE,
    LEAPS,
    WeeklyIncome,
    Momentum,
    Volatility,
    Scalping,
    Swing
}