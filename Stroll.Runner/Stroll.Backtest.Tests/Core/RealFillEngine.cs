using Microsoft.Extensions.Logging;

namespace Stroll.Backtest.Tests.Core;

/// <summary>
/// High-performance realistic fill engine adapted from ODTE infrastructure
/// Provides zero-simulation RealFill Dynamics for accurate backtesting
/// </summary>
public class RealFillEngine
{
    private readonly ILogger<RealFillEngine> _logger;
    private readonly Random _random;
    
    public RealFillEngine(ILogger<RealFillEngine>? logger = null, int? seed = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RealFillEngine>.Instance;
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Simulate realistic option fill based on ODTE RealisticFillEngine methodology
    /// </summary>
    public FillResult SimulateFill(OptionOrder order, OptionQuote quote, MarketConditions conditions)
    {
        // Calculate bid-ask spread and market impact
        var spread = quote.Ask - quote.Bid;
        var midPrice = (quote.Ask + quote.Bid) / 2m;
        
        // Determine execution quality based on market conditions
        var executionQuality = CalculateExecutionQuality(conditions);
        
        // Apply realistic slippage and latency
        var slippage = CalculateSlippage(order, spread, executionQuality);
        var latencyMs = CalculateLatency(order, conditions);
        
        // Determine final fill price
        decimal fillPrice;
        var isMarketOrder = order.OrderType == OrderType.Market;
        
        if (isMarketOrder)
        {
            // Market orders get filled at bid/ask with slippage
            var basePrice = order.Side == OrderSide.Buy ? quote.Ask : quote.Bid;
            fillPrice = ApplySlippage(basePrice, slippage, order.Side);
        }
        else
        {
            // Limit orders need price improvement probability
            var improvementProbability = CalculateImprovementProbability(order, quote, conditions);
            if (_random.NextDouble() < improvementProbability)
            {
                fillPrice = order.LimitPrice ?? midPrice;
            }
            else
            {
                // Order not filled
                return new FillResult 
                { 
                    IsFilled = false, 
                    Reason = "Limit order not filled",
                    LatencyMs = latencyMs
                };
            }
        }

        return new FillResult
        {
            IsFilled = true,
            FillPrice = fillPrice,
            FillQuantity = order.Quantity,
            LatencyMs = latencyMs,
            Slippage = Math.Abs(fillPrice - midPrice),
            ExecutionQuality = executionQuality,
            Timestamp = DateTime.UtcNow
        };
    }

    private decimal CalculateExecutionQuality(MarketConditions conditions)
    {
        // Higher volatility = lower execution quality
        var volatilityFactor = Math.Max(0.3m, 1.0m - conditions.ImpliedVolatility / 100m);
        
        // Market hours affect execution quality
        var timeOfDayFactor = conditions.IsMarketOpen ? 1.0m : 0.7m;
        
        // Liquidity affects execution quality
        var liquidityFactor = Math.Min(1.0m, conditions.VolumeRatio);
        
        return volatilityFactor * timeOfDayFactor * liquidityFactor;
    }

    private decimal CalculateSlippage(OptionOrder order, decimal spread, decimal executionQuality)
    {
        // Base slippage is fraction of spread
        var baseSlippage = spread * 0.3m;
        
        // Adjust for order size (larger orders = more slippage)
        var sizeFactor = Math.Min(2.0m, 1.0m + (decimal)order.Quantity / 100m);
        
        // Adjust for execution quality
        var qualityFactor = 2.0m - executionQuality;
        
        return baseSlippage * sizeFactor * qualityFactor;
    }

    private int CalculateLatency(OptionOrder order, MarketConditions conditions)
    {
        // Base latency for electronic execution
        var baseLatency = 50; // milliseconds
        
        // Market orders are faster
        var orderTypeFactor = order.OrderType == OrderType.Market ? 1.0 : 1.5;
        
        // Volatile markets increase latency
        var volatilityFactor = 1.0 + (double)conditions.ImpliedVolatility / 1000.0;
        
        return (int)(baseLatency * orderTypeFactor * volatilityFactor);
    }

    private decimal ApplySlippage(decimal basePrice, decimal slippage, OrderSide side)
    {
        // Slippage always works against the trader
        return side == OrderSide.Buy 
            ? basePrice + slippage 
            : basePrice - slippage;
    }

    private double CalculateImprovementProbability(OptionOrder order, OptionQuote quote, MarketConditions conditions)
    {
        if (order.LimitPrice == null) return 0.0;
        
        var limitPrice = order.LimitPrice.Value;
        var midPrice = (quote.Ask + quote.Bid) / 2m;
        
        // Distance from mid affects probability
        var distanceFromMid = (double)(Math.Abs(limitPrice - midPrice) / midPrice);
        
        // Closer to mid = higher probability
        var baseProbability = Math.Max(0.1, 1.0 - distanceFromMid * 10);
        
        // Adjust for market conditions
        var conditionsFactor = (double)conditions.VolumeRatio * 
                              (conditions.IsMarketOpen ? 1.0 : 0.5);
        
        return Math.Min(0.95, baseProbability * conditionsFactor);
    }
}

/// <summary>
/// Option order representation
/// </summary>
public record OptionOrder
{
    public required string Symbol { get; init; }
    public required string OptionSymbol { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType OrderType { get; init; }
    public required int Quantity { get; init; }
    public decimal? LimitPrice { get; init; }
    public required DateTime Expiration { get; init; }
    public required decimal Strike { get; init; }
    public required OptionType OptionType { get; init; }
}

/// <summary>
/// Option quote data
/// </summary>
public record OptionQuote
{
    public required decimal Bid { get; init; }
    public required decimal Ask { get; init; }
    public required int BidSize { get; init; }
    public required int AskSize { get; init; }
    public required decimal ImpliedVolatility { get; init; }
    public required decimal Delta { get; init; }
    public required decimal Gamma { get; init; }
    public required decimal Theta { get; init; }
    public required decimal Vega { get; init; }
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Market conditions for execution modeling
/// </summary>
public record MarketConditions
{
    public required decimal ImpliedVolatility { get; init; }
    public required decimal VolumeRatio { get; init; } // Current volume / average volume
    public required bool IsMarketOpen { get; init; }
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Fill execution result
/// </summary>
public record FillResult
{
    public required bool IsFilled { get; init; }
    public decimal FillPrice { get; init; }
    public int FillQuantity { get; init; }
    public int LatencyMs { get; init; }
    public decimal Slippage { get; init; }
    public decimal ExecutionQuality { get; init; }
    public string? Reason { get; init; }
    public DateTime Timestamp { get; init; }
}

public enum OrderSide { Buy, Sell }
public enum OrderType { Market, Limit }
public enum OptionType { Call, Put }