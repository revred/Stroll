using FluentAssertions;
using Stroll.Historical;
using Stroll.Historical.DataProviders;
using Stroll.Historical.Tests.TestData;
using Xunit;

namespace Stroll.Historical.Tests.UnitTests;

public class DataProviderTests
{
    [Fact]
    public async Task TestDataProvider_ShouldReturnTestData()
    {
        // Arrange
        var testData = new List<MarketDataBar>
        {
            new() { Timestamp = new DateTime(2024, 1, 1), Open = 100, High = 105, Low = 95, Close = 103, Volume = 1000000, VWAP = 101 },
            new() { Timestamp = new DateTime(2024, 1, 2), Open = 103, High = 107, Low = 102, Close = 106, Volume = 1200000, VWAP = 104.5 }
        };
        var provider = new TestDataProvider(testData);

        // Act
        var result = await provider.GetHistoricalBarsAsync(
            "TEST", 
            new DateTime(2024, 1, 1), 
            new DateTime(2024, 1, 2));

        // Assert
        result.Should().HaveCount(2);
        result.First().Open.Should().Be(100);
        result.Last().Close.Should().Be(106);
    }

    [Fact]
    public async Task TestDataProvider_ShouldFilterByDateRange()
    {
        // Arrange
        var testData = new List<MarketDataBar>
        {
            new() { Timestamp = new DateTime(2024, 1, 1), Open = 100, High = 105, Low = 95, Close = 103, Volume = 1000000, VWAP = 101 },
            new() { Timestamp = new DateTime(2024, 1, 5), Open = 103, High = 107, Low = 102, Close = 106, Volume = 1200000, VWAP = 104.5 },
            new() { Timestamp = new DateTime(2024, 1, 10), Open = 106, High = 110, Low = 104, Close = 109, Volume = 1300000, VWAP = 107.5 }
        };
        var provider = new TestDataProvider(testData);

        // Act - Request only middle date
        var result = await provider.GetHistoricalBarsAsync(
            "TEST", 
            new DateTime(2024, 1, 3), 
            new DateTime(2024, 1, 7));

        // Assert
        result.Should().HaveCount(1);
        result.First().Close.Should().Be(106);
    }

    [Fact]
    public async Task TestDataProvider_ShouldSimulateFailure()
    {
        // Arrange
        var provider = new TestDataProvider();
        provider.SimulateFailure = true;

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetHistoricalBarsAsync("TEST", DateTime.Today, DateTime.Today));
    }

    [Fact]
    public async Task TestDataProvider_ShouldRespectRateLimit()
    {
        // Arrange
        var provider = new TestDataProvider();
        provider.MaxRequests = 2;

        // Act - First two requests should succeed
        await provider.GetHistoricalBarsAsync("TEST", DateTime.Today, DateTime.Today);
        await provider.GetHistoricalBarsAsync("TEST", DateTime.Today, DateTime.Today);

        // Third request should fail due to rate limit
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetHistoricalBarsAsync("TEST", DateTime.Today, DateTime.Today));
    }

    [Fact]
    public async Task TestDataProvider_HealthCheck_ShouldReturnHealthy()
    {
        // Arrange
        var provider = new TestDataProvider();

        // Act
        var health = await provider.CheckHealthAsync();

        // Assert
        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
        health.ResponseTimeMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TestDataProvider_HealthCheck_ShouldReturnUnhealthy_WhenSimulatingFailure()
    {
        // Arrange
        var provider = new TestDataProvider();
        provider.SimulateFailure = true;

        // Act
        var health = await provider.CheckHealthAsync();

        // Assert
        health.IsHealthy.Should().BeFalse();
        health.ConsecutiveFailures.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TestDataProvider_RateLimitStatus_ShouldTrackRequests()
    {
        // Arrange
        var provider = new TestDataProvider();
        provider.MaxRequests = 10;

        // Act
        var initialStatus = provider.GetRateLimitStatus();

        // Assert
        initialStatus.RequestsRemaining.Should().Be(10);
        initialStatus.RequestsPerMinute.Should().Be(10);
        initialStatus.IsThrottled.Should().BeFalse();
    }

    [Fact]
    public async Task YahooFinanceProvider_ShouldHaveCorrectPriority()
    {
        // Arrange
        var provider = new YahooFinanceProvider();

        // Act & Assert
        provider.ProviderName.Should().Be("Yahoo Finance");
        provider.Priority.Should().Be(1);
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task AlphaVantageProvider_ShouldRequireApiKey()
    {
        // Arrange
        var provider = new AlphaVantageProvider("test-key");

        // Act & Assert
        provider.ProviderName.Should().Be("Alpha Vantage");
        provider.Priority.Should().Be(2);
        provider.IsAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task AlphaVantageProvider_ShouldBeUnavailable_WithoutApiKey()
    {
        // Arrange
        var provider = new AlphaVantageProvider("");

        // Act & Assert
        provider.IsAvailable.Should().BeFalse();
    }
}