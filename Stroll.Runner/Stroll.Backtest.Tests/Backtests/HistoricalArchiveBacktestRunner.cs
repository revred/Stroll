using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using Stroll.Backtest.Tests.Core;
using Stroll.Backtest.Tests.Strategy;
using System.Globalization;

namespace Stroll.Backtest.Tests.Backtests;

/// <summary>
/// Backtest runner using SQLite historical archive with Bar Magnifier for 1-minute precision
/// Uses the 35,931 bars of 5-minute SPY data acquired from Alpha Vantage
/// Applies Bar Magnifier to create synthetic 1-minute data for 1DTE strategy testing
/// </summary>
public class HistoricalArchiveBacktestRunner
{
    private readonly ILogger<HistoricalArchiveBacktestRunner> _logger;
    private readonly string _archivePath;
    private readonly SpxOneDteStrategy _strategy;
    private readonly RealFillEngine _fillEngine;
    private readonly Dictionary<string, Position> _activePositions;
    private readonly List<Trade> _completedTrades;
    
    private decimal _accountValue = 100000m;
    private decimal _maxDrawdown = 0m;
    private decimal _peakAccountValue = 100000m;

    public HistoricalArchiveBacktestRunner(string archivePath, ILogger<HistoricalArchiveBacktestRunner>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HistoricalArchiveBacktestRunner>.Instance;
        _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
        _strategy = new SpxOneDteStrategy(null, seed: 42);
        _fillEngine = new RealFillEngine(null, seed: 42);
        _activePositions = new Dictionary<string, Position>();
        _completedTrades = new List<Trade>();
    }

    /// <summary>
    /// Run backtest using available SQLite archive data (6 months of 5-minute bars)
    /// </summary>
    public async Task<BacktestResult> RunSixMonthBacktestAsync()
    {
        _logger.LogInformation("🚀 Starting 6-Month Historical Archive Backtest");
        _logger.LogInformation("📊 Data Source: {ArchivePath}", _archivePath);
        _logger.LogInformation("🔧 Using Bar Magnifier for 1-minute precision");

        var (startDate, endDate, totalBars) = await GetArchiveDataRange();
        _logger.LogInformation("📅 Backtest Period: {StartDate} to {EndDate}", 
            startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
        _logger.LogInformation("📈 Total 5-Minute Bars: {TotalBars:N0}", totalBars);

        var processedDays = 0;
        var currentDate = startDate;

        while (currentDate <= endDate)
        {
            try
            {
                await ProcessTradingDay(currentDate);
                processedDays++;

                if (processedDays % 10 == 0)
                {
                    var totalDays = (endDate - startDate).Days + 1;
                    var progressPct = (double)processedDays / totalDays * 100;
                    _logger.LogInformation("📊 Progress: {Progress:F1}% ({ProcessedDays}/{TotalDays} days), Account: ${AccountValue:N0}",
                        progressPct, processedDays, totalDays, _accountValue);
                }

                currentDate = currentDate.AddDays(1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing {Date}", currentDate.ToString("yyyy-MM-dd"));
                currentDate = currentDate.AddDays(1);
            }
        }

        _logger.LogInformation("✅ 6-Month backtest completed successfully");
        return GenerateBacktestResult(startDate, endDate);
    }

    private async Task ProcessTradingDay(DateTime date)
    {
        // Skip weekends
        if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            return;

        // Get 5-minute bars for the day
        var fiveMinuteBars = await GetDayBarsAsync(date);
        if (!fiveMinuteBars.Any())
        {
            _logger.LogDebug("No data for {Date}", date.ToString("yyyy-MM-dd"));
            return;
        }

        _logger.LogDebug("Processing {Date}: {Count} five-minute bars", date.ToString("yyyy-MM-dd"), fiveMinuteBars.Count);

        // Process each 5-minute bar, using Bar Magnifier for intrabar precision when needed
        foreach (var fiveMinBar in fiveMinuteBars)
        {
            await ProcessFiveMinuteBar(fiveMinBar);
        }

        // End of day cleanup
        await ProcessEndOfDay(date);
    }

    private async Task ProcessFiveMinuteBar(Bar5m fiveMinBar)
    {
        var marketData = CreateMarketData(fiveMinBar);

        // Strategy typically needs 1-minute precision for entries/exits
        // Use Bar Magnifier to create synthetic 1-minute bars
        var oneMinuteBars = BarMagnifier.ToMinutes(fiveMinBar, MagnifierMode.Conservative).ToList();

        _logger.LogDebug("Magnified {Timestamp} -> {Count} 1-minute bars", 
            fiveMinBar.T.ToString("HH:mm"), oneMinuteBars.Count);

        // Process each synthetic 1-minute bar for precise timing
        foreach (var oneMinBar in oneMinuteBars)
        {
            var preciseMarketData = CreateMarketDataFromOneMin(oneMinBar, marketData);

            // Manage existing positions with 1-minute precision
            await ManageExistingPositions(oneMinBar.T, preciseMarketData);

            // Generate new signals with 1-minute precision
            await ProcessNewSignals(oneMinBar.T, preciseMarketData);
        }
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
            if (_activePositions.Count < 2) // Conservative position sizing
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

        // Execute each leg with realistic fill simulation
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
                return;
            }
        }

        trade.Fills = fills.ToArray();
        trade.NetPremium = totalPremium;

        if (signal.SignalType == SignalType.Entry)
        {
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
            _accountValue += totalPremium;

            _logger.LogInformation("🔵 Opened {Strategy} position: ${Premium:F2} at {Time}",
                signal.StrategyName, totalPremium, signal.Timestamp.ToString("MM/dd HH:mm"));
        }
        else
        {
            if (_activePositions.TryGetValue(trade.Id, out var closingPosition))
            {
                _activePositions.Remove(trade.Id);
                _accountValue += totalPremium;

                var totalPnL = closingPosition.OpenValue + totalPremium;
                trade.PnL = totalPnL;
                _completedTrades.Add(trade);

                _logger.LogInformation("🔴 Closed position: P&L ${PnL:F2} ({Reason}) at {Time}",
                    totalPnL, signal.Reason ?? "Unknown", signal.Timestamp.ToString("MM/dd HH:mm"));
            }
        }

        UpdatePerformanceMetrics();
    }

