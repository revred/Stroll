namespace Stroll.Dataset;

/// <summary>
/// Test runner for Options QA Test Suite
/// Executes comprehensive validation of MCP service with synthetic options data
/// </summary>
public class TestRunner
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Stroll.Dataset Options QA Test Suite");
        Console.WriteLine("Testing MCP service with 10,000 synthetic options datapoints");
        Console.WriteLine("Validating: ingestion, Greeks computation, distributed queries, performance");
        Console.WriteLine();

        try
        {
            // Set up environment
            Environment.SetEnvironmentVariable("POLYGON_DB_PASSWORD", "$rc:P0lyg0n.$0");
            
            var qaTest = new OptionsQATest();
            await qaTest.RunCompleteQATest();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test runner failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Environment.Exit(1);
        }

        Console.WriteLine("\n✅ Test runner completed successfully!");
        
        if (args.Contains("--wait"))
        {
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}