using Stroll.Dataset;

namespace Stroll.Dataset.Tests;

public class TestSecureDataset
{
    public static async Task Main()
    {
        Console.WriteLine("🔐 Testing Secure Polygon Dataset Access");
        Console.WriteLine("==========================================");
        
        // Set environment variable for testing
        Environment.SetEnvironmentVariable("POLYGON_DB_PASSWORD", "$$rc:P0lyg0n.$0");
        
        var dataset = new SecurePolygonDataset();
        
        Console.WriteLine("\n📊 CHECKING DATABASE STATISTICS:");
        Console.WriteLine("==================================");
        
        // Test indices databases
        var indices = new[] { ("dji", 2021), ("ndx", 2021), ("vix", 2021), ("rut", 2021) };
        foreach (var (symbol, year) in indices)
        {
            var stats = await dataset.GetStatistics("Indices", symbol, year);
            Console.WriteLine($"  📈 {symbol.ToUpper()} {year}:");
            Console.WriteLine($"     Exists: {stats.Exists}");
            if (stats.Exists)
            {
                Console.WriteLine($"     Records: {stats.RecordCount:N0}");
                Console.WriteLine($"     Date Range: {stats.StartDate:yyyy-MM-dd} to {stats.EndDate:yyyy-MM-dd}");
                Console.WriteLine($"     Size: {stats.FileSizeBytes / 1024:N0} KB");
                Console.WriteLine($"     Days: {stats.UniqueDays}");
            }
            Console.WriteLine();
        }
        
        // Test options databases
        var options = new[] { ("spx", 2025), ("spx_enhanced", 2025) };
        foreach (var (symbol, year) in options)
        {
            var stats = await dataset.GetStatistics("Options", symbol, year);
            Console.WriteLine($"  📊 {symbol.ToUpper()} {year}:");
            Console.WriteLine($"     Exists: {stats.Exists}");
            if (stats.Exists)
            {
                Console.WriteLine($"     Records: {stats.RecordCount:N0}");
                Console.WriteLine($"     Date Range: {stats.StartDate:yyyy-MM-dd} to {stats.EndDate:yyyy-MM-dd}");
                Console.WriteLine($"     Size: {stats.FileSizeBytes / 1024:N0} KB");
                Console.WriteLine($"     Days: {stats.UniqueDays}");
            }
            Console.WriteLine();
        }
        
        // Generate full verification report
        Console.WriteLine("🔍 FULL VERIFICATION REPORT:");
        Console.WriteLine("=============================");
        var report = await dataset.GenerateVerificationReport();
        Console.WriteLine(report);
        
        Console.WriteLine("✅ Secure dataset test completed!");
    }
}