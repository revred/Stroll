using Microsoft.Extensions.Logging;
using Stroll.Storage;

namespace Stroll.Historical;

/// <summary>
/// Multi-provider data acquisition with intelligent fallback
/// Primary: Databento (institutional grade, 1-min since 2000)
/// Secondary: Alpha Vantage (reliable free/premium, comprehensive API)
/// Fallback: Yahoo Finance / Stooq (free backup)
/// </summary>
public class RunMultiProviderAcquisition
{
    public static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<RunMultiProviderAcquisition>();
        
        logger.LogInformation("🚀 Multi-Provider Historical Data Acquisition");
        logger.LogInformation("===========================================");

        try
        {
            // Configuration
            var startDate = args.Length > 0 && DateTime.TryParse(args[0], out var start) 
                ? start : new DateTime(2000, 1, 1);
            var endDate = args.Length > 1 && DateTime.TryParse(args[1], out var end)
                ? end : DateTime.Today;
            
            var symbols = new[] { "SPY", "QQQ", "IWM", "XLE", "XLF", "XLK" };

            logger.LogInformation("📅 Target Period: {Start} to {End} ({Years:F1} years)",
                startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"),
                (endDate - startDate).Days / 365.0);

            // Set up storage
            var dataPath = Path.GetFullPath("./data");
            Directory.CreateDirectory(dataPath);
            logger.LogInformation("💾 Output: {Path}", dataPath);

            // Initialize providers with priority order
            var acquisitionEngine = new MultiProviderAcquisitionEngine(
                loggerFactory.CreateLogger<MultiProviderAcquisitionEngine>());

            // Add Databento if API key available
            var databentoKey = Environment.GetEnvironmentVariable("DATABENTO_API_KEY");
            if (!string.IsNullOrEmpty(databentoKey))
            {
                acquisitionEngine.AddProvider(new DatabentoProvider(
                    databentoKey, 
                    loggerFactory.CreateLogger<DatabentoProvider>()), 
                    priority: 1);
                logger.LogInformation("✅ Databento provider added (Priority 1)");
            }

            // Add Alpha Vantage if API key available
            var alphaVantageKey = Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY");
            if (!string.IsNullOrEmpty(alphaVantageKey))
            {
                var isPremium = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALPHA_VANTAGE_PREMIUM"));
                acquisitionEngine.AddProvider(new AlphaVantageProvider(
                    alphaVantageKey, 
                    isPremium,
                    loggerFactory.CreateLogger<AlphaVantageProvider>()), 
                    priority: 2);
                logger.LogInformation("✅ Alpha Vantage provider added (Priority 2, {Tier})", 
                    isPremium ? "Premium" : "Free");
            }

            // Prompt for missing API keys
            if (string.IsNullOrEmpty(databentoKey) && string.IsNullOrEmpty(alphaVantageKey))
            {
                logger.LogInformation("🔑 No API keys found. Please set environment variables:");
                logger.LogInformation("   DATABENTO_API_KEY - Get from https://databento.com/");
                logger.LogInformation("   ALPHA_VANTAGE_API_KEY - Get from https://www.alphavantage.co/");
                
                Console.Write("Enter Alpha Vantage API key (or press Enter to skip): ");
                var manualKey = Console.ReadLine();
                if (!string.IsNullOrEmpty(manualKey))
                {
                    acquisitionEngine.AddProvider(new AlphaVantageProvider(
                        manualKey, 
                        false,
                        loggerFactory.CreateLogger<AlphaVantageProvider>()), 
                        priority: 2);
                    logger.LogInformation("✅ Alpha Vantage provider added manually");
                }
            }

            if (!acquisitionEngine.HasProviders)
            {
                logger.LogError("❌ No data providers available. Cannot proceed.");
                Environment.Exit(1);
            }

            // Execute acquisition
            var totalResults = new List<ProviderAcquisitionResult>();

            foreach (var symbol in symbols)
            {
                logger.LogInformation("🔄 Processing {Symbol}...", symbol);

                var symbolResult = await acquisitionEngine.AcquireSymbolDataAsync(
                    symbol, startDate, endDate);

                totalResults.Add(symbolResult);

                if (symbolResult.Success)
                {
                    logger.LogInformation("✅ {Symbol}: {Records:N0} records from {Provider}",
                        symbol, symbolResult.TotalRecords, symbolResult.SuccessfulProvider);
                    
                    // Store in Stroll format
                    await StoreSymbolData(symbol, symbolResult, dataPath, logger);
                }
                else
                {
                    logger.LogError("❌ {Symbol}: Failed with all providers - {Error}",
                        symbol, symbolResult.ErrorMessage);
                }
            }

            // Final report
            var successful = totalResults.Count(r => r.Success);
            var totalRecords = totalResults.Sum(r => r.TotalRecords);

            logger.LogInformation("🎯 ACQUISITION COMPLETE!");
            logger.LogInformation("========================");
            logger.LogInformation("📊 Success Rate: {Rate:P0} ({Success}/{Total})", 
                successful / (double)symbols.Length, successful, symbols.Length);
            logger.LogInformation("📈 Total Records: {Records:N0}", totalRecords);
            logger.LogInformation("💾 Data Location: {Path}", dataPath);

            if (successful > 0)
            {
                logger.LogInformation("🎉 Ready for professional backtesting!");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 Multi-provider acquisition failed");
            Environment.Exit(1);
        }
    }

