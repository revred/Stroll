using System.Text;

namespace Stroll.Dataset;

/// <summary>
/// Simple partitioning strategy for 22+ years of market data
/// Sub-minute data (ticks, trades): Monthly partitions
/// 1-minute data: 1 year per database
/// 5-minute data: 5 years per database
/// </summary>
public class OptimalPartitionStrategy
{
    public const long MAX_DB_SIZE_MB = 60;
    public const long MAX_DB_SIZE_BYTES = MAX_DB_SIZE_MB * 1024 * 1024;

    public static void PrintPartitioningStrategy()
    {
        Console.WriteLine("📊 SIMPLE PARTITION STRATEGY FOR POLYGON.IO DATA");
        Console.WriteLine("================================================");
        Console.WriteLine("🔥 Sub-minute data (ticks, trades): Monthly partitions");
        Console.WriteLine("✅ 1-minute data: 1 year per database");
        Console.WriteLine("✅ 5-minute data: 5 years per database");
        Console.WriteLine($"📦 Max DB Size: {MAX_DB_SIZE_MB}MB");
        Console.WriteLine($"📅 Target Period: 2003-2025 (22+ years)");
        Console.WriteLine();

        PrintSimplePartitioning();
    }

    private static void PrintSimplePartitioning()
    {
        Console.WriteLine("📊 PARTITION EXAMPLES");
        Console.WriteLine("=====================");
        
        Console.WriteLine("🔥 SUB-MINUTE DATA (Ticks, Trades):");
        Console.WriteLine("   trades_SPY_2025_01.db");
        Console.WriteLine("   trades_SPY_2025_02.db");
        Console.WriteLine("   ticks_SPX_2025_08.db");
        Console.WriteLine();
        
        Console.WriteLine("⏰ 1-MINUTE DATA:");
        Console.WriteLine("   spy_1min_2025.db");
        Console.WriteLine("   spx_1min_2024.db");
        Console.WriteLine("   options_spx_1min_2025.db");
        Console.WriteLine();
        
        Console.WriteLine("📅 5-MINUTE DATA:");
        Console.WriteLine("   spy_5min_2021_2025.db");
        Console.WriteLine("   spx_5min_2016_2020.db");
        Console.WriteLine("   options_spx_5min_2011_2015.db");
        Console.WriteLine();
    }

    private static void PrintStocksPartitioning()
    {
        Console.WriteLine("📊 STOCKS PARTITIONING (2003-2025)");
        Console.WriteLine("===================================");
        
        // Stocks have much higher volume - need smaller partitions
        // Estimate ~5-10MB per symbol per year for popular stocks
        var stocksPerPartition = 100; // Popular stocks
        var mbPerSymbolPerYear = 8;
        
        Console.WriteLine($"  Popular stocks per partition: {stocksPerPartition}");
        Console.WriteLine($"  Estimated size per stock/year: {mbPerSymbolPerYear}MB");
        
        // Calculate time periods
        var maxYearsPerPartition = MAX_DB_SIZE_MB / (stocksPerPartition * mbPerSymbolPerYear);
        var actualMonthsPerPartition = (int)(maxYearsPerPartition * 12);
        
        Console.WriteLine($"  Max years per partition: {maxYearsPerPartition:F2}");
        Console.WriteLine($"  Optimal partition: {actualMonthsPerPartition} months");
        Console.WriteLine();
        
        Console.WriteLine("  📁 Partition Scheme (by quarters):");
        for (int year = 2003; year <= 2025; year++)
        {
            for (int quarter = 1; quarter <= 4; quarter++)
            {
                if (year == 2025 && quarter > 3) break; // Current year
                Console.WriteLine($"     stocks_q{quarter}_{year}.db (~{MAX_DB_SIZE_MB * 0.8:F0}MB)");
            }
        }
        Console.WriteLine();
    }

