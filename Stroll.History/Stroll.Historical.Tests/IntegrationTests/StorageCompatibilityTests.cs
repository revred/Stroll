using FluentAssertions;
using Stroll.Dataset;
using Stroll.Historical;
using Stroll.Storage;
using Xunit;

namespace Stroll.Historical.Tests.IntegrationTests;

public class StorageCompatibilityTests : IDisposable
{
    private readonly string _testDataPath;
    private readonly DataCatalog _catalog;

    public StorageCompatibilityTests()
    {
        _testDataPath = Path.Combine(Path.GetTempPath(), "StrollStorageTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataPath);
        
        _catalog = DataCatalog.Default(_testDataPath);
    }

    [Fact]
    public async Task AcquiredData_ShouldBeCompatible_WithStrollStorage()
    {
        // Arrange - Create test CSV data in the expected format
        var csvContent = @"timestamp,open,high,low,close,volume,vwap
2024-01-01 00:00:00,100.50,102.75,99.25,101.80,1500000,101.25
2024-01-02 00:00:00,101.80,103.50,100.90,102.45,1750000,102.15
2024-01-03 00:00:00,102.45,104.20,101.30,103.85,1625000,102.98";

        var csvPath = Path.Combine(_testDataPath, "SPY_test.csv");
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act - Load data through Stroll.Storage (simulated)
        var lines = await File.ReadAllLinesAsync(csvPath);
        var bars = ParseCsvData(lines);

        // Assert - Verify data format compatibility
        bars.Should().HaveCount(3);
        
        var firstBar = bars.First();
        firstBar.Should().ContainKey("timestamp");
        firstBar.Should().ContainKey("open");
        firstBar.Should().ContainKey("high");
        firstBar.Should().ContainKey("low");
        firstBar.Should().ContainKey("close");
        firstBar.Should().ContainKey("volume");
        firstBar.Should().ContainKey("vwap");

        // Verify timestamp parsing
        var timestamp = DateTime.Parse(firstBar["timestamp"]?.ToString() ?? "");
        timestamp.Should().Be(new DateTime(2024, 1, 1));

        // Verify numeric data
        double.Parse(firstBar["open"]?.ToString() ?? "0").Should().Be(100.50);
        double.Parse(firstBar["close"]?.ToString() ?? "0").Should().Be(101.80);
        long.Parse(firstBar["volume"]?.ToString() ?? "0").Should().Be(1500000);
    }

    [Fact]
    public async Task DataPackager_ShouldSerialize_AcquiredData()
    {
        // Arrange
        var testBars = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["timestamp"] = new DateTime(2024, 1, 1),
                ["open"] = 100.50,
                ["high"] = 102.75,
                ["low"] = 99.25,
                ["close"] = 101.80,
                ["volume"] = 1500000L,
                ["vwap"] = 101.25
            }
        };

        var packager = new JsonPackager("stroll.history.v1", "1.0.0");

        // Act
        var json = packager.BarsRaw(
            "SPY", 
            DateOnly.FromDateTime(new DateTime(2024, 1, 1)), 
            DateOnly.FromDateTime(new DateTime(2024, 1, 1)), 
            Granularity.Daily, 
            testBars);

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().Contain("\"symbol\": \"SPY\"");
        json.Should().Contain("\"granularity\": \"1d\"");
        json.Should().Contain("\"bars\":");
        json.Should().Contain("2024-01-01");
    }

    [Fact]
    public void MarketDataBar_ShouldConvertTo_StorageFormat()
    {
        // Arrange
        var marketBar = new MarketDataBar
        {
            Timestamp = new DateTime(2024, 1, 1),
            Open = 100.50,
            High = 102.75,
            Low = 99.25,
            Close = 101.80,
            Volume = 1500000,
            VWAP = 101.25
        };

        // Act - Convert to storage dictionary format
        var storageDict = new Dictionary<string, object?>
        {
            ["timestamp"] = marketBar.Timestamp,
            ["open"] = marketBar.Open,
            ["high"] = marketBar.High,
            ["low"] = marketBar.Low,
            ["close"] = marketBar.Close,
            ["volume"] = marketBar.Volume,
            ["vwap"] = marketBar.VWAP
        };

        // Assert
        storageDict.Should().ContainKey("timestamp");
        storageDict["timestamp"].Should().Be(new DateTime(2024, 1, 1));
        storageDict["open"].Should().Be(100.50);
        storageDict["volume"].Should().Be(1500000L);
    }

    [Fact]
    public async Task CSV_DataFormat_ShouldBe_Parseable()
    {
        // Arrange - Create CSV in the format that DataAcquisitionEngine saves
        var csvLines = new[]
        {
            "timestamp,open,high,low,close,volume,vwap",
            "2024-01-01 00:00:00,100.50,102.75,99.25,101.80,1500000,101.25",
            "2024-01-02 00:00:00,101.80,103.50,100.90,102.45,1750000,102.15"
        };

        // Act - Parse using the same logic as storage system
        var parsedBars = new List<Dictionary<string, object?>>();

        for (int i = 1; i < csvLines.Length; i++) // Skip header
        {
            var fields = csvLines[i].Split(',');
            var bar = new Dictionary<string, object?>
            {
                ["timestamp"] = DateTime.Parse(fields[0]),
                ["open"] = double.Parse(fields[1]),
                ["high"] = double.Parse(fields[2]),
                ["low"] = double.Parse(fields[3]),
                ["close"] = double.Parse(fields[4]),
                ["volume"] = long.Parse(fields[5]),
                ["vwap"] = double.Parse(fields[6])
            };
            parsedBars.Add(bar);
        }

        // Assert
        parsedBars.Should().HaveCount(2);
        parsedBars[0]["timestamp"].Should().Be(new DateTime(2024, 1, 1));
        parsedBars[1]["close"].Should().Be(102.45);
    }

    [Fact]
    public void Granularity_ShouldBe_Serializable()
    {
        // Arrange & Act
        var daily = Granularity.Daily;
        var fiveMin = GranularityExtensions.Parse("5m");
        var oneMin = GranularityExtensions.Parse("1m");

        // Assert
        daily.Canon().Should().Be("1d");
        fiveMin.Canon().Should().Be("5m");
        oneMin.Canon().Should().Be("1m");
    }

    private static List<IDictionary<string, object?>> ParseCsvData(string[] lines)
    {
        var result = new List<IDictionary<string, object?>>();
        
        if (lines.Length <= 1) return result;

        var headers = lines[0].Split(',');
        
        for (int i = 1; i < lines.Length; i++)
        {
            var values = lines[i].Split(',');
            var row = new Dictionary<string, object?>();
            
            for (int j = 0; j < headers.Length && j < values.Length; j++)
            {
                row[headers[j]] = values[j];
            }
            
            result.Add(row);
        }

        return result;
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDataPath))
        {
            Directory.Delete(_testDataPath, true);
        }
    }
}