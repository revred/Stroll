namespace Stroll.Backtest.Tests.Core;

/// <summary>
/// Unified backtest result record with all necessary properties for different test scenarios
/// </summary>
public record BacktestResult
{
    // Basic identification and timing
    public required string Name { get; init; }
    public required long TimeMs { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    
    // Data processing metrics
    public required int BarCount { get; init; }
    public required int TradeCount { get; init; }
    
    // Financial metrics
    public required decimal StartingCapital { get; init; }
    public required decimal FinalValue { get; init; }
    public required decimal TotalReturn { get; init; }
    public required decimal AnnualizedReturn { get; init; }
    public required decimal MaxDrawdown { get; init; }
    
    // Trade statistics
    public required int TotalTrades { get; init; }
    public required int WinningTrades { get; init; }
    public required int LosingTrades { get; init; }
    public required decimal WinRate { get; init; }
    public required decimal AverageWin { get; init; }
    public required decimal AverageLoss { get; init; }
    public required decimal ProfitFactor { get; init; }
    
    // Compatibility properties (computed)
    public decimal FinalAccountValue => FinalValue;
    
    // Additional properties for trade details if needed  
    public TradeRecord[]? Trades { get; init; }
}

/// <summary>
/// Individual trade record for detailed analysis - Compatible with existing Trade record
/// </summary>
public record TradeRecord
{
    public required string Id { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string StrategyName { get; init; }
    public required DateTime EntryTime { get; init; }
    public required DateTime ExitTime { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal ExitPrice { get; init; }
    public required decimal PnL { get; init; }
    public required decimal NetPremium { get; init; }
    public required string InstrumentType { get; init; }
}