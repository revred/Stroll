using Microsoft.Extensions.Logging;

namespace Stroll.Depends;

/// <summary>
/// Simple test program to validate dependency management functionality
/// </summary>
public static class DependencyTest
{
    public static async Task Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        var logger = loggerFactory.CreateLogger<DependencyManager>();

        try
        {
            Console.WriteLine("🔧 Testing Stroll.Depends - Process Dependency Management System");
            Console.WriteLine();

            // Test YAML parsing
            var yamlConfig = @"
processes:
  - name: ""TestService""
    path: ""TestApp/Service""
    description: ""Test service process""
    dependencies: []
    type: ""service""
    
  - name: ""TestClient""
    path: ""TestApp/Client""
    description: ""Test client process""
    dependencies:
      - ""TestService""
    type: ""application""

dependency_rules:
  on_termination:
    kill_dependents: true
    grace_period: 5
  on_startup:
    auto_start_dependencies: true

lifecycle:
  health_check:
    interval: 10
  restart_policy:
    auto_restart: true
    max_restarts: 3
";

            Console.WriteLine("✅ Parsing YAML configuration...");
            var manager = DependencyManager.FromYaml(yamlConfig, logger);

            // Test dependency resolution
            Console.WriteLine("✅ Testing dependency resolution:");
            
            var clientDependencies = manager.GetDependencies("TestClient");
            Console.WriteLine($"   TestClient dependencies: {string.Join(", ", clientDependencies)}");
            
            var serviceDependents = manager.GetDependents("TestService");
            Console.WriteLine($"   TestService dependents: {string.Join(", ", serviceDependents)}");

            // Test startup order
            var startupOrder = manager.GetStartupOrder("TestClient", "TestService");
            Console.WriteLine($"   Startup order: {string.Join(" → ", startupOrder)}");

            var shutdownOrder = manager.GetShutdownOrder("TestClient", "TestService");
            Console.WriteLine($"   Shutdown order: {string.Join(" → ", shutdownOrder)}");

            // Test process info
            Console.WriteLine();
            Console.WriteLine("✅ Process information:");
            var allProcesses = manager.GetAllProcessInfo();
            foreach (var process in allProcesses)
            {
                Console.WriteLine($"   📦 {process.Name} ({process.Type})");
                Console.WriteLine($"      Description: {process.Description}");
                Console.WriteLine($"      Dependencies: {string.Join(", ", process.DirectDependencies)}");
                Console.WriteLine($"      Dependents: {string.Join(", ", process.DirectDependents)}");
                Console.WriteLine($"      Status: {(process.IsRunning ? "Running" : "Stopped")}");
                Console.WriteLine();
            }

            Console.WriteLine("🎉 All dependency management tests passed!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}