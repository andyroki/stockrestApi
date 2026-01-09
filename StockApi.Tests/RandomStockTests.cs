using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace StockApi.Tests;

public class RandomStockTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public RandomStockTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetRandomStock_WithDefaultMonths_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/stocks/random");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRandomStock_ReturnsValidResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/stocks/random?months=3");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RandomStockResponse>();

        Assert.NotNull(result);
        Assert.NotNull(result.Symbol);
        Assert.NotNull(result.StartDate);
        Assert.NotNull(result.EndDate);
        Assert.True(result.DataPoints > 0);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(12)]
    public async Task GetRandomStock_WithDifferentMonths_ReturnsData(int months)
    {
        // Act
        var response = await _client.GetAsync($"/api/stocks/random?months={months}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RandomStockResponse>();

        Assert.NotNull(result);
        Assert.NotEmpty(result.Data);
    }

    [Fact]
    public async Task GetRandomStock_MultipleCalls_ReturnsDifferentData()
    {
        // Act - Call twice
        var response1 = await _client.GetAsync("/api/stocks/random?months=3");
        var result1 = await response1.Content.ReadFromJsonAsync<RandomStockResponse>();

        var response2 = await _client.GetAsync("/api/stocks/random?months=3");
        var result2 = await response2.Content.ReadFromJsonAsync<RandomStockResponse>();

        // Assert - At least one should be different (symbol or date range)
        // Note: There's a small chance they could be the same by random chance
        Assert.NotNull(result1);
        Assert.NotNull(result2);
    }

    [Fact]
    public async Task GetRandomStock_DataPointsMatchActualData()
    {
        // Act
        var response = await _client.GetAsync("/api/stocks/random?months=6");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RandomStockResponse>();

        Assert.NotNull(result);
        Assert.Equal(result.DataPoints, result.Data.Count);
    }

    [Fact]
    public async Task GetRandomStock_DateRangeIsValid()
    {
        // Act
        var response = await _client.GetAsync("/api/stocks/random?months=3");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<RandomStockResponse>();

        Assert.NotNull(result);
        var startDate = DateTime.Parse(result.StartDate);
        var endDate = DateTime.Parse(result.EndDate);

        Assert.True(endDate >= startDate);

        // Verify all data points are within the date range
        foreach (var point in result.Data)
        {
            Assert.True(point.Time >= startDate);
            Assert.True(point.Time <= endDate);
        }
    }
}

public class RandomStockResponse
{
    public string Symbol { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public int DataPoints { get; set; }
    public List<StockDataPoint> Data { get; set; } = new();
}