    private async Task ProcessEndOfDay(DateTime date)
    {
        var expiringPositions = _activePositions.Values
            .Where(p => p.Expiration.Date <= date.Date)
            .ToList();

        foreach (var position in expiringPositions)
        {
            var closeSignal = CreateForceCloseSignal(position, date.AddHours(16)); // 4 PM close
            var marketData = new MarketData
            {
                Timestamp = date.AddHours(16),
                SpxPrice = 4500m, // Simplified
                ImpliedVolatility = 0.20m,
                VolumeRatio = 1.0m,
                IsMarketOpen = false
            };
            await ExecuteSignal(closeSignal, marketData);
        }
    }

    private async Task<List<Bar5m>> GetDayBarsAsync(DateTime date)
    {
        var bars = new List<Bar5m>();
        
        using var connection = new SqliteConnection($"Data Source={_archivePath}");
        await connection.OpenAsync();

        var sql = @"
            SELECT timestamp, open, high, low, close, volume 
            FROM intraday_bars 
            WHERE symbol = 'SPY' 
            AND DATE(timestamp) = @date 
            ORDER BY timestamp";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@date", date.ToString("yyyy-MM-dd"));

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            bars.Add(new Bar5m(
                T: reader.GetDateTime(0),
                O: reader.GetDecimal(1),
                H: reader.GetDecimal(2),
                L: reader.GetDecimal(3),
                C: reader.GetDecimal(4),
                V: reader.GetInt64(5)
            ));
        }

        return bars;
    }

    private async Task<(DateTime StartDate, DateTime EndDate, int TotalBars)> GetArchiveDataRange()
    {
        using var connection = new SqliteConnection($"Data Source={_archivePath}");
        await connection.OpenAsync();

        var sql = @"
            SELECT 
                MIN(timestamp) as start_date,
                MAX(timestamp) as end_date,
                COUNT(*) as total_bars
            FROM intraday_bars 
            WHERE symbol = 'SPY'";

        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return (
                StartDate: reader.GetDateTime(0).Date,
                EndDate: reader.GetDateTime(1).Date,
                TotalBars: reader.GetInt32(2)
            );
        }

        throw new InvalidOperationException("No data found in archive");
    }

    private MarketData CreateMarketData(Bar5m bar)
    {
        return new MarketData
        {
            Timestamp = bar.T,
            SpxPrice = bar.C,
            ImpliedVolatility = CalculateImpliedVolatility(bar),
            VolumeRatio = 1.0m, // Simplified
            IsMarketOpen = IsMarketHours(bar.T)
        };
    }

    private MarketData CreateMarketDataFromOneMin(Bar1m bar, MarketData baseData)
    {
        return baseData with
        {
            Timestamp = bar.T,
            SpxPrice = bar.C
        };
    }

    private decimal CalculateImpliedVolatility(Bar5m bar)
    {
        // Simplified IV calculation based on price range
        if (bar.H == bar.L) return 0.15m; // Minimum IV
        
        var range = bar.H - bar.L;
        var price = bar.C;
        var rangePercent = price > 0 ? range / price : 0;
        
        // Convert intraday range to annualized volatility estimate
        var dailyVol = rangePercent * (decimal)Math.Sqrt(252); // Trading days
        return Math.Max(0.10m, Math.Min(0.50m, dailyVol)); // Cap between 10% and 50%
    }

    private bool IsMarketHours(DateTime timestamp)
    {
        return timestamp.DayOfWeek != DayOfWeek.Saturday &&
               timestamp.DayOfWeek != DayOfWeek.Sunday &&
               timestamp.Hour >= 9 && timestamp.Hour < 16;
    }

    // Generate option quotes, orders, market conditions (same as existing backtest)
    private OptionQuote GenerateOptionQuote(OptionLeg leg, MarketData marketData)
    {
        var timeToExpiry = Math.Max(0.001, Math.Min((leg.Expiration - marketData.Timestamp).TotalDays / 365.0, 1.0));
        var intrinsic = Math.Max(0, leg.OptionType == OptionType.Call
            ? marketData.SpxPrice - leg.Strike
            : leg.Strike - marketData.SpxPrice);

        var timeValue = marketData.SpxPrice * marketData.ImpliedVolatility * (decimal)Math.Sqrt(timeToExpiry) / 8;
        var theoreticalValue = intrinsic + timeValue;
        var spread = Math.Max(0.05m, theoreticalValue * 0.015m);

        return new OptionQuote
        {
            Bid = Math.Max(0.01m, theoreticalValue - spread / 2),
            Ask = theoreticalValue + spread / 2,
            BidSize = 5,
            AskSize = 5,
            ImpliedVolatility = marketData.ImpliedVolatility,
            Delta = 0.15m,
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
            OrderType = OrderType.Market,
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

    private BacktestResult GenerateBacktestResult(DateTime startDate, DateTime endDate)
    {
        var totalReturn = (_accountValue - 100000m) / 100000m;
        var winningTrades = _completedTrades.Where(t => t.PnL > 0).ToList();
        var losingTrades = _completedTrades.Where(t => t.PnL <= 0).ToList();

        return new BacktestResult
        {
            StartDate = startDate,
            EndDate = endDate,
            StartingCapital = 100000m,
            FinalAccountValue = _accountValue,
            TotalReturn = totalReturn,
            AnnualizedReturn = CalculateAnnualizedReturn(totalReturn, (endDate - startDate).Days),
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

        var returnRatio = 1 + totalReturn;
        if (returnRatio <= 0) return -1m;

        var result = Math.Pow((double)returnRatio, (double)(1 / years)) - 1;
        return (decimal)Math.Max(-1.0, Math.Min(10.0, result));
    }
}