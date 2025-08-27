using Microsoft.Extensions.Logging;
using Stroll.Backtest.Tests.Core;
using Stroll.Backtest.Tests.Strategy;
using Stroll.Storage;
using System.Globalization;

namespace Stroll.Backtest.Tests.Backtests;

/// <summary>
/// Comprehensive backtest runner for SPX 1DTE strategy from Sep 9, 1999 to Aug 24, 2025
/// Integrates with Stroll.Storage for market data and uses RealFill dynamics from ODTE
/// </summary>
public class SpxOneDteBacktestRunner
{
    private readonly ILogger<SpxOneDteBacktestRunner> _logger;
    private readonly IStorageProvider _storage;
    private readonly SpxOneDteStrategy _strategy;
    private readonly RealFillEngine _fillEngine;
    private readonly Dictionary<string, Position> _activePositions;
    private readonly List<Trade> _completedTrades;
    
    // Backtest parameters
    private readonly DateTime _startDate = new DateTime(1999, 9, 9);
    private readonly DateTime _endDate = new DateTime(2025, 8, 24);
    private decimal _accountValue = 100000m; // Starting capital
    private decimal _maxDrawdown = 0m;
    private decimal _peakAccountValue = 100000m;

    public SpxOneDteBacktestRunner(IStorageProvider storage, ILogger<SpxOneDteBacktestRunner>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SpxOneDteBacktestRunner>.Instance;
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _strategy = new SpxOneDteStrategy(null, seed: 42); // Deterministic for testing
        _fillEngine = new RealFillEngine(null, seed: 42);
        _activePositions = new Dictionary<string, Position>();
        _completedTrades = new List<Trade>();
    }

