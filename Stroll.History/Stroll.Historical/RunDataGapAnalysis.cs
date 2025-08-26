using Microsoft.Extensions.Logging;
using Stroll.Storage;

namespace Stroll.Historical;

/// <summary>
/// Direct runner for SPX data gap analysis
/// </summary>
public class RunDataGapAnalysis
{
    public static async Task Main(string[] args)
    {
        // Set up logging
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<RunDataGapAnalysis>();
        
        logger.LogInformation("🚀 Starting SPX Data Gap Analysis");
        
        try
        {
            // Set up storage
            var catalog = DataCatalog.Default("./data");
            var storage = new CompositeStorage(catalog);
            
            // Create gap analysis
            var gapAnalysis = new DataGapAnalysis(storage, loggerFactory.CreateLogger<DataGapAnalysis>());
            
            // Run analysis
            var report = await gapAnalysis.AnalyzeSpxDataAsync();
            
            // Display results
            logger.LogInformation("📊 SPX DATA GAP ANALYSIS RESULTS");
            logger.LogInformation("═══════════════════════════════════════════════");
            logger.LogInformation("📅 Analysis Period: {StartDate} to {EndDate}", 
                report.StartDate.ToString("yyyy-MM-dd"), report.EndDate.ToString("yyyy-MM-dd"));
            logger.LogInformation("📊 Coverage: {Coverage:P2} ({Available} data points available)", 
                report.CoveragePercentage, report.AvailableDataPoints);
            logger.LogInformation("❌ Data Gaps: {GapCount} periods missing", report.DataGaps.Count);
            
            if (report.DataGaps.Any())
            {
                logger.LogInformation("🔍 Major Data Gaps:");
                foreach (var gap in report.DataGaps.Take(10))
                {
                    var gapDays = (gap.End - gap.Start).Days + 1;
                    logger.LogInformation("   • {Start} to {End} ({Days} days)", 
                        gap.Start.ToString("yyyy-MM-dd"), gap.End.ToString("yyyy-MM-dd"), gapDays);
                }
            }
            
            if (report.AcquisitionStrategy.Any())
            {
                logger.LogInformation("💡 ACQUISITION STRATEGY:");
                foreach (var task in report.AcquisitionStrategy.Take(5))
                {
                    logger.LogInformation("   🎯 {Priority}: {Start} to {End} ({Points} points, ~{Minutes}min)", 
                        task.Priority, task.DateRange.Start.ToString("yyyy-MM-dd"), 
                        task.DateRange.End.ToString("yyyy-MM-dd"), 
                        task.EstimatedDataPoints, task.EstimatedDurationMinutes);
                    logger.LogInformation("      Provider: {Provider}", task.SuggestedProvider);
                }
            }
            
            var totalYears = (report.EndDate - report.StartDate).Days / 365.0;
            logger.LogInformation("📈 BACKTEST READINESS:");
            if (report.CoveragePercentage > 0.9m)
            {
                logger.LogInformation("   ✅ EXCELLENT - Ready for comprehensive backtesting");
            }
            else if (report.CoveragePercentage > 0.7m)
            {
                logger.LogInformation("   ⚠️  GOOD - Some gaps may affect backtest accuracy");
            }
            else if (report.CoveragePercentage > 0.3m)
            {
                logger.LogInformation("   ❌ POOR - Significant data acquisition needed");
            }
            else
            {
                logger.LogInformation("   🚨 CRITICAL - Full data acquisition required");
            }
            
            logger.LogInformation("═══════════════════════════════════════════════");
            logger.LogInformation("✅ Analysis Complete");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Data gap analysis failed");
        }
    }
}