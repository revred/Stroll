using FluentAssertions;
using Microsoft.Extensions.Logging;
using Stroll.Historical;
using Stroll.Historical.Tests.TestData;
using Xunit;

namespace Stroll.Historical.Tests.IntegrationTests;

public class DataAcquisitionEngineTests : IDisposable
{
    private readonly string _testOutputPath;
    private readonly ILogger<DataAcquisitionEngine> _logger;

    public DataAcquisitionEngineTests()
    {
        _testOutputPath = Path.Combine(Path.GetTempPath(), "StrollHistoricalTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testOutputPath);
        
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<DataAcquisitionEngine>();
    }

    [Fact]
    public async Task DataAcquisitionEngine_ShouldAcquireDataFromProvider()
    {
        // Arrange
        var testData = GenerateTestData("SPY", new DateTime(2024, 1, 1), 5);
        var testProvider = new TestDataProvider(testData);
        
        using var engine = new DataAcquisitionEngine(_testOutputPath, initializeDefaultProviders: false);
        engine.AddProvider(testProvider);

        // Act
        var result = await engine.AcquireDataAsync(
            "SPY", 
            new DateTime(2024, 1, 1), 
            new DateTime(2024, 1, 5));

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.BarsAcquired.Should().Be(5);
        result.SuccessfulProviders.Should().Contain("Test Data Provider");
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);

        // Verify file was created
        var expectedFile = Path.Combine(_testOutputPath, "SPY_20240101_20240105.csv");
        File.Exists(expectedFile).Should().BeTrue();
    }

    [Fact]
    public async Task DataAcquisitionEngine_ShouldHandleProviderFailure()
    {
        // Arrange
        var failingProvider = new TestDataProvider();
        failingProvider.SimulateFailure = true;
        
        using var engine = new DataAcquisitionEngine(_testOutputPath, initializeDefaultProviders: false);
        engine.AddProvider(failingProvider);

        // Act
        var result = await engine.AcquireDataAsync(
            "SPY", 
            new DateTime(2024, 1, 1), 
            new DateTime(2024, 1, 5));

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.FailedProviders.Should().Contain("Test Data Provider");
    }

    [Fact]
    public async Task DataAcquisitionEngine_ShouldFallbackToSecondProvider()
    {
        // Arrange
        var failingProvider = new TestDataProvider() { Priority = 0 };
        failingProvider.SimulateFailure = true;
        
        var workingProvider = new TestDataProvider(GenerateTestData("SPY", new DateTime(2024, 1, 1), 3))
        { Priority = 1 };
        
        using var engine = new DataAcquisitionEngine(_testOutputPath, initializeDefaultProviders: false);
        engine.AddProvider(failingProvider);
        engine.AddProvider(workingProvider);

        // Act
        var result = await engine.AcquireDataAsync(
            "SPY", 
            new DateTime(2024, 1, 1), 
            new DateTime(2024, 1, 3));

        // Assert
        result.Success.Should().BeTrue();
        result.BarsAcquired.Should().Be(3);
        result.SuccessfulProviders.Should().Contain("Test Data Provider");
        result.FailedProviders.Should().Contain("Test Data Provider"); // First provider failed
    }

    [Fact]
    public async Task DataAcquisitionEngine_ShouldGetProviderStatus()
    {
        // Arrange
        var provider1 = new TestDataProvider() { Priority = 0 };
        var provider2 = new TestDataProvider() { Priority = 1 };
        provider2.SimulateFailure = true;
        
        using var engine = new DataAcquisitionEngine(_testOutputPath, initializeDefaultProviders: false);
        engine.AddProvider(provider1);
        engine.AddProvider(provider2);

        // Act
        var statuses = await engine.GetProviderStatusAsync();

        // Assert
        statuses.Should().HaveCount(2);
        
        var healthyProvider = statuses.First(s => s.Priority == 0);
        healthyProvider.IsHealthy.Should().BeTrue();
        healthyProvider.IsAvailable.Should().BeTrue();
        
        var unhealthyProvider = statuses.First(s => s.Priority == 1);
        unhealthyProvider.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public async Task DataAcquisitionEngine_ShouldHandleEmptyDateRange()
    {
        // Arrange
        var testProvider = new TestDataProvider();
        using var engine = new DataAcquisitionEngine(_testOutputPath, initializeDefaultProviders: false);
        engine.AddProvider(testProvider);

        // Act
        var result = await engine.AcquireDataAsync(
            "SPY", 
            new DateTime(2024, 12, 31), 
            new DateTime(2024, 12, 30)); // End before start

        // Assert
        result.Success.Should().BeTrue();
        result.BarsAcquired.Should().Be(0);
    }

    [Fact]
    public async Task DataAcquisitionEngine_ShouldRemoveDuplicateData()
    {
        // Arrange
        var duplicateData = new List<MarketDataBar>
        {
            new() { Timestamp = new DateTime(2024, 1, 1), Open = 100, High = 105, Low = 95, Close = 103, Volume = 1000000, VWAP = 101 },
            new() { Timestamp = new DateTime(2024, 1, 1), Open = 100, High = 105, Low = 95, Close = 103, Volume = 1000000, VWAP = 101 }, // Duplicate
            new() { Timestamp = new DateTime(2024, 1, 2), Open = 103, High = 107, Low = 102, Close = 106, Volume = 1200000, VWAP = 104.5 }
        };
        var testProvider = new TestDataProvider(duplicateData);
        using var engine = new DataAcquisitionEngine(_testOutputPath, initializeDefaultProviders: false);
        engine.AddProvider(testProvider);

        // Act
        var result = await engine.AcquireDataAsync(
            "SPY", 
            new DateTime(2024, 1, 1), 
            new DateTime(2024, 1, 2));

        // Assert
        result.Success.Should().BeTrue();
        result.BarsAcquired.Should().Be(2); // Duplicates should be removed
    }

    private static List<MarketDataBar> GenerateTestData(string symbol, DateTime startDate, int count)
    {
        var bars = new List<MarketDataBar>();
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            var price = 100.0 + i;
            bars.Add(new MarketDataBar
            {
                Timestamp = startDate.AddDays(i),
                Open = price,
                High = price + 2,
                Low = price - 2,
                Close = price + 1,
                Volume = random.Next(1000000, 5000000),
                VWAP = price + 0.5
            });
        }

        return bars;
    }

    public void Dispose()
    {
        if (Directory.Exists(_testOutputPath))
        {
            Directory.Delete(_testOutputPath, true);
        }
    }
}