    /// <summary>
    /// Run the complete backtest from September 9, 1999 to August 24, 2025
    /// </summary>
    public async Task<BacktestResult> RunBacktestAsync()
    {
        _logger.LogInformation("🚀 Starting SPX 1DTE Backtest: {StartDate} to {EndDate}", 
            _startDate.ToString("yyyy-MM-dd"), _endDate.ToString("yyyy-MM-dd"));
        
        var totalDays = (_endDate - _startDate).Days;
        var processedDays = 0;
        var currentDate = _startDate;
        
        while (currentDate <= _endDate)
        {
            try
            {
                await ProcessTradingDay(currentDate);
                processedDays++;
                
                // Progress reporting every 100 days
                if (processedDays % 100 == 0)
                {
                    var progressPct = (double)processedDays / totalDays * 100;
                    _logger.LogInformation("📊 Progress: {Progress:F1}% ({ProcessedDays}/{TotalDays} days), Account Value: ${AccountValue:N0}", 
                        progressPct, processedDays, totalDays, _accountValue);
                }
                
                currentDate = currentDate.AddDays(1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing trading day {Date}", currentDate.ToString("yyyy-MM-dd"));
                currentDate = currentDate.AddDays(1);
            }
        }
        
        _logger.LogInformation("✅ Backtest completed successfully");
        return GenerateBacktestResult();
    }

    private async Task ProcessTradingDay(DateTime date)
    {
        // Skip weekends
        if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            return;
        
        // Get market data for the day
        var marketData = await GetMarketDataAsync(date);
        if (marketData == null)
        {
            _logger.LogDebug("No market data available for {Date}", date.ToString("yyyy-MM-dd"));
            return;
        }
        
        // Process trading throughout the day (simulate intraday timestamps)
        var tradingHours = GenerateTradingHours(date);
        
        foreach (var timestamp in tradingHours)
        {
            var currentMarketData = marketData with { Timestamp = timestamp };
            
            // Manage existing positions first
            await ManageExistingPositions(timestamp, currentMarketData);
            
            // Generate new signals
            await ProcessNewSignals(timestamp, currentMarketData);
        }
        
        // End of day cleanup
        await ProcessEndOfDay(date, marketData);
    }

    private async Task ManageExistingPositions(DateTime timestamp, MarketData marketData)
    {
        if (!_activePositions.Any()) return;
        
        var positions = _activePositions.Values.ToList();
        var managementSignals = await _strategy.ManagePositionsAsync(timestamp, positions, marketData);
        
        foreach (var signal in managementSignals)
        {
            await ExecuteSignal(signal, marketData);
        }
    }

    private async Task ProcessNewSignals(DateTime timestamp, MarketData marketData)
    {
        var signals = await _strategy.GenerateSignalsAsync(timestamp, marketData);
        
        foreach (var signal in signals.Where(s => s.SignalType == SignalType.Entry))
        {
            // Risk management - don't over-leverage
            if (_activePositions.Count < 3) // Maximum 3 concurrent positions
            {
                await ExecuteSignal(signal, marketData);
            }
        }
    }

    private async Task ExecuteSignal(TradeSignal signal, MarketData marketData)
    {
        var trade = new Trade
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = signal.Timestamp,
            StrategyName = signal.StrategyName,
            SignalType = signal.SignalType
        };
        
        var totalPremium = 0m;
        var fills = new List<FillResult>();
        
        // Execute each leg of the trade
        foreach (var leg in signal.Legs)
        {
            var optionQuote = GenerateOptionQuote(leg, marketData);
            var order = CreateOptionOrder(leg, optionQuote);
            var marketConditions = CreateMarketConditions(marketData);
            
            var fillResult = _fillEngine.SimulateFill(order, optionQuote, marketConditions);
            
            if (fillResult.IsFilled)
            {
                fills.Add(fillResult);
                var legValue = fillResult.FillPrice * leg.Quantity * 100; // SPX multiplier
                totalPremium += leg.Side == OrderSide.Buy ? -legValue : legValue;
            }
            else
            {
                _logger.LogDebug("Failed to fill leg: {Symbol} {Strike} {OptionType} {Side}", 
                    leg.Symbol, leg.Strike, leg.OptionType, leg.Side);
                return; // If any leg fails, abort the entire trade
            }
        }
        
        trade.Fills = fills.ToArray();
        trade.NetPremium = totalPremium;
        
        if (signal.SignalType == SignalType.Entry)
        {
            // Open new position
            var position = new Position
            {
                Id = trade.Id,
                Symbol = signal.Legs[0].Symbol,
                Expiration = signal.Legs[0].Expiration,
                Legs = signal.Legs,
                OpenValue = totalPremium,
                OpenTime = signal.Timestamp
            };
            
            _activePositions[trade.Id] = position;
            _accountValue += totalPremium; // Credit received (positive for iron condor)
            
            _logger.LogDebug("Opened position {TradeId}: ${Premium:F2}", trade.Id, totalPremium);
        }
        else
        {
            // Close existing position
            if (_activePositions.TryGetValue(trade.Id, out var closingPosition))
            {
                _activePositions.Remove(trade.Id);
                _accountValue += totalPremium; // Debit paid (negative to close)
                
                var totalPnL = closingPosition.OpenValue + totalPremium;
                trade.PnL = totalPnL;
                
                _completedTrades.Add(trade);
                
                _logger.LogDebug("Closed position {TradeId}: P&L ${PnL:F2}", trade.Id, totalPnL);
            }
        }
        
        // Update performance metrics
        UpdatePerformanceMetrics();
    }

    private async Task ProcessEndOfDay(DateTime date, MarketData marketData)
    {
        // Force close any positions expiring today
        var expiringPositions = _activePositions.Values
            .Where(p => p.Expiration.Date <= date.Date)
            .ToList();
        
        foreach (var position in expiringPositions)
        {
            var closeSignal = CreateForceCloseSignal(position, date);
            await ExecuteSignal(closeSignal, marketData);
        }
    }

