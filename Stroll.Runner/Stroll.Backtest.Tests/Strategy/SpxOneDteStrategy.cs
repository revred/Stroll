using Microsoft.Extensions.Logging;
using Stroll.Backtest.Tests.Core;

namespace Stroll.Backtest.Tests.Strategy;

/// <summary>
/// Simple 1DTE (1 Day To Expiration) SPX options strategy
/// Trades daily expiring options on SPX with systematic entry and exit rules
/// </summary>
public class SpxOneDteStrategy
{
    private readonly ILogger<SpxOneDteStrategy> _logger;
    private readonly RealFillEngine _fillEngine;
    
    // Strategy parameters
    private const decimal TARGET_DELTA = 0.15m; // Target delta for short options
    private const decimal PROFIT_TARGET_PCT = 0.5m; // Take profit at 50% of credit
    private const decimal STOP_LOSS_MULTIPLIER = 3.0m; // Stop loss at 3x credit received
    private const int ENTRY_TIME_HOUR = 9; // Enter at 9 AM ET
    private const int ENTRY_TIME_MINUTE = 45; // 15 minutes after market open
    private const int EXIT_TIME_HOUR = 15; // Close positions by 3 PM ET
    private const int EXIT_TIME_MINUTE = 45; // 15 minutes before close
    
    public SpxOneDteStrategy(ILogger<SpxOneDteStrategy>? logger = null, int? seed = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SpxOneDteStrategy>.Instance;
        _fillEngine = new RealFillEngine(null, seed);
    }