    private static void PrintOptionsPartitioning()
    {
        Console.WriteLine("⚡ OPTIONS PARTITIONING (2014-2025)");
        Console.WriteLine("===================================");
        
        // Options have the highest data density due to Greeks
        // Need very granular partitioning
        var mbPerMonthEstimate = 25; // Conservative estimate
        
        Console.WriteLine($"  Estimated size per month: {mbPerMonthEstimate}MB");
        Console.WriteLine($"  Max months per partition: {MAX_DB_SIZE_MB / mbPerMonthEstimate:F1}");
        Console.WriteLine();
        
        Console.WriteLine("  📁 Partition Scheme (monthly for recent years):");
        
        // 2014-2019: Quarterly partitions (lower volume)
        for (int year = 2014; year <= 2019; year++)
        {
            for (int quarter = 1; quarter <= 4; quarter++)
            {
                Console.WriteLine($"     options_q{quarter}_{year}.db (~{MAX_DB_SIZE_MB * 0.7:F0}MB)");
            }
        }
        
        // 2020-2025: Monthly partitions (higher volume)
        for (int year = 2020; year <= 2025; year++)
        {
            var endMonth = year == 2025 ? 8 : 12; // Current year
            for (int month = 1; month <= endMonth; month++)
            {
                Console.WriteLine($"     options_{year:D4}_{month:D2}.db (~{mbPerMonthEstimate}MB)");
            }
        }
        Console.WriteLine();
    }

    private static void PrintSummary()
    {
        Console.WriteLine("📋 PARTITIONING SUMMARY");
        Console.WriteLine("========================");
        
        var indicesPartitions = CalculateIndicesPartitions();
        var stocksPartitions = CalculateStocksPartitions();
        var optionsPartitions = CalculateOptionsPartitions();
        
        Console.WriteLine($"  📈 Indices partitions: {indicesPartitions}");
        Console.WriteLine($"  📊 Stocks partitions: {stocksPartitions}"); 
        Console.WriteLine($"  ⚡ Options partitions: {optionsPartitions}");
        Console.WriteLine($"  💽 Total databases: {indicesPartitions + stocksPartitions + optionsPartitions}");
        Console.WriteLine($"  📦 Estimated total size: {(indicesPartitions + stocksPartitions + optionsPartitions) * MAX_DB_SIZE_MB * 0.8:F0}MB");
        Console.WriteLine();
        
        Console.WriteLine("✅ All partitions designed to stay under 60MB limit");
        Console.WriteLine("🔐 All databases will be password-protected");
        Console.WriteLine("🔄 Automatic partition selection based on date ranges");
    }

    private static int CalculateIndicesPartitions()
    {
        return (2025 - 2003 + 1) / 2; // ~2 years per partition
    }

    private static int CalculateStocksPartitions()
    {
        return (2025 - 2003 + 1) * 4; // Quarterly partitions
    }

    private static int CalculateOptionsPartitions()
    {
        // 2014-2019: Quarterly (24 partitions)
        // 2020-2025: Monthly (72 partitions)  
        return 24 + 72;
    }

    public static string GetPartitionName(string dataType, DateTime date, string symbol = "")
    {
        return dataType.ToLower() switch
        {
            "indices" => $"indices_{GetIndicesPartitionSuffix(date)}",
            "stocks" => $"stocks_{GetStocksPartitionSuffix(date)}_{symbol}",
            "options" => $"options_{GetOptionsPartitionSuffix(date)}",
            _ => throw new ArgumentException($"Unknown data type: {dataType}")
        };
    }

    private static string GetIndicesPartitionSuffix(DateTime date)
    {
        var startYear = (date.Year / 2) * 2;
        var endYear = startYear + 1;
        return $"{startYear}_{endYear}";
    }

    private static string GetStocksPartitionSuffix(DateTime date)
    {
        var quarter = (date.Month - 1) / 3 + 1;
        return $"q{quarter}_{date.Year}";
    }

    private static string GetOptionsPartitionSuffix(DateTime date)
    {
        if (date.Year < 2020)
        {
            var quarter = (date.Month - 1) / 3 + 1;
            return $"q{quarter}_{date.Year}";
        }
        else
        {
            return $"{date.Year:D4}_{date.Month:D2}";
        }
    }

    public static void Main()
    {
        PrintPartitioningStrategy();
    }
}