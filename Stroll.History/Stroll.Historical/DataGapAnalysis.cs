using Microsoft.Extensions.Logging;
using Stroll.Storage;
using System.Globalization;

namespace Stroll.Historical;

/// <summary>
/// Analyzes data gaps for SPX historical data from Sep 9, 1999 to Aug 24, 2025
/// Identifies missing periods and provides acquisition strategy
/// </summary>
public class DataGapAnalysis
{
    private readonly ILogger<DataGapAnalysis> _logger;
    private readonly IStorageProvider _storage;
    
    // Target date range for 1DTE backtest
    private readonly DateTime _startDate = new DateTime(1999, 9, 9);
    private readonly DateTime _endDate = new DateTime(2025, 8, 24);

    public DataGapAnalysis(IStorageProvider storage, ILogger<DataGapAnalysis>? logger = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DataGapAnalysis>.Instance;
    }

    /// <summary>
    /// Analyze SPX data availability and identify gaps
    /// </summary>
    public async Task<DataGapReport> AnalyzeSpxDataAsync()
    {
        _logger.LogInformation("🔍 Analyzing SPX data availability from {StartDate} to {EndDate}", 
            _startDate.ToString("yyyy-MM-dd"), _endDate.ToString("yyyy-MM-dd"));
        
        var report = new DataGapReport
        {
            Symbol = "SPX",
            StartDate = _startDate,
            EndDate = _endDate,
            AnalysisDate = DateTime.UtcNow
        };

        // Check data availability in yearly chunks for performance
        var dataPoints = new List<DataPoint>();
        var gaps = new List<DateRange>();
        
        var currentYear = _startDate.Year;
        var endYear = _endDate.Year;
        
        while (currentYear <= endYear)
        {
            var yearStart = new DateTime(currentYear, 1, 1);
            if (currentYear == _startDate.Year) yearStart = _startDate;
            
            var yearEnd = new DateTime(currentYear, 12, 31);
            if (currentYear == _endDate.Year) yearEnd = _endDate;
            
            _logger.LogInformation("📅 Checking data for year {Year}", currentYear);
            
            var yearData = await CheckYearDataAsync(yearStart, yearEnd);
            dataPoints.AddRange(yearData.DataPoints);
            gaps.AddRange(yearData.Gaps);
            
            currentYear++;
        }
        
        report.AvailableDataPoints = dataPoints.Count;
        report.DataGaps = gaps;
        report.CoveragePercentage = CalculateCoveragePercentage(dataPoints, gaps);
        
        // Generate acquisition strategy
        report.AcquisitionStrategy = GenerateAcquisitionStrategy(gaps);
        
        _logger.LogInformation("📊 Analysis Complete: {Coverage:P1} coverage, {DataPoints} points, {Gaps} gaps", 
            report.CoveragePercentage, report.AvailableDataPoints, report.DataGaps.Count);
        
        return report;
    }

    private async Task<YearDataCheck> CheckYearDataAsync(DateTime yearStart, DateTime yearEnd)
    {
        var result = new YearDataCheck();
        
        try
        {
            // Check monthly chunks for better granularity
            var currentMonth = yearStart;
            
            while (currentMonth <= yearEnd)
            {
                var monthEnd = new DateTime(currentMonth.Year, currentMonth.Month, 
                    DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month));
                if (monthEnd > yearEnd) monthEnd = yearEnd;
                
                var monthData = await CheckMonthDataAsync(currentMonth, monthEnd);
                result.DataPoints.AddRange(monthData.DataPoints);
                result.Gaps.AddRange(monthData.Gaps);
                
                currentMonth = currentMonth.AddMonths(1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking year data {YearStart} to {YearEnd}", 
                yearStart.ToString("yyyy-MM-dd"), yearEnd.ToString("yyyy-MM-dd"));
            
            // Treat entire year as gap if we can't check it
            result.Gaps.Add(new DateRange { Start = yearStart, End = yearEnd });
        }
        
        return result;
    }