    /// <summary>
    /// Generate 1DTE Iron Condor trade signals
    /// </summary>
    public async Task<IEnumerable<TradeSignal>> GenerateSignalsAsync(DateTime timestamp, MarketData marketData)
    {
        var signals = new List<TradeSignal>();
        
        _logger.LogDebug("Checking signals for {Timestamp} - Market Hours: {MarketHours}, Entry Time: {EntryTime}", 
            timestamp, IsMarketHours(timestamp), IsEntryTime(timestamp));
        
        // Only trade during market hours
        if (!IsMarketHours(timestamp))
        {
            _logger.LogDebug("Skipping {Timestamp} - not market hours", timestamp);
            return signals;
        }
        
        // Check if it's time to enter new positions
        if (!IsEntryTime(timestamp))
        {
            _logger.LogDebug("Skipping {Timestamp} - not entry time (hour: {Hour}, minute: {Minute})", 
                timestamp, timestamp.Hour, timestamp.Minute);
            return signals;
        }
            
        // For backtesting with stock data, we'll simulate 1DTE option behavior
        // Skip condition that requires actual options data - simulate instead
        var expirationDate = GetNextExpirationDate(timestamp);
        
        try
        {
            _logger.LogInformation("Generating 1DTE SPX signals for {Date}", timestamp.ToString("yyyy-MM-dd HH:mm"));
            
            // Get current SPX price
            var spxPrice = marketData.SpxPrice;
            
            // Calculate strike prices for Iron Condor
            var strikes = CalculateIronCondorStrikes(spxPrice, marketData.ImpliedVolatility);
            
            // Create Iron Condor trade (short strangle + long wings for protection)
            var ironCondorSignal = new TradeSignal
            {
                Timestamp = timestamp,
                StrategyName = "SPX_1DTE_Iron_Condor",
                Legs = new[]
                {
                    // Short Call (closer to money)
                    new OptionLeg
                    {
                        Symbol = "SPX",
                        Strike = strikes.ShortCallStrike,
                        Expiration = expirationDate,
                        OptionType = OptionType.Call,
                        Side = OrderSide.Sell,
                        Quantity = 1
                    },
                    // Long Call (further from money - protection)
                    new OptionLeg
                    {
                        Symbol = "SPX",
                        Strike = strikes.LongCallStrike,
                        Expiration = expirationDate,
                        OptionType = OptionType.Call,
                        Side = OrderSide.Buy,
                        Quantity = 1
                    },
                    // Short Put (closer to money)
                    new OptionLeg
                    {
                        Symbol = "SPX",
                        Strike = strikes.ShortPutStrike,
                        Expiration = expirationDate,
                        OptionType = OptionType.Put,
                        Side = OrderSide.Sell,
                        Quantity = 1
                    },
                    // Long Put (further from money - protection)
                    new OptionLeg
                    {
                        Symbol = "SPX",
                        Strike = strikes.LongPutStrike,
                        Expiration = expirationDate,
                        OptionType = OptionType.Put,
                        Side = OrderSide.Buy,
                        Quantity = 1
                    }
                },
                SignalType = SignalType.Entry,
                Confidence = CalculateSignalConfidence(marketData)
            };
            
            signals.Add(ironCondorSignal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating 1DTE signals at {Timestamp}", timestamp);
        }
        
        return signals;
    }

    /// <summary>
    /// Manage existing positions - check for profit taking or stop losses
    /// </summary>
    public async Task<IEnumerable<TradeSignal>> ManagePositionsAsync(DateTime timestamp, IEnumerable<Position> positions, MarketData marketData)
    {
        var managementSignals = new List<TradeSignal>();
        
        foreach (var position in positions.Where(p => p.Symbol == "SPX"))
        {
            try
            {
                // Check if position should be closed
                var closeSignal = await EvaluatePositionClose(timestamp, position, marketData);
                if (closeSignal != null)
                {
                    managementSignals.Add(closeSignal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error managing position {PositionId}", position.Id);
            }
        }
        
        return managementSignals;
    }

    private async Task<TradeSignal?> EvaluatePositionClose(DateTime timestamp, Position position, MarketData marketData)
    {
        var timeToExpiry = position.Expiration - timestamp;
        
        // Force close if near expiration (30 minutes before close)
        if (timeToExpiry.TotalMinutes < 30)
        {
            return CreateCloseSignal(position, timestamp, "Expiration approaching");
        }
        
        // Force close at end of trading day
        if (timestamp.Hour >= EXIT_TIME_HOUR && timestamp.Minute >= EXIT_TIME_MINUTE)
        {
            return CreateCloseSignal(position, timestamp, "End of trading day");
        }
        
        // Calculate current P&L
        var currentValue = await EstimatePositionValue(position, marketData);
        var pnl = currentValue - position.OpenValue;
        var pnlPct = position.OpenValue != 0 ? pnl / Math.Abs(position.OpenValue) : 0;
        
        // Profit target reached
        if (pnlPct >= PROFIT_TARGET_PCT)
        {
            return CreateCloseSignal(position, timestamp, $"Profit target hit: {pnlPct:P1}");
        }
        
        // Stop loss triggered
        if (pnlPct <= -STOP_LOSS_MULTIPLIER)
        {
            return CreateCloseSignal(position, timestamp, $"Stop loss triggered: {pnlPct:P1}");
        }
        
        return null;
    }

    private TradeSignal CreateCloseSignal(Position position, DateTime timestamp, string reason)
    {
        return new TradeSignal
        {
            Timestamp = timestamp,
            StrategyName = "SPX_1DTE_Iron_Condor",
            Legs = position.Legs.Select(leg => new OptionLeg
            {
                Symbol = leg.Symbol,
                Strike = leg.Strike,
                Expiration = leg.Expiration,
                OptionType = leg.OptionType,
                Side = leg.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy, // Opposite side to close
                Quantity = leg.Quantity
            }).ToArray(),
            SignalType = SignalType.Exit,
            Reason = reason
        };
    }

    private IronCondorStrikes CalculateIronCondorStrikes(decimal spxPrice, decimal impliedVolatility)
    {
        // Calculate expected move based on IV with overflow protection
        var sqrtTimeDouble = Math.Sqrt(1.0 / 365.0); // 1DTE = ~0.0524
        var sqrtTime = sqrtTimeDouble > (double)decimal.MaxValue ? decimal.MaxValue : 
                       sqrtTimeDouble < (double)decimal.MinValue ? decimal.MinValue : 
                       (decimal)sqrtTimeDouble;
        var safeIV = Math.Min(impliedVolatility, 5.0m); // Cap IV at 500% for safety
        var expectedMove = spxPrice * safeIV * sqrtTime;
        
        // Ensure expected move doesn't exceed reasonable bounds
        expectedMove = Math.Min(expectedMove, spxPrice * 0.2m); // Max 20% move
        
        // Short strikes target 15 delta (approximately)
        var shortCallStrike = Math.Ceiling(spxPrice + expectedMove * 0.85m);
        var shortPutStrike = Math.Floor(spxPrice - expectedMove * 0.85m);
        
        // Long strikes 10-15 points away for protection
        var wingWidth = 15m;
        var longCallStrike = shortCallStrike + wingWidth;
        var longPutStrike = shortPutStrike - wingWidth;
        
        return new IronCondorStrikes
        {
            ShortCallStrike = shortCallStrike,
            LongCallStrike = longCallStrike,
            ShortPutStrike = shortPutStrike,
            LongPutStrike = longPutStrike
        };
    }

    private decimal CalculateSignalConfidence(MarketData marketData)
    {
        var confidence = 0.5m; // Base confidence
        
        // Higher confidence in lower volatility environments
        if (marketData.ImpliedVolatility < 0.20m)
            confidence += 0.2m;
        
        // Higher confidence during normal market hours
        if (IsMarketHours(marketData.Timestamp))
            confidence += 0.1m;
        
        // Higher confidence with decent volume
        if (marketData.VolumeRatio > 0.8m)
            confidence += 0.1m;
        
        return Math.Min(1.0m, confidence);
    }

    private async Task<decimal> EstimatePositionValue(Position position, MarketData marketData)
    {
        // Simplified position valuation - in production this would use proper option pricing
        decimal totalValue = 0m;
        
        foreach (var leg in position.Legs)
        {
            var intrinsicValue = CalculateIntrinsicValue(leg, marketData.SpxPrice);
            var timeValue = CalculateTimeValue(leg, marketData);
            var optionValue = intrinsicValue + timeValue;
            
            totalValue += leg.Side == OrderSide.Buy ? optionValue : -optionValue;
        }
        
        // Protect against overflow when applying SPX multiplier
        var maxValueBeforeMultiplier = decimal.MaxValue / 100m;
        if (Math.Abs(totalValue) > maxValueBeforeMultiplier)
        {
            totalValue = totalValue > 0 ? maxValueBeforeMultiplier : -maxValueBeforeMultiplier;
        }
        
        return totalValue * 100; // SPX multiplier
    }

    private decimal CalculateIntrinsicValue(OptionLeg leg, decimal underlyingPrice)
    {
        if (leg.OptionType == OptionType.Call)
        {
            return Math.Max(0, underlyingPrice - leg.Strike);
        }
        else
        {
            return Math.Max(0, leg.Strike - underlyingPrice);
        }
    }

    private decimal CalculateTimeValue(OptionLeg leg, MarketData marketData)
    {
        var timeToExpiry = leg.Expiration - marketData.Timestamp;
        if (timeToExpiry.TotalDays <= 0) return 0m;
        
        // Simplified time value calculation with overflow protection
        var timeRatio = Math.Min(timeToExpiry.TotalDays / 365.0, 1.0); // Cap at 1 year
        var sqrtTimeDouble = Math.Sqrt(timeRatio);
        var timeValueFactor = sqrtTimeDouble > (double)decimal.MaxValue ? decimal.MaxValue : 
                              sqrtTimeDouble < (double)decimal.MinValue ? decimal.MinValue : 
                              (decimal)sqrtTimeDouble;
        var safeIV = Math.Min(marketData.ImpliedVolatility, 5.0m); // Cap IV at 500%
        
        var timeValue = marketData.SpxPrice * safeIV * timeValueFactor * 0.1m;
        return Math.Min(timeValue, marketData.SpxPrice * 0.5m); // Cap at 50% of underlying price
    }

    private bool IsMarketHours(DateTime timestamp)
    {
        // For backtesting, assume all timestamps during weekdays are market hours
        return timestamp.DayOfWeek != DayOfWeek.Saturday && 
               timestamp.DayOfWeek != DayOfWeek.Sunday;
    }

    private bool IsEntryTime(DateTime timestamp)
    {
        // For backtesting, allow entry at the 9:45 timestamp
        return timestamp.Hour == ENTRY_TIME_HOUR && 
               timestamp.Minute >= ENTRY_TIME_MINUTE;
    }

    private DateTime GetNextExpirationDate(DateTime currentDate)
    {
        // SPX has daily expirations - next business day
        var nextDate = currentDate.Date.AddDays(1);
        while (nextDate.DayOfWeek == DayOfWeek.Saturday || nextDate.DayOfWeek == DayOfWeek.Sunday)
        {
            nextDate = nextDate.AddDays(1);
        }
        return nextDate;
    }
}

// Supporting data structures
public record IronCondorStrikes
{
    public required decimal ShortCallStrike { get; init; }
    public required decimal LongCallStrike { get; init; }
    public required decimal ShortPutStrike { get; init; }
    public required decimal LongPutStrike { get; init; }
}

public record TradeSignal
{
    public required DateTime Timestamp { get; init; }
    public required string StrategyName { get; init; }
    public required OptionLeg[] Legs { get; init; }
    public required SignalType SignalType { get; init; }
    public decimal Confidence { get; init; } = 0.5m;
    public string? Reason { get; init; }
}

public record OptionLeg
{
    public required string Symbol { get; init; }
    public required decimal Strike { get; init; }
    public required DateTime Expiration { get; init; }
    public required OptionType OptionType { get; init; }
    public required OrderSide Side { get; init; }
    public required int Quantity { get; init; }
}

public record Position
{
    public required string Id { get; init; }
    public required string Symbol { get; init; }
    public required DateTime Expiration { get; init; }
    public required OptionLeg[] Legs { get; init; }
    public required decimal OpenValue { get; init; }
    public required DateTime OpenTime { get; init; }
}

public record MarketData
{
    public required DateTime Timestamp { get; init; }
    public required decimal SpxPrice { get; init; }
    public required decimal ImpliedVolatility { get; init; }
    public required decimal VolumeRatio { get; init; }
    public required bool IsMarketOpen { get; init; }
}

public enum SignalType { Entry, Exit }