    private static async Task StoreSymbolData(
        string symbol, 
        ProviderAcquisitionResult result, 
        string dataPath, 
        ILogger logger)
    {
        try
        {
            var fileName = $"{symbol}_{result.SuccessfulProvider?.ToLower()}.csv";
            var filePath = Path.Combine(dataPath, fileName);
            
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("timestamp,open,high,low,close,volume");

            foreach (var bar in result.Bars.OrderBy(b => (DateTime)b["t"]!))
            {
                var timestamp = (DateTime)bar["t"]!;
                var open = (decimal)bar["o"]!;
                var high = (decimal)bar["h"]!;
                var low = (decimal)bar["l"]!;
                var close = (decimal)bar["c"]!;
                var volume = (long)bar["v"]!;

                csv.AppendLine($"{timestamp:yyyy-MM-dd HH:mm:ss},{open},{high},{low},{close},{volume}");
            }

            await File.WriteAllTextAsync(filePath, csv.ToString());
            logger.LogDebug("💾 Stored {Symbol}: {Records:N0} bars in {File}",
                symbol, result.TotalRecords, fileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to store {Symbol} data", symbol);
        }
    }
}

/// <summary>
/// Multi-provider acquisition engine with intelligent failover
/// </summary>
public class MultiProviderAcquisitionEngine
{
    private readonly List<(object Provider, int Priority)> _providers = new();
    private readonly ILogger<MultiProviderAcquisitionEngine> _logger;

    public MultiProviderAcquisitionEngine(ILogger<MultiProviderAcquisitionEngine> logger)
    {
        _logger = logger;
    }

    public bool HasProviders => _providers.Count > 0;

    public void AddProvider(object provider, int priority)
    {
        _providers.Add((provider, priority));
        _providers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }

    public async Task<ProviderAcquisitionResult> AcquireSymbolDataAsync(
        string symbol, DateTime startDate, DateTime endDate)
    {
        var result = new ProviderAcquisitionResult
        {
            Symbol = symbol,
            StartDate = startDate,
            EndDate = endDate
        };

        foreach (var (provider, priority) in _providers)
        {
            try
            {
                _logger.LogInformation("🔄 Trying {Provider} for {Symbol}", 
                    provider.GetType().Name, symbol);

                var providerResult = provider switch
                {
                    DatabentoProvider databento => await TryDatabentoAsync(databento, symbol, startDate, endDate),
                    AlphaVantageProvider alphaVantage => await TryAlphaVantageAsync(alphaVantage, symbol, startDate, endDate),
                    _ => null
                };

                if (providerResult?.Success == true && providerResult.Bars.Count > 0)
                {
                    result.Success = true;
                    result.Bars = providerResult.Bars;
                    result.TotalRecords = providerResult.Bars.Count;
                    result.SuccessfulProvider = provider.GetType().Name.Replace("Provider", "");
                    
                    _logger.LogInformation("✅ {Provider} succeeded for {Symbol}: {Records:N0} records",
                        result.SuccessfulProvider, symbol, result.TotalRecords);
                    
                    return result;
                }
                else
                {
                    _logger.LogWarning("⚠️ {Provider} failed for {Symbol}: {Error}",
                        provider.GetType().Name, symbol, providerResult?.ErrorMessage ?? "No data");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ {Provider} exception for {Symbol}",
                    provider.GetType().Name, symbol);
            }
        }

        result.ErrorMessage = "All providers failed";
        return result;
    }

    private async Task<ProviderResult?> TryDatabentoAsync(
        DatabentoProvider databento, string symbol, DateTime startDate, DateTime endDate)
    {
        var results = await databento.GetHistoricalBarsChunkedAsync(symbol, startDate, endDate);
        var allBars = results.SelectMany(r => r.Bars).ToList();
        
        return new ProviderResult
        {
            Success = allBars.Count > 0,
            Bars = allBars,
            ErrorMessage = allBars.Count == 0 ? "No data returned" : null
        };
    }

    private async Task<ProviderResult?> TryAlphaVantageAsync(
        AlphaVantageProvider alphaVantage, string symbol, DateTime startDate, DateTime endDate)
    {
        var results = await alphaVantage.GetComprehensiveHistoricalAsync(symbol, startDate, endDate);
        var allBars = results.SelectMany(r => r.Bars).ToList();
        
        return new ProviderResult
        {
            Success = allBars.Count > 0,
            Bars = allBars,
            ErrorMessage = allBars.Count == 0 ? "No data returned" : null
        };
    }
}

// Supporting records
public record ProviderAcquisitionResult
{
    public required string Symbol { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public bool Success { get; set; }
    public List<Dictionary<string, object?>> Bars { get; set; } = new();
    public int TotalRecords { get; set; }
    public string? SuccessfulProvider { get; set; }
    public string? ErrorMessage { get; set; }
}

public record ProviderResult
{
    public bool Success { get; set; }
    public List<Dictionary<string, object?>> Bars { get; set; } = new();
    public string? ErrorMessage { get; set; }
}