    private TradeSignal CreateForceCloseSignal(Position position, DateTime timestamp)
    {
        return new TradeSignal
        {
            Timestamp = timestamp,
            StrategyName = position.Legs[0].Symbol + "_Force_Close",
            Legs = position.Legs.Select(leg => new OptionLeg
            {
                Symbol = leg.Symbol,
                Strike = leg.Strike,
                Expiration = leg.Expiration,
                OptionType = leg.OptionType,
                Side = leg.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy,
                Quantity = leg.Quantity
            }).ToArray(),
            SignalType = SignalType.Exit,
            Reason = "Forced expiration close"
        };
    }

    private async Task<MarketData?> GetMarketDataAsync(DateTime date)
    {
        try
        {
            // Get SPX bar data from Stroll.Storage
            var dateOnly = DateOnly.FromDateTime(date);
            var bars = await _storage.GetBarsRawAsync("SPX", dateOnly, dateOnly, Granularity.Daily);
            
            if (!bars.Any()) return null;
            
            var bar = bars.First();
            
            return new MarketData
            {
                Timestamp = date,
                SpxPrice = Convert.ToDecimal(bar["c"]), // Close price
                ImpliedVolatility = 0.20m, // Simplified - would use VIX data in production
                VolumeRatio = 1.0m, // Simplified
                IsMarketOpen = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching market data for {Date}", date.ToString("yyyy-MM-dd"));
            return null;
        }
    }

    private IEnumerable<DateTime> GenerateTradingHours(DateTime date)
    {
        // Generate key timestamps during trading day
        var tradingTimes = new List<DateTime>
        {
            date.AddHours(9).AddMinutes(45),   // Entry time
            date.AddHours(12),                 // Mid-day check
            date.AddHours(15).AddMinutes(45)   // Close time
        };
        
        return tradingTimes;
    }

    private OptionQuote GenerateOptionQuote(OptionLeg leg, MarketData marketData)
    {
        // Simplified option pricing - in production would use Black-Scholes or market data
        var timeToExpiry = Math.Min((leg.Expiration - marketData.Timestamp).TotalDays / 365.0, 1.0); // Cap at 1 year
        var intrinsic = Math.Max(0, leg.OptionType == OptionType.Call 
            ? marketData.SpxPrice - leg.Strike 
            : leg.Strike - marketData.SpxPrice);
        
        // Protect against overflow in time value calculation
        var safeIV = Math.Min(marketData.ImpliedVolatility, 5.0m); // Cap IV at 500%
        
        // Cap timeToExpiry to prevent sqrt overflow
        var safeTimeToExpiry = Math.Max(0.0, Math.Min(timeToExpiry, 10.0)); // Cap at 10 years max
        
        var sqrtTimeDouble = Math.Sqrt(safeTimeToExpiry);
        // Protect against decimal overflow when converting from double  
        var sqrtTime = double.IsInfinity(sqrtTimeDouble) || double.IsNaN(sqrtTimeDouble) ? 0m :
                       sqrtTimeDouble > (double)decimal.MaxValue ? decimal.MaxValue : 
                       sqrtTimeDouble < (double)decimal.MinValue ? decimal.MinValue : 
                       (decimal)sqrtTimeDouble;
        var timeValue = marketData.SpxPrice * safeIV * sqrtTime / 10;
        timeValue = Math.Min(timeValue, marketData.SpxPrice * 0.5m); // Cap at 50% of underlying
        
        var theoreticalValue = intrinsic + timeValue;
        
        var spread = Math.Max(0.05m, theoreticalValue * 0.02m); // 2% spread minimum $0.05
        
        return new OptionQuote
        {
            Bid = Math.Max(0.01m, theoreticalValue - spread/2),
            Ask = theoreticalValue + spread/2,
            BidSize = 10,
            AskSize = 10,
            ImpliedVolatility = marketData.ImpliedVolatility,
            Delta = 0.15m, // Simplified
            Gamma = 0.01m,
            Theta = -0.1m,
            Vega = 0.05m,
            Timestamp = marketData.Timestamp
        };
    }

    private OptionOrder CreateOptionOrder(OptionLeg leg, OptionQuote quote)
    {
        return new OptionOrder
        {
            Symbol = leg.Symbol,
            OptionSymbol = $"{leg.Symbol}{leg.Expiration:yyMMdd}{leg.OptionType.ToString()[0]}{leg.Strike:0000000}",
            Side = leg.Side,
            OrderType = OrderType.Market, // Use market orders for simplicity
            Quantity = leg.Quantity,
            Expiration = leg.Expiration,
            Strike = leg.Strike,
            OptionType = leg.OptionType
        };
    }

    private MarketConditions CreateMarketConditions(MarketData marketData)
    {
        return new MarketConditions
        {
            ImpliedVolatility = marketData.ImpliedVolatility,
            VolumeRatio = marketData.VolumeRatio,
            IsMarketOpen = marketData.IsMarketOpen,
            Timestamp = marketData.Timestamp
        };
    }

    private void UpdatePerformanceMetrics()
    {
        if (_accountValue > _peakAccountValue)
        {
            _peakAccountValue = _accountValue;
        }
        
        var currentDrawdown = (_peakAccountValue - _accountValue) / _peakAccountValue;
        if (currentDrawdown > _maxDrawdown)
        {
            _maxDrawdown = currentDrawdown;
        }
    }

    private BacktestResult GenerateBacktestResult()
    {
        var totalReturn = (_accountValue - 100000m) / 100000m;
        var winningTrades = _completedTrades.Where(t => t.PnL > 0).ToList();
        var losingTrades = _completedTrades.Where(t => t.PnL <= 0).ToList();
        
        return new BacktestResult
        {
            StartDate = _startDate,
            EndDate = _endDate,
            StartingCapital = 100000m,
            FinalAccountValue = _accountValue,
            TotalReturn = totalReturn,
            AnnualizedReturn = CalculateAnnualizedReturn(totalReturn, (_endDate - _startDate).Days),
            MaxDrawdown = _maxDrawdown,
            TotalTrades = _completedTrades.Count,
            WinningTrades = winningTrades.Count,
            LosingTrades = losingTrades.Count,
            WinRate = _completedTrades.Count > 0 ? (decimal)winningTrades.Count / _completedTrades.Count : 0m,
            AverageWin = winningTrades.Count > 0 ? winningTrades.Average(t => t.PnL) : 0m,
            AverageLoss = losingTrades.Count > 0 ? losingTrades.Average(t => t.PnL) : 0m,
            ProfitFactor = losingTrades.Sum(t => Math.Abs(t.PnL)) != 0 
                ? winningTrades.Sum(t => t.PnL) / Math.Abs(losingTrades.Sum(t => t.PnL)) 
                : 0m,
            Trades = _completedTrades.ToArray()
        };
    }

    private decimal CalculateAnnualizedReturn(decimal totalReturn, int totalDays)
    {
        if (totalDays <= 0) return 0m;
        
        var years = (decimal)totalDays / 365.25m;
        if (years <= 0) return 0m;
        
        // Protect against overflow/underflow in power calculation
        var returnRatio = 1 + totalReturn;
        if (returnRatio <= 0) return -1m; // Complete loss
        
        var exponent = (double)(1 / years);
        var result = Math.Pow((double)returnRatio, exponent) - 1;
        
        // Cap extreme results to prevent overflow
        return (decimal)Math.Max(-1.0, Math.Min(100.0, result)); // Cap between -100% and 10000%
    }
}

// Supporting data structures
public record Trade
{
    public required string Id { get; init; }
    public required DateTime Timestamp { get; init; }
    public string StrategyName { get; set; } = string.Empty;
    public required SignalType SignalType { get; init; }
    public FillResult[] Fills { get; set; } = Array.Empty<FillResult>();
    public decimal NetPremium { get; set; }
    public decimal PnL { get; set; }
    
    // Additional required properties for compatibility
    public decimal Pnl { get => PnL; set => PnL = value; }
    public string Strategy { get => StrategyName; set => StrategyName = value; }
}

// BacktestResult is now defined in Core/BacktestResult.cs