    private async Task<MonthDataCheck> CheckMonthDataAsync(DateTime monthStart, DateTime monthEnd)
    {
        var result = new MonthDataCheck();
        
        try
        {
            var fromDate = DateOnly.FromDateTime(monthStart);
            var toDate = DateOnly.FromDateTime(monthEnd);
            
            var bars = await _storage.GetBarsRawAsync("SPX", fromDate, toDate, Granularity.Daily);
            
            if (bars.Any())
            {
                foreach (var bar in bars)
                {
                    if (DateTime.TryParse(bar["t"]?.ToString(), out var barDate))
                    {
                        result.DataPoints.Add(new DataPoint
                        {
                            Date = barDate,
                            Close = Convert.ToDecimal(bar["c"] ?? 0),
                            Volume = Convert.ToInt64(bar["v"] ?? 0)
                        });
                    }
                }
                
                _logger.LogDebug("✅ Found {Count} bars for {Month}", bars.Count, monthStart.ToString("yyyy-MM"));
            }
            else
            {
                // No data for this month - add as gap
                result.Gaps.Add(new DateRange { Start = monthStart, End = monthEnd });
                _logger.LogDebug("❌ No data found for {Month}", monthStart.ToString("yyyy-MM"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking month data {Month}", monthStart.ToString("yyyy-MM"));
            result.Gaps.Add(new DateRange { Start = monthStart, End = monthEnd });
        }
        
        return result;
    }

    private decimal CalculateCoveragePercentage(List<DataPoint> dataPoints, List<DateRange> gaps)
    {
        var totalDays = (_endDate - _startDate).Days + 1;
        var gapDays = gaps.Sum(g => (g.End - g.Start).Days + 1);
        var coverageDays = totalDays - gapDays;
        
        return totalDays > 0 ? (decimal)coverageDays / totalDays : 0m;
    }

    private List<AcquisitionTask> GenerateAcquisitionStrategy(List<DateRange> gaps)
    {
        var tasks = new List<AcquisitionTask>();
        
        // Group consecutive gaps and prioritize by recency
        var prioritizedGaps = gaps
            .OrderByDescending(g => g.End) // Most recent first
            .ToList();
        
        foreach (var gap in prioritizedGaps)
        {
            var years = (gap.End - gap.Start).Days / 365.0;
            var priority = years > 5 ? Priority.Low : 
                          years > 1 ? Priority.Medium : Priority.High;
            
            tasks.Add(new AcquisitionTask
            {
                DateRange = gap,
                Priority = priority,
                EstimatedDataPoints = CalculateExpectedTradingDays(gap.Start, gap.End),
                SuggestedProvider = SuggestDataProvider(gap.Start, gap.End),
                EstimatedDurationMinutes = EstimateAcquisitionTime(gap.Start, gap.End)
            });
        }
        
        return tasks.OrderByDescending(t => t.Priority).ThenByDescending(t => t.DateRange.End).ToList();
    }

    private int CalculateExpectedTradingDays(DateTime start, DateTime end)
    {
        var tradingDays = 0;
        var current = start;
        
        while (current <= end)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                tradingDays++;
            }
            current = current.AddDays(1);
        }
        
        // Approximate holiday reduction (about 10 holidays per year)
        var years = (end - start).Days / 365.0;
        var holidays = (int)(years * 10);
        
        return Math.Max(0, tradingDays - holidays);
    }

    private string SuggestDataProvider(DateTime start, DateTime end)
    {
        // Recent data (last 2 years) - Yahoo Finance is reliable
        if (start >= DateTime.Now.AddYears(-2))
            return "Yahoo Finance";
        
        // Historical data (pre-2020) - Alpha Vantage or Stooq
        if (end < new DateTime(2020, 1, 1))
            return "Alpha Vantage Premium";
        
        // Mixed range - combination strategy
        return "Yahoo Finance + Alpha Vantage";
    }

    private int EstimateAcquisitionTime(DateTime start, DateTime end)
    {
        var days = (end - start).Days;
        // Estimate 1 second per day for API calls + processing
        return Math.Max(1, days / 60); // Convert to minutes
    }
}

// Data structures for gap analysis
public record DataGapReport
{
    public required string Symbol { get; init; }
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required DateTime AnalysisDate { get; init; }
    public int AvailableDataPoints { get; set; }
    public decimal CoveragePercentage { get; set; }
    public List<DateRange> DataGaps { get; set; } = new();
    public List<AcquisitionTask> AcquisitionStrategy { get; set; } = new();
}

public record DateRange
{
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    public int TotalDays => (End - Start).Days + 1;
}

public record DataPoint
{
    public required DateTime Date { get; init; }
    public decimal Close { get; init; }
    public long Volume { get; init; }
}

public record AcquisitionTask
{
    public required DateRange DateRange { get; init; }
    public required Priority Priority { get; init; }
    public int EstimatedDataPoints { get; init; }
    public required string SuggestedProvider { get; init; }
    public int EstimatedDurationMinutes { get; init; }
}

record YearDataCheck
{
    public List<DataPoint> DataPoints { get; } = new();
    public List<DateRange> Gaps { get; } = new();
}

record MonthDataCheck
{
    public List<DataPoint> DataPoints { get; } = new();
    public List<DateRange> Gaps { get; } = new();
}

public enum Priority
{
    Low = 1,
    Medium = 2, 
    High = 3
}